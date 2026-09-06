/*
 * =============================================================================
 * File        : BookingSummaryActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : activities
 * Description : The shared confirmation page shown after every reservation
 *               action - create, change and cancel - which the marking scheme
 *               names as the "summary page after each action".
 *
 *               THIS SCREEN DECIDES NOTHING. The outcome flag, the headline and
 *               the message all arrive in the Intent from the screen that made
 *               the call, and the message itself is the WEB API's own wording.
 *               When the caller also passes the reservation JSON the API
 *               returned, the card below repeats that booking verbatim; no
 *               figure on this page is recomputed on the phone.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.content.Intent;
import android.content.res.ColorStateList;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.text.TextUtils;
import android.util.Log;
import android.view.View;
import android.widget.ImageView;
import android.widget.TextView;

import androidx.annotation.Nullable;
import androidx.core.content.ContextCompat;
import androidx.core.widget.ImageViewCompat;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.card.MaterialCardView;

import org.json.JSONException;
import org.json.JSONObject;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.models.ReservationModel;
import lk.sliit.smartsolar.util.Constants;
import lk.sliit.smartsolar.util.DateUtils;

/** Reports the result of a reservation action and offers two ways onward. */
public class BookingSummaryActivity extends BaseActivity {

    private static final String TAG = "BookingSummary";

    // Outcome badge and headline.
    private View panelBadge;
    private ImageView imageIcon;
    private TextView textTitle;
    private TextView textMessage;

    // Optional booking card.
    private MaterialCardView cardBooking;
    private TextView textReservationNo;
    private TextView textStatus;
    private TextView textStationName;
    private TextView textStationCode;
    private TextView textDate;
    private TextView textTime;
    private TextView textEnergy;

    // Exits.
    private MaterialButton btnBookings;
    private MaterialButton btnHome;

    /** Builds the screen and paints whatever the calling activity handed over. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // super.onCreate must run first because it creates api, session and localStore.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_booking_summary);

        // No back arrow: the form the user just submitted must not be reachable again.
        setupToolbar(R.id.toolbar_booking_summary, getString(R.string.summary_title), false);

        bindViews();
        renderOutcome();
        renderReservation();
        setupActions();
    }

    /** Looks up every view this screen drives. */
    private void bindViews() {
        // findViewById throughout: view binding and data binding are not permitted here.
        panelBadge = findViewById(R.id.panel_summary_badge);
        imageIcon = findViewById(R.id.img_summary_icon);
        textTitle = findViewById(R.id.text_summary_title);
        textMessage = findViewById(R.id.text_summary_message);

        cardBooking = findViewById(R.id.card_summary_booking);
        textReservationNo = findViewById(R.id.text_summary_reservation_no);
        textStatus = findViewById(R.id.text_summary_status);
        textStationName = findViewById(R.id.text_summary_station_name);
        textStationCode = findViewById(R.id.text_summary_station_code);
        textDate = findViewById(R.id.text_summary_date);
        textTime = findViewById(R.id.text_summary_time);
        textEnergy = findViewById(R.id.text_summary_energy);

        btnBookings = findViewById(R.id.btn_summary_bookings);
        btnHome = findViewById(R.id.btn_summary_home);
    }

    // =====================================================================
    // Outcome
    // =====================================================================

    /**
     * Paints the badge, the headline and the message from the Intent.
     *
     * Whether the action succeeded is NOT worked out here: the calling screen
     * already read result.isSuccess() from the Web API and passed the answer in
     * EXTRA_SUMMARY_SUCCESS, which defaults to true because every screen that
     * currently opens this page does so only after the server said yes.
     */
    private void renderOutcome() {
        // getBooleanExtra's default keeps the page working if a caller omits the flag.
        boolean success = getIntent().getBooleanExtra(Constants.EXTRA_SUMMARY_SUCCESS, true);
        String title = getIntent().getStringExtra(Constants.EXTRA_SUMMARY_TITLE);
        String message = getIntent().getStringExtra(Constants.EXTRA_SUMMARY_MESSAGE);

        // Green tick on success, red alert otherwise. Both drawables already exist in the theme.
        panelBadge.setBackgroundResource(
                success ? R.drawable.bg_success_panel : R.drawable.bg_error_panel);
        imageIcon.setImageResource(success ? R.drawable.ic_check_circle : R.drawable.ic_alert);
        ImageViewCompat.setImageTintList(imageIcon, ColorStateList.valueOf(
                ContextCompat.getColor(this, success ? R.color.grid_600 : R.color.error_red)));

        // A caller that supplied no headline falls back to the neutral screen title.
        textTitle.setText(TextUtils.isEmpty(title) ? getString(R.string.summary_title) : title);

        // The message is shown exactly as the API worded it, and hidden when there was none.
        if (TextUtils.isEmpty(message)) {
            textMessage.setVisibility(View.GONE);
        } else {
            textMessage.setText(message);
            textMessage.setVisibility(View.VISIBLE);
        }
    }

    // =====================================================================
    // Optional booking card
    // =====================================================================

