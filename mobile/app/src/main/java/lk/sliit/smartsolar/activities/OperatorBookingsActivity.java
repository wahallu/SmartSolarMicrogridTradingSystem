/* =============================================================================
 * File        : OperatorBookingsActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Activities / Operator Bookings
 * Description : Shows the bookings handled by a Grid Operator.
 * Author      : IT22228062 - Akash
 * Created     : 2026-09-15
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.View;
import android.widget.EditText;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.chip.Chip;
import com.google.android.material.chip.ChipGroup;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.adapters.ReservationAdapter;
import lk.sliit.smartsolar.api.ApiClient;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.models.ReservationModel;
import lk.sliit.smartsolar.models.UserModel;
import lk.sliit.smartsolar.util.Constants;

/** Lists the bookings held at the signed in Grid Operator's microgrid node. */
public class OperatorBookingsActivity extends BaseActivity {

    /**
     * How long the screen waits for typing to stop before it calls the API.
     * 400 ms is long enough to swallow a burst of keystrokes and short enough
     * that the list still feels like it is reacting to the search box.
     */
    private static final long SEARCH_DEBOUNCE_MS = 400L;

    /** Page size asked of the paged endpoint - one node rarely holds more than this. */
    private static final String PAGE_SIZE = "50";

    // Filter widgets.
    private ChipGroup chipsStatus;
    private Chip chipAll;
    private Chip chipPending;
    private Chip chipApproved;
    private Chip chipCompleted;
    private EditText inputSearch;

    // Node badge.
    private View panelNode;
    private TextView textNode;

    // The four screen states.
    private SwipeRefreshLayout swipeRefresh;
    private RecyclerView recyclerBookings;
    private ProgressBar progressBookings;
    private View stateEmpty;
    private View stateError;
    private TextView textError;
    private MaterialButton btnRetry;

    private ReservationAdapter adapter;

    /** Node this operator is posted to; sent as the stationId filter. */
    private String assignedStationId;

    /** Status asked of the API, or an empty string for the "All" chip. */
    private String selectedStatus = "";

    /** Posts the debounced search request onto the main thread. */
    private final Handler searchHandler = new Handler(Looper.getMainLooper());

    /** The pending search request, cancelled and re-queued on every keystroke. */
    private Runnable searchRunnable;

    /** Builds the screen and wires the chips, the search box and the list. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // super.onCreate must run first because it creates api, session and localStore.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_operator_bookings);

        setupToolbar(R.id.toolbar_operator_bookings,
                getString(R.string.action_station_bookings), true);

        bindViews();
        applyChipLabels();
        bindNodeBadge();
        setupRecycler();
        setupChips();
        setupSearch();
        setupRefreshAndRetry();
    }

    /**
     * Reloads on every return to the screen so an approval or a rejection made on
     * the booking detail screen is reflected here without a manual refresh.
     */
    @Override
    protected void onResume() {
        // Also covers the very first appearance, so onCreate does not load separately.
        super.onResume();
        loadBookings(false);
    }

    /** Cancels any queued search so a request can never fire after the screen has gone. */
    @Override
    protected void onDestroy() {
        // The debounce Runnable holds this Activity, so it is explicitly dropped here.
        if (searchRunnable != null) {
            searchHandler.removeCallbacks(searchRunnable);
        }
        super.onDestroy();
    }

    // =====================================================================
    // Setup
    // =====================================================================

    /** Looks up every view this screen drives. */
    private void bindViews() {
        // findViewById is used throughout because view binding is not permitted on this project.
        chipsStatus = findViewById(R.id.chips_operator_status);
        chipAll = findViewById(R.id.chip_operator_all);
        chipPending = findViewById(R.id.chip_operator_pending);
        chipApproved = findViewById(R.id.chip_operator_approved);
        chipCompleted = findViewById(R.id.chip_operator_completed);
        inputSearch = findViewById(R.id.input_operator_search);

        panelNode = findViewById(R.id.panel_operator_node);
        textNode = findViewById(R.id.text_operator_node);

        swipeRefresh = findViewById(R.id.swipe_operator_bookings);
        recyclerBookings = findViewById(R.id.recycler_operator_bookings);
        progressBookings = findViewById(R.id.progress_operator_bookings);
        stateEmpty = findViewById(R.id.state_operator_empty);
        stateError = findViewById(R.id.state_operator_error);
        textError = findViewById(R.id.text_operator_error);
        btnRetry = findViewById(R.id.btn_operator_retry);
    }

