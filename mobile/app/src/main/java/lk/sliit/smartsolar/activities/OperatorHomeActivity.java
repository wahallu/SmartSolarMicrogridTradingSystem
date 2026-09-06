/*
 * =============================================================================
 * File        : OperatorHomeActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : activities
 * Description : Console shown to a signed in Grid Operator. It names the node the
 *               operator is assigned to, shows the node scoped counters returned
 *               by GET /reservations/dashboard, gives the QR scanner - the
 *               operator's main job - a full width primary card, and lists the
 *               bookings at that node that are still waiting for approval.
 *
 *               This screen is a pure UI layer. Every counter and every booking
 *               row is a value the Web API calculated; nothing is counted,
 *               scored or ruled upon here.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.content.Intent;
import android.os.Bundle;
import android.view.View;
import android.widget.ImageButton;
import android.widget.TextView;

import androidx.annotation.Nullable;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;
import androidx.swiperefreshlayout.widget.SwipeRefreshLayout;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.card.MaterialCardView;

import java.util.ArrayList;
import java.util.Calendar;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.adapters.ReservationAdapter;
import lk.sliit.smartsolar.api.ApiClient;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.models.DashboardModel;
import lk.sliit.smartsolar.models.ReservationModel;
import lk.sliit.smartsolar.models.UserModel;
import lk.sliit.smartsolar.util.Constants;

/** Dashboard and daily working surface for a signed in Grid Operator. */
public class OperatorHomeActivity extends BaseActivity {

    // Header widgets - identity of the operator and of the node they staff.
    private TextView textAvatarInitial;
    private TextView textGreeting;
    private TextView textUserName;
    private TextView textStationCode;

    // Node scoped counters, every one of them filled from the API payload.
    private TextView textStatPending;
    private TextView textStatApproved;
    private TextView textStatCompleted;
    private TextView textStatToday;
    private TextView textStatEnergy;

    // The "awaiting approval" list and its loading / empty / error companions.
    private RecyclerView recyclerPending;
    private View layoutLoading;
    private View layoutEmpty;
    private View layoutError;
    private TextView textError;

    private SwipeRefreshLayout swipeRefresh;

    /** Adapter over the pending bookings shown beneath the quick actions. */
    private ReservationAdapter adapter;

    /** Builds the console, wires its actions and paints the cached identity at once. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // BaseActivity.onCreate creates api, session and localStore before anything else runs.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_operator_home);

        bindViews();
        setupRecycler();
        setupActions();
        bindHeader();

        // The list opens in its loading state; onResume performs the first call.
        showOnly(layoutLoading, recyclerPending, layoutEmpty, layoutError);
    }

    /** Re-reads the dashboard so a scan or an approval elsewhere shows fresh numbers. */
    @Override
    protected void onResume() {
        // Approving, rejecting or finalising a booking on another screen moves these
        // counters, so the figures are always re-fetched rather than remembered.
        super.onResume();
        loadDashboard(false);
    }

    /** Looks up every view this screen touches. */
    private void bindViews() {
        // findViewById is used throughout: view binding and data binding are not permitted.
        textAvatarInitial = findViewById(R.id.text_avatar_initial);
        textGreeting = findViewById(R.id.text_greeting);
        textUserName = findViewById(R.id.text_user_name);
        textStationCode = findViewById(R.id.text_station_code);

        textStatPending = findViewById(R.id.text_stat_pending);
        textStatApproved = findViewById(R.id.text_stat_approved);
        textStatCompleted = findViewById(R.id.text_stat_completed);
        textStatToday = findViewById(R.id.text_stat_today);
        textStatEnergy = findViewById(R.id.text_stat_energy);

        recyclerPending = findViewById(R.id.recycler_pending);
        layoutLoading = findViewById(R.id.layout_loading);
        layoutEmpty = findViewById(R.id.layout_empty);
        layoutError = findViewById(R.id.layout_error);
        textError = findViewById(R.id.text_error);

        swipeRefresh = findViewById(R.id.swipe_refresh);
    }

    /** Prepares the nested "awaiting approval" list. */
    private void setupRecycler() {
        // The list sits inside a NestedScrollView, so it must not scroll on its own.
        recyclerPending.setLayoutManager(new LinearLayoutManager(this));
        recyclerPending.setNestedScrollingEnabled(false);
        recyclerPending.setHasFixedSize(false);

        adapter = new ReservationAdapter(new ArrayList<ReservationModel>(),
                new ReservationAdapter.OnReservationClick() {

                    /** Opens the tapped booking on the detail screen. */
                    @Override
                    public void onClick(ReservationModel reservation) {
                        // The detail screen re-fetches the booking by id, so only the id travels.
                        openBookingDetail(reservation);
                    }
                });
        recyclerPending.setAdapter(adapter);
    }