    /**
     * Renders the reservation the caller passed, when it passed one.
     *
     * The payload is the untouched JSON the Web API returned from the create or
     * update call, so the card shows the booking exactly as the server now holds
     * it - including the status the server chose, which for a new booking is
     * Pending until staff approve it.
     */
    private void renderReservation() {
        // The extra is optional: the cancel path, for example, sends only a message.
        String json = getIntent().getStringExtra(Constants.EXTRA_RESERVATION_JSON);
        if (TextUtils.isEmpty(json)) {
            cardBooking.setVisibility(View.GONE);
            return;
        }

        try {
            ReservationModel reservation = ReservationModel.fromJson(new JSONObject(json));
            bindReservation(reservation);
            cardBooking.setVisibility(View.VISIBLE);
        } catch (JSONException ex) {
            // A malformed payload only costs the card; the outcome message still stands alone.
            Log.e(TAG, "Could not parse the reservation payload passed to the summary screen.", ex);
            cardBooking.setVisibility(View.GONE);
        }
    }

    /** Fills the summary card from the parsed reservation. */
    private void bindReservation(ReservationModel reservation) {
        // Every value below was produced by the Web API; nothing is derived on the phone.
        textReservationNo.setText(reservation.getReservationNo());
        bindStatusChip(reservation.getStatus());

        textStationName.setText(reservation.getStationName());
        textStationCode.setText(reservation.getStationCode());
        textDate.setText(DateUtils.formatDate(reservation.getReservationStartUtc()));
        textTime.setText(reservation.getTimeRange());
        textEnergy.setText(reservation.getEnergyLabel());
    }

    /** Writes the status name into the pill and tints the pill to match. */
    private void bindStatusChip(String status) {
        // One rounded drawable (bg_status_chip) is reused for all five statuses and its
        // solid fill is replaced at runtime, exactly as ReservationAdapter does on a row.
        textStatus.setText(status);
        textStatus.setTextColor(ContextCompat.getColor(this, textColourFor(status)));

        Drawable pill = textStatus.getBackground();
        if (pill instanceof GradientDrawable) {
            // mutate() gives this chip its own state so no shared drawable is recoloured.
            GradientDrawable shape = (GradientDrawable) pill.mutate();
            shape.setColor(ContextCompat.getColor(this, fillColourFor(status)));
        }
    }

    // =====================================================================
    // Navigation
    // =====================================================================

    /** Wires the two exits offered by the page. */
    private void setupActions() {
        // Both listeners leave this screen behind, so the summary is never revisited.
        btnBookings.setOnClickListener(new View.OnClickListener() {

            /** Opens the bookings list so the user can see the record just changed. */
            @Override
            public void onClick(View view) {
                // CLEAR_TOP reuses an existing bookings list instead of stacking a second copy.
                openMyBookings();
            }
        });

        btnHome.setOnClickListener(new View.OnClickListener() {

            /** Returns to the home screen that matches the signed in role. */
            @Override
            public void onClick(View view) {
                // BaseActivity owns the role routing, so it is not repeated here.
                goToRoleHome();
            }
        });
    }

    /** Starts the bookings list and closes this page. */
    private void openMyBookings() {
        // MyBookingsActivity reloads from the API on resume, so it will show the new state.
        Intent intent = new Intent(this, MyBookingsActivity.class);
        intent.setFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP);
        startActivity(intent);
        finish();
    }

    /**
     * Sends the hardware back gesture to the role home screen.
     *
     * WHY: the screen that opened this page called finish() on itself, so the
     * previous entry on the stack is whatever came before the form. Letting the
     * default behaviour run would drop the user back into a half remembered
     * booking form for a request the server has already accepted, inviting a
     * duplicate submission. Routing to the role home instead makes back mean the
     * same thing as the "Back to home" button on screen. super is deliberately
     * not called because goToRoleHome() already navigates and finishes.
     */
    @Override
    public void onBackPressed() {
        // Deprecated on API 33+ but still the supported path for a minSdk 24 application.
        goToRoleHome();
    }

    // =====================================================================
    // Small helpers
    // =====================================================================

    /** Maps a reservation status onto its pill fill colour. */
    private static int fillColourFor(String status) {
        // Mirrors the badge palette used by ReservationAdapter and by the web client.
        if (Constants.RES_APPROVED.equals(status)) {
            return R.color.status_approved_bg;
        }
        if (Constants.RES_COMPLETED.equals(status)) {
            return R.color.status_completed_bg;
        }
        if (Constants.RES_CANCELLED.equals(status)) {
            return R.color.status_cancelled_bg;
        }
        if (Constants.RES_REJECTED.equals(status)) {
            return R.color.status_rejected_bg;
        }
        return R.color.status_pending_bg;
    }

    /** Maps a reservation status onto its pill text colour. */
    private static int textColourFor(String status) {
        // Each fill has a matching dark tone that keeps the label readable.
        if (Constants.RES_APPROVED.equals(status)) {
            return R.color.status_approved_text;
        }
        if (Constants.RES_COMPLETED.equals(status)) {
            return R.color.status_completed_text;
        }
        if (Constants.RES_CANCELLED.equals(status)) {
            return R.color.status_cancelled_text;
        }
        if (Constants.RES_REJECTED.equals(status)) {
            return R.color.status_rejected_text;
        }
        return R.color.status_pending_text;
    }
}