    /** Writes the caption onto each filter chip. */
    private void applyChipLabels() {
        // "All" is ordinary UI copy so it comes from strings.xml. The other three are
        // labelled with the API's own ReservationStatus names (Constants.RES_*), which are
        // the exact literals sent as ?status=, so a caption can never drift from the value
        // the server is actually asked for.
        chipAll.setText(getString(R.string.tab_all));
        chipPending.setText(Constants.RES_PENDING);
        chipApproved.setText(Constants.RES_APPROVED);
        chipCompleted.setText(Constants.RES_COMPLETED);
    }

    /** Shows which microgrid node this operator is posted to. */
    private void bindNodeBadge() {
        // The posting is part of the account, so it is read from the session held in local
        // SQLite and the badge is painted before any network call finishes.
        UserModel user = session.getUser();
        assignedStationId = user == null ? null : user.getAssignedStationId();
        String code = user == null ? null : user.getAssignedStationCode();

        if (code == null || code.trim().isEmpty()) {
            // An operator with no posting sees every booking they are entitled to read,
            // so there is no node to name and the badge is hidden.
            panelNode.setVisibility(View.GONE);
            return;
        }
        panelNode.setVisibility(View.VISIBLE);
        textNode.setText(code);
    }

    /** Attaches the layout manager and the adapter to the booking list. */
    private void setupRecycler() {
        // A vertical linear list is the natural shape for one card per booking.
        recyclerBookings.setLayoutManager(new LinearLayoutManager(this));
        adapter = new ReservationAdapter(new ArrayList<ReservationModel>(),
                new ReservationAdapter.OnReservationClick() {

                    /** Opens the tapped booking on the detail screen. */
                    @Override
                    public void onClick(ReservationModel reservation) {
                        // The detail screen re-fetches the booking, so only the id travels.
                        openBookingDetail(reservation);
                    }
                });
        recyclerBookings.setAdapter(adapter);
    }

    /** Maps the four status chips onto the ?status= filter and re-queries on a change. */
    private void setupChips() {
        // Only one chip can be checked at a time (singleSelection in the layout), so the
        // list of checked ids the listener receives holds at most one entry.
        chipsStatus.setOnCheckedStateChangeListener(new ChipGroup.OnCheckedStateChangeListener() {

            /** Re-queries the API with the status belonging to the newly checked chip. */
            @Override
            public void onCheckedChanged(@NonNull ChipGroup group, @NonNull List<Integer> checkedIds) {
                // Filtering by status is the SERVER's job; the app never slices the list itself.
                selectedStatus = checkedIds.isEmpty() ? "" : statusForCheckedId(checkedIds.get(0));
                loadBookings(false);
            }
        });
    }

    /** Converts a checked chip id into the matching Constants.RES_* value. */
    private String statusForCheckedId(int checkedId) {
        // An empty string means "no status filter", which ApiClient.withQuery drops entirely.
        if (checkedId == R.id.chip_operator_pending) {
            return Constants.RES_PENDING;
        }
        if (checkedId == R.id.chip_operator_approved) {
            return Constants.RES_APPROVED;
        }
        if (checkedId == R.id.chip_operator_completed) {
            return Constants.RES_COMPLETED;
        }
        return "";
    }

    /** Wires the search box to the debounced API query. */
    private void setupSearch() {
        // Unlike the prosumer's own booking list, this search is performed BY THE API - the
        // typed text is sent as ?search= so the server matches it against the prosumer name,
        // the NIC and the reservation reference using its own indexes.
        inputSearch.addTextChangedListener(new TextWatcher() {

            /** Required by TextWatcher; nothing is needed before the text changes. */
            @Override
            public void beforeTextChanged(CharSequence s, int start, int count, int after) {
                // Intentionally empty.
            }

            /** Required by TextWatcher; the request is scheduled after the edit is applied. */
            @Override
            public void onTextChanged(CharSequence s, int start, int before, int count) {
                // Intentionally empty.
            }

            /** Queues a fresh query once typing pauses. */
            @Override
            public void afterTextChanged(Editable editable) {
                // Scheduling rather than calling immediately is what creates the debounce.
                scheduleSearch();
            }
        });
    }

    /**
     * Restarts the debounce timer.
     *
     * WHY DEBOUNCE: afterTextChanged fires once per keystroke, so typing a NIC would
     * otherwise launch a dozen HTTP requests whose replies could arrive out of order
     * and repaint the list with a stale search. Cancelling the pending Runnable and
     * re-posting it means exactly ONE request is sent per pause in typing.
     */
    private void scheduleSearch() {
        // removeCallbacks throws away the request queued by the previous keystroke.
        if (searchRunnable != null) {
            searchHandler.removeCallbacks(searchRunnable);
        }

        searchRunnable = new Runnable() {

            /** Runs 400 ms after the last keystroke. */
            @Override
            public void run() {
                // By now the user has stopped typing, so the term is worth a round trip.
                loadBookings(false);
            }
        };
        searchHandler.postDelayed(searchRunnable, SEARCH_DEBOUNCE_MS);
    }