    /** Wires pull to refresh, the scanner card, the secondary actions and sign out. */
    private void setupActions() {
        // Each card simply starts the screen that owns that piece of the operator workflow.
        swipeRefresh.setColorSchemeResources(R.color.solar_500, R.color.grid_500);
        swipeRefresh.setOnRefreshListener(new SwipeRefreshLayout.OnRefreshListener() {

            /** Reloads the dashboard when the operator pulls the page down. */
            @Override
            public void onRefresh() {
                // "true" keeps the swipe spinner visible instead of blanking the list.
                loadDashboard(true);
            }
        });

        ImageButton btnLogout = findViewById(R.id.btn_logout);
        btnLogout.setOnClickListener(new View.OnClickListener() {

            /** Signs the operator out through the shared confirmation flow. */
            @Override
            public void onClick(View view) {
                // performLogout lives in BaseActivity so every screen signs out identically.
                performLogout();
            }
        });

        // The scanner is the operator's primary task, so it gets the large amber card.
        MaterialCardView cardScan = findViewById(R.id.card_scan);
        cardScan.setOnClickListener(new View.OnClickListener() {

            /** Opens the QR scanner. */
            @Override
            public void onClick(View view) {
                // The scanned token is posted to the API, which alone decides whether it is valid.
                open(ScannerActivity.class);
            }
        });

        MaterialCardView cardStationBookings = findViewById(R.id.card_station_bookings);
        cardStationBookings.setOnClickListener(new View.OnClickListener() {

            /** Opens the full booking list for this node. */
            @Override
            public void onClick(View view) {
                // The list screen performs its own server side search and filtering.
                open(OperatorBookingsActivity.class);
            }
        });

        MaterialCardView cardNearbyNodes = findViewById(R.id.card_nearby_nodes);
        cardNearbyNodes.setOnClickListener(new View.OnClickListener() {

            /** Opens the map of nearby microgrid nodes. */
            @Override
            public void onClick(View view) {
                // Useful when an operator has to redirect a prosumer to another node.
                open(StationsMapActivity.class);
            }
        });

        MaterialCardView cardProfile = findViewById(R.id.card_profile);
        cardProfile.setOnClickListener(new View.OnClickListener() {

            /** Opens the operator's own profile. */
            @Override
            public void onClick(View view) {
                // Shared with the prosumer role; the screen adapts to the signed in account.
                open(ProfileActivity.class);
            }
        });

        MaterialButton btnRetry = findViewById(R.id.btn_retry);
        btnRetry.setOnClickListener(new View.OnClickListener() {

            /** Retries a failed dashboard load. */
            @Override
            public void onClick(View view) {
                // Same call path as onResume, so there is only one loading routine.
                loadDashboard(false);
            }
        });
    }

    /** Shows the greeting, the operator's name, their role and their assigned node. */
    private void bindHeader() {
        // Reading the identity from the SQLite session means the header is populated
        // before any network call finishes, so the console never opens blank.
        UserModel user = session.getUser();
        if (user == null) {
            return;
        }

        textAvatarInitial.setText(user.getInitial());
        textGreeting.setText(greetingResource());
        textUserName.setText(user.getDisplayName());

        // The role and the assigned node code both come from the session user the API
        // issued at login; the phone never decides which node an operator staffs.
        textStationCode.setText(roleAndNodeLabel(user));
    }

    /** Builds the "Grid operator - MG-COL-01" line beneath the operator's name. */
    private String roleAndNodeLabel(UserModel user) {
        // An operator with no assigned node is a legitimate back office arrangement, so the
        // label says so plainly rather than showing an empty space.
        String roleLabel = user.isGridOperator() ? "Grid operator" : user.getRole();
        String code = user.getAssignedStationCode();

        if (code == null || code.trim().isEmpty()) {
            return roleLabel + " - no node assigned";
        }
        return roleLabel + " - " + code.trim();
    }

    /** Picks the greeting that matches the local hour of the day. */
    private int greetingResource() {
        // Purely cosmetic wording; the three phrases live in strings.xml.
        int hour = Calendar.getInstance().get(Calendar.HOUR_OF_DAY);
        if (hour < 12) {
            return R.string.greeting_morning;
        }
        if (hour < 17) {
            return R.string.greeting_afternoon;
        }
        return R.string.greeting_evening;
    }

    // =====================================================================
    // Data loading
    // =====================================================================

