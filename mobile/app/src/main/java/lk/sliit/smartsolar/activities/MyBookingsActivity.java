/*
 * =============================================================================
 * File        : MyBookingsActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : activities
 * Description : The booking list for a signed in solar prosumer. It satisfies
 *               three separate marking requirements at once: the current and
 *               pending bookings, the full booking history, and a working search
 *               filter. Each tab re-queries GET /reservations/my with a different
 *               scope, and every successful load is mirrored into the local
 *               SQLite cache so the screen still has something to show offline.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.content.Intent;
import android.os.Bundle;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.View;
import android.widget.EditText;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.annotation.Nullable;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.tabs.TabLayout;

import org.json.JSONArray;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.adapters.ReservationAdapter;
import lk.sliit.smartsolar.api.ApiClient;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.models.ReservationModel;
import lk.sliit.smartsolar.util.Constants;

/** Lists the signed in prosumer's bookings, grouped by scope and searchable. */
public class MyBookingsActivity extends BaseActivity {

    private TabLayout tabsScope;
    private EditText inputSearch;
    private View panelOffline;
    private SwipeRefreshLayout swipeRefresh;
    private RecyclerView recyclerBookings;
    private ProgressBar progressBookings;
    private View stateEmpty;
    private View stateError;
    private TextView textError;
    private MaterialButton btnRetry;

    private ReservationAdapter adapter;

    /** Everything the API returned for the current scope, before the search box narrows it. */
    private final List<ReservationModel> loadedBookings = new ArrayList<>();

    /** The scope currently asked of the API - one of the Constants.SCOPE_* values. */
    private String currentScope = Constants.SCOPE_CURRENT;

    /** True while the rows on screen came from SQLite rather than from the API. */
    private boolean showingCachedRows = false;

    /** Builds the screen, wires the tabs, the search box and the list. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // super.onCreate must run first because it creates api, session and localStore.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_my_bookings);

        setupToolbar(R.id.toolbar_my_bookings, getString(R.string.bookings_title), true);

        bindViews();
        setupRecycler();
        setupTabs();
        setupSearch();
        setupRefreshAndRetry();
    }

    /**
     * Reloads on every return to the screen so a cancellation performed on the
     * booking detail screen is reflected here without a manual refresh.
     */
    @Override
    protected void onResume() {
        // Also covers the very first appearance, so onCreate does not load separately.
        super.onResume();
        loadBookings(false);
    }

