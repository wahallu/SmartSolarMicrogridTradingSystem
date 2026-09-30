/*
 * =============================================================================
 * File        : VerifyResultActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : activities
 * Description : The second half of the "read QR code and update the job as done"
 *               requirement. It displays the booking the Web API returned for a
 *               verified QR token, then lets the grid operator record what was
 *               actually delivered and close the job.
 *
 *               THIS SCREEN IS A UI LAYER ONLY. It did not decide that the code
 *               was valid: the scanner posted the token to the API and rule R9
 *               in the FAT service produced this reservation, which arrives here
 *               as untouched JSON and is only rendered. Finalising is the same
 *               story - POST reservations/complete performs the state change,
 *               writes the audit fields and retires the QR token, and this
 *               screen simply reports the server's own message afterwards.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.content.Intent;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.text.TextUtils;
import android.util.Log;
import android.view.View;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.annotation.Nullable;
import androidx.core.content.ContextCompat;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.textfield.TextInputEditText;
import com.google.android.material.textfield.TextInputLayout;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.Locale;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.api.ApiClient;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.models.ReservationModel;
import lk.sliit.smartsolar.util.Constants;
import lk.sliit.smartsolar.util.DateUtils;

/** Shows a QR verified booking and finalises its energy transfer. */
public class VerifyResultActivity extends BaseActivity {

    private static final String TAG = "VerifyResult";

    // Booking card.
    private TextView textReservationNo;
    private TextView textStatus;
    private TextView textProsumerName;
    private TextView textProsumerNic;
    private TextView textStationName;
    private TextView textStationCode;
    private TextView textSlotCode;
    private TextView textDate;
    private TextView textTime;
    private TextView textType;
    private TextView textEnergy;

    // Finalise form.
    private TextInputLayout tilDelivered;
    private TextInputEditText inputDelivered;
    private TextInputEditText inputNotes;
    private ProgressBar progressVerify;
    private MaterialButton btnFinalise;

    /** The reservation the Web API returned for the scanned token. */
    private ReservationModel reservation;

    /**
     * The exact text that was scanned or typed. It is sent back unchanged when
     * the transfer is finalised, because the token is how the server identifies
     * which booking this operator is closing.
     */
    private String qrToken;

    /** True while POST reservations/complete is in flight. */
    private boolean finalising;

    /** Builds the screen from the payload the scanner handed over. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // super.onCreate must run first because it creates api, session and localStore.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_verify_result);

        setupToolbar(R.id.toolbar_verify_result, getString(R.string.verify_title), true);

        bindViews();

        qrToken = getIntent().getStringExtra(Constants.EXTRA_QR_TOKEN);
        reservation = readReservationExtra();

        if (reservation == null || TextUtils.isEmpty(qrToken)) {
            // Without both halves there is nothing to finalise, so the screen closes cleanly.
            toast(getString(R.string.error_generic));
            finish();
            return;
        }

        bindReservation();
        setupActions();
    }

    /** Looks up every view this screen drives. */
    private void bindViews() {
        // findViewById throughout: view binding and data binding are not permitted here.
        textReservationNo = findViewById(R.id.text_verify_reservation_no);
        textStatus = findViewById(R.id.text_verify_status);
        textProsumerName = findViewById(R.id.text_verify_prosumer_name);
        textProsumerNic = findViewById(R.id.text_verify_prosumer_nic);
        textStationName = findViewById(R.id.text_verify_station_name);
        textStationCode = findViewById(R.id.text_verify_station_code);
        textSlotCode = findViewById(R.id.text_verify_slot_code);
        textDate = findViewById(R.id.text_verify_date);
        textTime = findViewById(R.id.text_verify_time);
        textType = findViewById(R.id.text_verify_type);
        textEnergy = findViewById(R.id.text_verify_energy);

        tilDelivered = findViewById(R.id.til_verify_delivered);
        inputDelivered = findViewById(R.id.input_verify_delivered);
        inputNotes = findViewById(R.id.input_verify_notes);
        progressVerify = findViewById(R.id.progress_verify);
        btnFinalise = findViewById(R.id.btn_verify_finalise);
    }

    /**
     * Rebuilds the reservation from the JSON the scanner passed in the Intent.
     *
     * The payload is the untouched body of the verify-qr response, so the model
     * built here holds exactly the fields the Web API decided to disclose.
     */
    private ReservationModel readReservationExtra() {
        // A missing or malformed payload returns null and the caller closes the screen.
        String json = getIntent().getStringExtra(Constants.EXTRA_RESERVATION_JSON);
        if (TextUtils.isEmpty(json)) {
            return null;
        }

        try {
            return ReservationModel.fromJson(new JSONObject(json));
        } catch (JSONException ex) {
            // Nothing can be shown or finalised from a payload that will not parse.
            Log.e(TAG, "Could not parse the reservation payload passed by the scanner.", ex);
            return null;
        }
    }

    

    /** Fills the booking card and prefills the delivered energy field. */
    private void bindReservation() {
        // Every value below was produced by the Web API; nothing is derived on the phone.
        textReservationNo.setText(reservation.getReservationNo());
        bindStatusChip(reservation.getStatus());

        textProsumerName.setText(reservation.getProsumerName());
        textProsumerNic.setText(reservation.getProsumerNic());

        textStationName.setText(reservation.getStationName());
        textStationCode.setText(reservation.getStationCode());
        textSlotCode.setText(reservation.getSlotCode());
        textDate.setText(DateUtils.formatDate(reservation.getReservationStartUtc()));
        textTime.setText(reservation.getTimeRange());
        textType.setText(reservation.getSlotTypeLabel());
        textEnergy.setText(formatKwh(reservation.getEnergyKwh()));

        // The requested figure is the usual answer, so it is offered as the starting value
        // and the operator only edits it when the transfer fell short or ran over.
        inputDelivered.setText(toEditableNumber(reservation.getEnergyKwh()));
    }