    /** Wires pull to refresh and the retry button on the error state. */
    private void setupRefreshAndRetry() {
        // Both simply ask the API for the current filter combination again.
        swipeRefresh.setColorSchemeResources(R.color.solar_500, R.color.grid_500);
        swipeRefresh.setOnRefreshListener(new SwipeRefreshLayout.OnRefreshListener() {

            /** Reloads the current filters when the user pulls the list down. */
            @Override
            public void onRefresh() {
                // "true" keeps the spinner visible instead of swapping to the loading state.
                loadBookings(true);
            }
        });

        btnRetry.setOnClickListener(new View.OnClickListener() {

            /** Retries the failed request. */
            @Override
            public void onClick(View view) {
                // Same call path as a chip change, so there is only one loading routine.
                loadBookings(false);
            }
        });
    }

    // =====================================================================
    // Data loading
    // =====================================================================

    /**
     * Requests GET /reservations with the node, the selected status and the search
     * term. When fromSwipe is true the SwipeRefreshLayout is already showing its own
     * spinner, so the full screen loading state is skipped.
     */
    private void loadBookings(final boolean fromSwipe) {
        // The loading state is shown first so the user never sees a stale list during a reload.
        if (!fromSwipe) {
            showOnly(progressBookings, swipeRefresh, stateEmpty, stateError);
        }

        // ApiClient.withQuery URL encodes each value and silently drops any that is blank,
        // so an empty status or an empty search term simply leaves that filter out.
        Map<String, String> query = new LinkedHashMap<>();
        query.put("stationId", assignedStationId);
        query.put("status", selectedStatus);
        query.put("search", currentSearchTerm());
        query.put("pageSize", PAGE_SIZE);
        String path = ApiClient.withQuery(Constants.EP_RESERVATIONS, query);

        api.get(path, token(), new ApiClient.Callback() {

            /** Receives the server reply on the main thread. */
            @Override
            public void onResult(ApiResult result) {
                // ApiClient posts callbacks to the main looper, so views may be touched directly.
                swipeRefresh.setRefreshing(false);

                if (result.isSuccess()) {
                    handleLoadSuccess(result);
                } else {
                    handleLoadFailure(result);
                }
            }
        });
    }

    /** Reads the rows out of the paged payload and renders them. */
    private void handleLoadSuccess(ApiResult result) {
        // GET /reservations is a PAGED endpoint: its "data" is the wrapper object
        // {items, page, pageSize, totalCount, totalPages}, not a bare array. The rows are
        // therefore read with getPagedItems() rather than getDataArray(), which is what the
        // unpaged /reservations/my endpoint used by the prosumer screens returns.
        List<ReservationModel> rows = ReservationModel.fromJsonArray(result.getPagedItems());

        // DELIBERATELY NOT CACHED: the local SQLite reservation cache belongs to the signed
        // in prosumer's own bookings. Node wide staff data is always read live from the API.
        renderList(rows);
    }

    /** Shows the error state with the wording the server itself supplied. */
    private void handleLoadFailure(ApiResult result) {
        // An expired session is dealt with centrally by BaseActivity.
        if (handleAuthFailure(result)) {
            return;
        }

        textError.setText(result.getMessage().isEmpty()
                ? getString(R.string.error_generic)
                : result.getMessage());
        showOnly(stateError, progressBookings, swipeRefresh, stateEmpty);
        showApiError(result);
    }

    /** Shows the supplied rows, or the empty state when the query matched nothing. */
    private void renderList(List<ReservationModel> rows) {
        // showOnly comes from BaseActivity so all list screens handle their states identically.
        adapter.updateData(rows);

        if (rows.isEmpty()) {
            showOnly(stateEmpty, progressBookings, swipeRefresh, stateError);
        } else {
            showOnly(swipeRefresh, progressBookings, stateEmpty, stateError);
        }
    }

    /** Current contents of the search box, trimmed. */
    private String currentSearchTerm() {
        // A blank term is dropped from the query string by ApiClient.withQuery.
        return inputSearch.getText() == null ? "" : inputSearch.getText().toString().trim();
    }

    /** Opens the booking detail screen for one reservation. */
    private void openBookingDetail(ReservationModel reservation) {
        // Only the identifier is passed. The detail screen asks the API for the live record so
        // the verdicts it acts on - canModify() and canCancel(), the SERVER's answers to the 12
        // hour notice rule - are always the latest, never a value cached in this list.
        if (reservation == null || reservation.getId() == null) {
            return;
        }
        Intent intent = new Intent(this, BookingDetailActivity.class);
        intent.putExtra(Constants.EXTRA_RESERVATION_ID, reservation.getId());
        startActivity(intent);
    }
}