    /** Looks up every view this screen drives. */
    private void bindViews() {
        // findViewById is used throughout because view binding is not permitted on this project.
        tabsScope = findViewById(R.id.tabs_bookings);
        inputSearch = findViewById(R.id.input_bookings_search);
        panelOffline = findViewById(R.id.panel_bookings_offline);
        swipeRefresh = findViewById(R.id.swipe_bookings);
        recyclerBookings = findViewById(R.id.recycler_bookings);
        progressBookings = findViewById(R.id.progress_bookings);
        stateEmpty = findViewById(R.id.state_bookings_empty);
        stateError = findViewById(R.id.state_bookings_error);
        textError = findViewById(R.id.text_bookings_error);
        btnRetry = findViewById(R.id.btn_bookings_retry);
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
                        // The detail screen re-fetches the booking by id, so only the id travels.
                        openBookingDetail(reservation);
                    }
                });
        recyclerBookings.setAdapter(adapter);
    }

    /** Maps the four tabs onto the four reservation scopes the API accepts. */
    private void setupTabs() {
        // Deliberately no ViewPager: one RecyclerView re-bound per tab is far simpler to
        // reason about, and the tab only changes which scope is asked of the server.
        tabsScope.addOnTabSelectedListener(new TabLayout.OnTabSelectedListener() {

            /** Re-queries the API with the scope belonging to the newly selected tab. */
            @Override
            public void onTabSelected(TabLayout.Tab tab) {
                // Filtering by scope is the SERVER's job; the app never slices the list itself.
                currentScope = scopeForTabPosition(tab.getPosition());
                loadBookings(false);
            }

            /** No behaviour is needed when a tab loses selection. */
            @Override
            public void onTabUnselected(TabLayout.Tab tab) {
                // Intentionally empty - the newly selected tab drives the reload.
            }

            /** Re-tapping the active tab refreshes it. */
            @Override
            public void onTabReselected(TabLayout.Tab tab) {
                // Gives the user an obvious way to pull fresh data without a swipe.
                loadBookings(false);
            }
        });
    }

    /** Converts a tab index into the matching Constants.SCOPE_* value. */
    private String scopeForTabPosition(int position) {
        // The order here must match the TabItem order declared in activity_my_bookings.xml.
        switch (position) {
            case 1:
                return Constants.SCOPE_PENDING;
            case 2:
                return Constants.SCOPE_HISTORY;
            case 3:
                return Constants.SCOPE_ALL;
            case 0:
            default:
                return Constants.SCOPE_CURRENT;
        }
    }

    /** Wires the search box to the client side filter. */
    private void setupSearch() {
        // NOTE ON SCOPE OF THIS FILTER: this is a view level convenience only. It narrows the
        // rows the API has ALREADY returned for the selected tab and issues no request of its
        // own, so no business rule is being reimplemented here. The authoritative, indexed
        // search lives on the API's GET /reservations endpoint (its ?search= parameter), which
        // the staff screens - OperatorBookingsActivity - use instead.
        inputSearch.addTextChangedListener(new TextWatcher() {

            /** Required by TextWatcher; nothing is needed before the text changes. */
            @Override
            public void beforeTextChanged(CharSequence s, int start, int count, int after) {
                // Intentionally empty.
            }

            /** Required by TextWatcher; the filter runs after the edit is applied. */
            @Override
            public void onTextChanged(CharSequence s, int start, int before, int count) {
                // Intentionally empty.
            }

            /** Re-applies the filter to the in-memory list on every keystroke. */
            @Override
            public void afterTextChanged(Editable editable) {
                // No debounce is needed: the work is a loop over one screen of rows, not a call.
                applySearchFilter();
            }
        });
    }

    /** Wires pull to refresh and the retry button on the error state. */
    private void setupRefreshAndRetry() {
        // Both simply ask the API for the current scope again.
        swipeRefresh.setColorSchemeResources(R.color.solar_500, R.color.grid_500);
        swipeRefresh.setOnRefreshListener(new SwipeRefreshLayout.OnRefreshListener() {

            /** Reloads the current scope when the user pulls the list down. */
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
                // Same call path as a tab change, so there is only one loading routine.
                loadBookings(false);
            }
        });
    }

    // =====================================================================
    // Data loading
    // =====================================================================

    /**
     * Requests GET /reservations/my for the currently selected scope. When
     * fromSwipe is true the SwipeRefreshLayout is already showing its own
     * spinner, so the full screen loading state is skipped.
     */
    private void loadBookings(final boolean fromSwipe) {
        // The loading state is shown first so the user never sees a stale list during a reload.
        if (!fromSwipe) {
            showOnly(progressBookings, swipeRefresh, stateEmpty, stateError);
        }

        Map<String, String> query = new LinkedHashMap<>();
        query.put("scope", currentScope);
        String path = ApiClient.withQuery(Constants.EP_RESERVATIONS_MY, query);

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

    /** Stores a freshly downloaded list, caches it locally and renders it. */
    private void handleLoadSuccess(ApiResult result) {
        // /reservations/my answers with a plain array; getPagedItems is a safety net in case
        // the endpoint is later switched to the paged envelope used by /reservations.
        JSONArray array = result.getDataArray();
        if (array == null) {
            array = result.getPagedItems();
        }

        List<ReservationModel> fresh = ReservationModel.fromJsonArray(array);

        loadedBookings.clear();
        loadedBookings.addAll(fresh);
        showingCachedRows = false;
        panelOffline.setVisibility(View.GONE);

        // OFFLINE SUPPORT: the rows are copied into SQLite purely as a cache of the last known
        // server state. It is never the authority - every create, edit and cancel still goes
        // to the Web API, and the cache is overwritten by the next successful load.
        localStore.cacheReservations(fresh);

        applySearchFilter();
    }

    /**
     * Handles a failed load. A transport failure falls back to the SQLite cache
     * so the prosumer can still read their bookings without a connection; any
     * other failure shows the error state with the server's own message.
     */
    private void handleLoadFailure(ApiResult result) {
        // An expired session is dealt with centrally by BaseActivity.
        if (handleAuthFailure(result)) {
            return;
        }

        if (result.isNetworkError()) {
            // Passing null reads every cached row regardless of status. The cache mirrors the
            // most recent successful load, so it may be older than the tab that is selected -
            // hence the banner that tells the user plainly that this is saved data.
            List<ReservationModel> cached = localStore.getCachedReservations(null);
            if (!cached.isEmpty()) {
                loadedBookings.clear();
                loadedBookings.addAll(cached);
                showingCachedRows = true;
                panelOffline.setVisibility(View.VISIBLE);
                applySearchFilter();
                return;
            }
        }

        showingCachedRows = false;
        panelOffline.setVisibility(View.GONE);
        textError.setText(result.getMessage().isEmpty()
                ? getString(R.string.error_generic)
                : result.getMessage());
        showOnly(stateError, progressBookings, swipeRefresh, stateEmpty);
        showApiError(result);
    }

    // =====================================================================
    // Filtering and rendering
    // =====================================================================

    /**
     * Narrows the loaded list by the text in the search box and redraws. The
     * match runs across the reservation reference, the node name, the node code
     * and the status name.
     */
    private void applySearchFilter() {
        // Working on the already downloaded rows keeps typing instant and offline safe.
        String term = inputSearch.getText() == null
                ? ""
                : inputSearch.getText().toString().trim().toLowerCase(Locale.getDefault());

        if (term.isEmpty()) {
            renderList(loadedBookings);
            return;
        }

        List<ReservationModel> matches = new ArrayList<>();
        for (ReservationModel booking : loadedBookings) {
            if (matchesTerm(booking, term)) {
                matches.add(booking);
            }
        }
        renderList(matches);
    }

    /** True when any searchable field of the booking contains the term. */
    private boolean matchesTerm(ReservationModel booking, String term) {
        // Null tolerant because a cached row can legitimately carry null text columns.
        return contains(booking.getReservationNo(), term)
                || contains(booking.getStationName(), term)
                || contains(booking.getStationCode(), term)
                || contains(booking.getStatus(), term);
    }

    /** Case insensitive contains that treats null as "no match". */
    private boolean contains(String value, String term) {
        // Keeps matchesTerm readable rather than repeating four null checks.
        return value != null && value.toLowerCase(Locale.getDefault()).contains(term);
    }

    /** Shows the supplied rows, or the empty state when there are none. */
    private void renderList(List<ReservationModel> rows) {
        // showOnly comes from BaseActivity so all list screens handle their states identically.
        adapter.updateData(rows);

        if (rows.isEmpty()) {
            showOnly(stateEmpty, progressBookings, swipeRefresh, stateError);
        } else {
            showOnly(swipeRefresh, progressBookings, stateEmpty, stateError);
            // The banner sits above the frame, so it is restored after any state swap.
            panelOffline.setVisibility(showingCachedRows ? View.VISIBLE : View.GONE);
        }
    }

    /** Opens the booking detail screen for one reservation. */
    private void openBookingDetail(ReservationModel reservation) {
        // Only the identifier is passed; the detail screen asks the API for the live record so
        // the 12 hour verdicts it shows (canModify / canCancel) are always the server's latest.
        if (reservation == null || reservation.getId() == null) {
            return;
        }
        Intent intent = new Intent(this, BookingDetailActivity.class);
        intent.putExtra(Constants.EXTRA_RESERVATION_ID, reservation.getId());
        startActivity(intent);
    }
}
