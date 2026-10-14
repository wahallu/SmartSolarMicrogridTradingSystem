/* =============================================================================
 * File        : ProsumerHomeActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Activities / Prosumer Home
 * Description : Presents the prosumer dashboard and main booking shortcuts.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-19
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

import java.util.Calendar;
import java.util.List;
import java.util.Locale;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.adapters.ReservationAdapter;
import lk.sliit.smartsolar.models.DashboardModel;
import lk.sliit.smartsolar.models.ReservationModel;
import lk.sliit.smartsolar.models.UserModel;
import lk.sliit.smartsolar.util.Constants;

/** Dashboard shown to a signed in Solar Prosumer. */
public class ProsumerHomeActivity extends BaseActivity
        implements ReservationAdapter.OnReservationClick {

    // Header widgets.
    private TextView textAvatarInitial;
    private TextView textGreeting;
    private TextView textUserName;
    private TextView textUserNic;

    // Live counter widgets - all four are filled from the API payload.
    private TextView textStatPending;
    private TextView textStatApproved;
    private TextView textStatCompleted;
    private TextView textStatEnergy;

    // Upcoming bookings list and its loading / empty / error companions.
    private RecyclerView recyclerUpcoming;
    private View layoutLoading;
    private View layoutEmpty;
    private View layoutError;
    private TextView textError;

    private SwipeRefreshLayout swipeRefresh;

    /** Builds the screen, wires the actions and shows the cached identity immediately. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // BaseActivity.onCreate creates api, session and localStore before anything else runs.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_prosumer_home);

        bindViews();
        setupRecycler();
        setupActions();
        bindHeader();

        // The list starts in its loading state; onResume performs the first call.
        showOnly(layoutLoading, recyclerUpcoming, layoutEmpty, layoutError);
    }

    /** Re-reads the dashboard so returning from a booking action shows fresh numbers. */
    @Override
    protected void onResume() {
        // A booking created, edited or cancelled on another screen changes these counters,
        // so the figures are always re-fetched rather than remembered from last time.
        super.onResume();
        loadDashboard(false);
    }

    /** Looks up every view this screen touches. */
    private void bindViews() {
        // findViewById is used throughout: view binding and data binding are not permitted.
        textAvatarInitial = findViewById(R.id.text_avatar_initial);
        textGreeting = findViewById(R.id.text_greeting);
        textUserName = findViewById(R.id.text_user_name);
        textUserNic = findViewById(R.id.text_user_nic);

        textStatPending = findViewById(R.id.text_stat_pending);
        textStatApproved = findViewById(R.id.text_stat_approved);
        textStatCompleted = findViewById(R.id.text_stat_completed);
        textStatEnergy = findViewById(R.id.text_stat_energy);

        recyclerUpcoming = findViewById(R.id.recycler_upcoming);
        layoutLoading = findViewById(R.id.layout_loading);
        layoutEmpty = findViewById(R.id.layout_empty);
        layoutError = findViewById(R.id.layout_error);
        textError = findViewById(R.id.text_error);

        swipeRefresh = findViewById(R.id.swipe_refresh);
    }

    /** Prepares the nested bookings list. */
    private void setupRecycler() {
        // The list lives inside a NestedScrollView, so it must not scroll on its own.
        recyclerUpcoming.setLayoutManager(new LinearLayoutManager(this));
        recyclerUpcoming.setNestedScrollingEnabled(false);
        recyclerUpcoming.setHasFixedSize(false);
    }

    /** Wires the pull to refresh gesture, the quick actions and the sign out button. */
    private void setupActions() {
        // Each quick action simply starts the screen that owns that piece of the workflow.
        swipeRefresh.setColorSchemeResources(R.color.solar_500, R.color.grid_500);
        swipeRefresh.setOnRefreshListener(() -> loadDashboard(true));

        ImageButton btnLogout = findViewById(R.id.btn_logout);
        btnLogout.setOnClickListener(v -> performLogout());

        MaterialCardView cardNewBooking = findViewById(R.id.card_new_booking);
        cardNewBooking.setOnClickListener(v -> open(NewBookingActivity.class));

        MaterialCardView cardMyBookings = findViewById(R.id.card_my_bookings);
        cardMyBookings.setOnClickListener(v -> open(MyBookingsActivity.class));

        MaterialCardView cardNearbyNodes = findViewById(R.id.card_nearby_nodes);
        cardNearbyNodes.setOnClickListener(v -> open(StationsMapActivity.class));

        MaterialCardView cardProfile = findViewById(R.id.card_profile);
        cardProfile.setOnClickListener(v -> open(ProfileActivity.class));

        MaterialButton btnRetry = findViewById(R.id.btn_retry);
        btnRetry.setOnClickListener(v -> loadDashboard(false));
    }

    /** Shows the greeting, name and NIC from the session stored in local SQLite. */
    private void bindHeader() {
        // Reading the identity from SQLite means the header is populated before any
        // network call finishes, so the screen never opens blank.
        UserModel user = session.getUser();
        if (user == null) {
            return;
        }
        textAvatarInitial.setText(user.getInitial());
        textGreeting.setText(greetingResource());
        textUserName.setText(user.getDisplayName());
        textUserNic.setText(user.getNic() == null ? "" : user.getNic());
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

    /**
     * Calls GET /reservations/dashboard and paints the result.
     *
     * The endpoint scopes itself to the caller from the JWT, so a prosumer receives
     * only their own figures without the app having to filter anything.
     */
    private void loadDashboard(boolean fromSwipe) {
        // A swipe already shows its own spinner, so the block loading state is skipped then.
        if (!fromSwipe) {
            showOnly(layoutLoading, recyclerUpcoming, layoutEmpty, layoutError);
        }

        api.get(Constants.EP_RESERVATIONS_DASHBOARD, token(), result -> {
            // ApiClient delivers this on the main thread, so views can be touched directly.
            swipeRefresh.setRefreshing(false);

            if (!result.isSuccess()) {
                textError.setText(result.getMessage().isEmpty()
                        ? getString(R.string.error_generic)
                        : result.getMessage());
                showOnly(layoutError, layoutLoading, recyclerUpcoming, layoutEmpty);
                showApiError(result);
                return;
            }

            DashboardModel dashboard = DashboardModel.fromJson(result.getDataObject());
            bindCounters(dashboard);
            bindUpcoming(dashboard.getUpcoming());
        });
    }

    /** Writes the four live counters onto the stat cards. */
    private void bindCounters(DashboardModel dashboard) {
        // Every figure below is a value the Web API calculated. The phone performs no
        // counting, no filtering by status and no arithmetic of its own.
        textStatPending.setText(formatCount(dashboard.getPendingReservations()));
        textStatApproved.setText(formatCount(dashboard.getApprovedFutureReservations()));
        textStatCompleted.setText(formatCount(dashboard.getCompletedReservations()));
        textStatEnergy.setText(dashboard.getEnergyLabel());
    }

    /** Formats a server counter for display. */
    private String formatCount(long value) {
        // Locale aware so the figure reads naturally on a localised device.
        return String.format(Locale.getDefault(), "%d", value);
    }

    /** Shows the upcoming bookings, or the empty state when the API returned none. */
    private void bindUpcoming(List<ReservationModel> upcoming) {
        // The server decides which bookings count as "upcoming" and sends them in order.
        if (upcoming == null || upcoming.isEmpty()) {
            showOnly(layoutEmpty, layoutLoading, recyclerUpcoming, layoutError);
            return;
        }

        recyclerUpcoming.setAdapter(new ReservationAdapter(upcoming, this));
        showOnly(recyclerUpcoming, layoutLoading, layoutEmpty, layoutError);
    }

    /** Opens a booking row in the detail screen. */
    @Override
    public void onClick(ReservationModel reservation) {
        // Only the identifier travels in the intent; the detail screen re-reads the
        // booking from the API so its rule verdicts are always current.
        if (reservation == null) {
            return;
        }
        Intent intent = new Intent(this, BookingDetailActivity.class);
        intent.putExtra(Constants.EXTRA_RESERVATION_ID, reservation.getId());
        startActivity(intent);
    }

    /** Starts one of the quick action screens. */
    private void open(Class<?> target) {
        // Kept in one place so every quick action behaves identically.
        startActivity(new Intent(this, target));
    }
}