    /**
     * Calls GET /reservations/dashboard and paints the result.
     *
     * SCOPE, AND WHY IT DIFFERS FROM THE PROSUMER HOME SCREEN: a prosumer needs no
     * parameters at all, because the endpoint narrows itself to the caller read from
     * the JWT and returns that one person's figures. A grid operator is staff, so the
     * same endpoint would otherwise answer with system wide totals across every node.
     * Passing the operator's own assignedStationId as ?stationId= asks the API to
     * restrict the same counters to the node that operator actually works at, which is
     * the only view that is useful at the gate. The narrowing is still performed by the
     * SERVER - the app sends an identifier and displays whatever comes back, and never
     * filters or re-totals the numbers itself.
     */
    private void loadDashboard(final boolean fromSwipe) {
        // A swipe already shows its own spinner, so the block loading state is skipped then.
        if (!fromSwipe) {
            showOnly(layoutLoading, recyclerPending, layoutEmpty, layoutError);
        }

        UserModel user = session.getUser();
        String stationId = user == null ? null : user.getAssignedStationId();

        // withQuery drops a null or blank value, so an unassigned operator simply sends no
        // filter and receives the unscoped figures the API decides are appropriate.
        Map<String, String> query = new LinkedHashMap<>();
        query.put("stationId", stationId);
        String path = ApiClient.withQuery(Constants.EP_RESERVATIONS_DASHBOARD, query);

        api.get(path, token(), new ApiClient.Callback() {

            /** Receives the server reply on the main thread. */
            @Override
            public void onResult(ApiResult result) {
                // ApiClient posts callbacks to the main looper, so views may be touched directly.
                swipeRefresh.setRefreshing(false);

                if (!result.isSuccess()) {
                    handleLoadFailure(result);
                    return;
                }

                DashboardModel dashboard = DashboardModel.fromJson(result.getDataObject());
                bindCounters(dashboard);
                bindAwaitingApproval(dashboard.getUpcoming());
            }
        });
    }

    /** Shows the error state and the server's own wording when a load fails. */
    private void handleLoadFailure(ApiResult result) {
        // The API writes its messages for end users, so they are preferred over anything
        // the app could invent. An expired session is handled centrally by showApiError.
        textError.setText(result.getMessage().isEmpty()
                ? getString(R.string.error_generic)
                : result.getMessage());
        showOnly(layoutError, layoutLoading, recyclerPending, layoutEmpty);
        showApiError(result);
    }

    /** Writes the node scoped counters onto the stat cards. */
    private void bindCounters(DashboardModel dashboard) {
        // Every figure below was calculated by the Web API for the requested station scope.
        // The phone performs no counting, no status filtering and no arithmetic of its own.
        textStatPending.setText(formatCount(dashboard.getPendingReservations()));
        textStatApproved.setText(formatCount(dashboard.getApprovedFutureReservations()));
        textStatCompleted.setText(formatCount(dashboard.getCompletedReservations()));
        textStatToday.setText(todayLabel(dashboard.getTodayReservations()));
        textStatEnergy.setText(dashboard.getEnergyLabel());
    }

    /** Formats a server counter for display. */
    private String formatCount(long value) {
        // Locale aware so the figure reads naturally on a localised device.
        return String.format(Locale.getDefault(), "%d", value);
    }

    /** Formats the "3 today" line under the completed counter. */
    private String todayLabel(long todayCount) {
        // todayReservations arrives from the dashboard payload; only the wording is local.
        return String.format(Locale.getDefault(), "%d today", todayCount);
    }

    /**
     * Shows the bookings from the dashboard's "upcoming" preview that are still
     * waiting for a decision, or the empty state when there are none.
     */
    private void bindAwaitingApproval(List<ReservationModel> upcoming) {
        // NOTE ON THIS SELECTION: the API already decided each booking's status and shipped
        // it on the row. Reading that status name to group the preview into one section is a
        // display choice, not a business rule - the app never decides what may be approved,
        // and the approve and reject actions themselves are PATCH calls the SERVER rules on.
        // The authoritative pending figure remains the dashboard's own pendingReservations
        // counter above; this strip is only the first few rows the payload carried.
        List<ReservationModel> awaiting = new ArrayList<>();
        if (upcoming != null) {
            for (ReservationModel reservation : upcoming) {
                if (reservation != null && reservation.isPending()) {
                    awaiting.add(reservation);
                }
            }
        }

        adapter.updateData(awaiting);

        if (awaiting.isEmpty()) {
            showOnly(layoutEmpty, layoutLoading, recyclerPending, layoutError);
            return;
        }
        showOnly(recyclerPending, layoutLoading, layoutEmpty, layoutError);
    }

    // =====================================================================
    // Navigation
    // =====================================================================

    /** Opens one booking on the detail screen. */
    private void openBookingDetail(ReservationModel reservation) {
        // Only the identifier is passed. The detail screen asks the API for the live record
        // so the verdicts it shows - canModify and canCancel, the server's answer on the 12
        // hour notice rule - are always the latest ones, never a stale copy from this list.
        if (reservation == null || reservation.getId() == null || reservation.getId().isEmpty()) {
            return;
        }
        Intent intent = new Intent(this, BookingDetailActivity.class);
        intent.putExtra(Constants.EXTRA_RESERVATION_ID, reservation.getId());
        startActivity(intent);
    }

    /** Starts one of the console's action screens. */
    private void open(Class<?> target) {
        // Kept in one place so every card on this screen behaves identically.
        startActivity(new Intent(this, target));
    }
}