    /** sets the pill's text and colour to reflect the status. */
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

    /** Formats a kWh figure for display. */
    private String formatKwh(double value) {
        // One decimal place matches the wording used on the booking cards.
        return String.format(Locale.getDefault(), "%.1f kWh", value);
    }

    /** Formats a number for the delivered energy input box. */
    private String toEditableNumber(double value) {
        // Locale.US is used DELIBERATELY here rather than the device locale: this text is
        // read straight back by Double.parseDouble, which only understands a full stop as
        // the decimal separator, so a locale that writes "18,0" would break the prefill.
        return String.format(Locale.US, "%.1f", value);
    }

    // =====================================================================
    // Finalising the transfer
    // =====================================================================

    /** Wires the finalise button. */
    private void setupActions() {
        // The listener is attached once; the in flight guard lives inside the handler.
        btnFinalise.setOnClickListener(new View.OnClickListener() {

            /** Validates the form and sends the completion to the API. */
            @Override
            public void onClick(View view) {
                // A second tap while a call is running is simply ignored.
                if (!finalising) {
                    submitCompletion();
                }
            }
        });
    }

    /**
     * Validates the delivered figure and calls POST reservations/complete.
     *
     * The only local check is that the number can be read and is not negative,
     * which saves a pointless round trip on a mistyped value. Everything that
     * matters - that the token is still live, that the booking is Approved, that
     * this operator may close it - is decided by the FAT service layer.
     */
    private void submitCompletion() {
        // The error from a previous attempt is cleared before the field is read again.
        tilDelivered.setError(null);

        String typed = inputDelivered.getText() == null
                ? "" : inputDelivered.getText().toString().trim();

        if (typed.isEmpty()) {
            tilDelivered.setError("Enter the energy delivered.");
            return;
        }

        double delivered;
        try {
            delivered = Double.parseDouble(typed);
        } catch (NumberFormatException ex) {
            // A stray character rather than a genuine failure, so no logging is needed.
            tilDelivered.setError("Enter a number, for example 17.4");
            return;
        }

        if (delivered < 0) {
            tilDelivered.setError("The energy delivered cannot be negative.");
            return;
        }

        String notes = inputNotes.getText() == null ? "" : inputNotes.getText().toString().trim();

        JSONObject body = buildCompletionBody(delivered, notes);
        if (body == null) {
            // Building a three field object cannot realistically fail, but the screen survives it.
            toastLong(getString(R.string.error_generic));
            return;
        }

        sendCompletion(body);
    }

    /** Builds the {qrToken, deliveredKwh, notes} body the complete route expects. */
    private JSONObject buildCompletionBody(double delivered, String notes) {
        // The token is sent back exactly as it was scanned or typed; the server matches on it.
        try {
            JSONObject body = new JSONObject();
            body.put("qrToken", qrToken);
            body.put("deliveredKwh", delivered);
            body.put("notes", notes);
            return body;
        } catch (JSONException ex) {
            Log.e(TAG, "Could not build the completion body.", ex);
            return null;
        }
    }

    /**
     * Posts the completion and reacts to the server's answer.
     *
     * ON SUCCESS THE QR CODE IS SPENT. The Web API clears the reservation's
     * qrToken as part of completing it, which is why this app keeps no record of
     * which codes it has already used: scanning the same printed code again is
     * refused by the server as QR_INVALID, so single use is guaranteed centrally
     * rather than by every client remembering to enforce it.
     */
    private void sendCompletion(JSONObject body) {
        // The button is disabled for the duration so the job cannot be closed twice.
        setBusy(true);

        api.post(Constants.EP_COMPLETE, body, token(), new ApiClient.Callback() {

            /** Receives the server's answer on the main thread. */
            @Override
            public void onResult(ApiResult result) {
                // ApiClient posts callbacks to the main looper, so views are touched directly.
                setBusy(false);

                if (!result.isSuccess()) {
                    // A refusal keeps the operator on this screen so the figure can be corrected.
                    showApiError(result);
                    return;
                }
                openSummary(result);
            }
        });
    }

    /** Shows or hides the in flight indicators. */
    private void setBusy(boolean busy) {
        // Kept in one place so both exits from the call leave the same state behind.
        finalising = busy;
        progressVerify.setVisibility(busy ? View.VISIBLE : View.GONE);
        btnFinalise.setEnabled(!busy);
    }

    /** Opens the shared summary page with the completed booking. */
    private void openSummary(ApiResult result) {
        // The message shown is the WEB API's own wording, and the reservation carried across
        // is the updated record the server returned, so the summary repeats server truth only.
        JSONObject completed = result.getDataObject();

        Intent intent = new Intent(this, BookingSummaryActivity.class);
        intent.putExtra(Constants.EXTRA_SUMMARY_SUCCESS, true);
        intent.putExtra(Constants.EXTRA_SUMMARY_TITLE, getString(R.string.summary_transfer_completed));
        intent.putExtra(Constants.EXTRA_SUMMARY_MESSAGE, result.getMessage());
        if (completed != null) {
            intent.putExtra(Constants.EXTRA_RESERVATION_JSON, completed.toString());
        }
        startActivity(intent);

        // finish() means the back gesture never returns to a form for a job already closed.
        finish();
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
