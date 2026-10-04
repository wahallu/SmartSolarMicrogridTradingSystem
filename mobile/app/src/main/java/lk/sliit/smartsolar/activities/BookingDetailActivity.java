/* =============================================================================
 * File        : BookingDetailActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Activities / Booking Detail
 * Description : Shows one booking and the actions still available for it.
 * Author      : IT22228062 - Akash
 * Created     : 2026-09-16
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.content.DialogInterface;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.text.TextUtils;
import android.util.Base64;
import android.util.Log;
import android.view.View;
import android.view.ViewGroup;
import android.widget.EditText;
import android.widget.ImageView;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.annotation.Nullable;
import androidx.appcompat.app.AlertDialog;
import androidx.core.content.ContextCompat;
import androidx.core.widget.NestedScrollView;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.card.MaterialCardView;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.Locale;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.api.ApiClient;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.models.ReservationModel;
import lk.sliit.smartsolar.util.Constants;
import lk.sliit.smartsolar.util.DateUtils;

/** Full detail view of a single reservation, with the actions the server allows. */
public class BookingDetailActivity extends BaseActivity {

    private static final String TAG = "BookingDetail";

    /** Reason recorded against a cancellation the account holder made on the phone. */
    private static final String CANCEL_REASON = "Cancelled by the account holder from the mobile app.";

    // Screen states.
    private NestedScrollView scrollDetail;
    private ProgressBar progressDetail;
    private View stateError;
    private TextView textError;
    private MaterialButton btnRetry;

    // Header.
    private TextView textReservationNo;
    private TextView textStatus;
    private TextView textHours;

    // Booking fields.
    private TextView textStationName;
    private TextView textStationCode;
    private TextView textSlotCode;
    private TextView textDate;
    private TextView textTime;
    private TextView textType;
    private TextView textEnergy;
    private View rowDelivered;
    private TextView textDelivered;

    // QR card.
    private MaterialCardView cardQr;
    private ProgressBar progressQr;
    private ImageView imageQr;
    private TextView textQrHint;
    private TextView textQrError;

    // Actions.
    private View panelNotice;
    private MaterialButton btnEdit;
    private MaterialButton btnCancel;
    private View rowStaff;
    private MaterialButton btnApprove;
    private MaterialButton btnReject;

    // Audit trail.
    private MaterialCardView cardAudit;
    private TextView textApproved;
    private TextView textCompleted;
    private TextView textNotes;
    private TextView textCancelReason;
    private TextView textRejectedReason;

    /** Identifier of the reservation this screen is showing. */
    private String reservationId;

    /** The reservation as the API last returned it. */
    private ReservationModel reservation;

    /**
     * The untouched JSON of that reservation. It is kept so the edit screen can be
     * handed the exact server payload, and so the audit fields the model does not
     * expose (approvedAt, completedAt) can still be read.
     */
    private JSONObject reservationJson;

    /** Builds the screen and reads the reservation identifier out of the intent. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // super.onCreate must run first because it creates api, session and localStore.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_booking_detail);

        setupToolbar(R.id.toolbar_booking_detail, getString(R.string.booking_detail_title), true);

        bindViews();
        setupActions();

        reservationId = getIntent().getStringExtra(Constants.EXTRA_RESERVATION_ID);
        if (TextUtils.isEmpty(reservationId)) {
            // Without an identifier there is nothing to show, so the screen closes cleanly.
            toast(getString(R.string.error_generic));
            finish();
            return;
        }

        showOnly(progressDetail, scrollDetail, stateError);
    }

    /**
     * Reloads the booking every time the screen comes forward. Returning from the
     * edit screen, or from an approval, must show the server's latest verdicts
     * rather than the copy this screen loaded earlier.
     */
    @Override
    protected void onResume() {
        // Also covers the first appearance, so onCreate does not load separately.
        super.onResume();
        if (!TextUtils.isEmpty(reservationId)) {
            loadReservation();
        }
    }

    /** Looks up every view this screen drives. */
    private void bindViews() {
        // findViewById throughout: view binding and data binding are not permitted here.
        scrollDetail = findViewById(R.id.scroll_detail);
        progressDetail = findViewById(R.id.progress_detail);
        stateError = findViewById(R.id.state_detail_error);
        textError = findViewById(R.id.text_detail_error);
        btnRetry = findViewById(R.id.btn_detail_retry);

        textReservationNo = findViewById(R.id.text_detail_reservation_no);
        textStatus = findViewById(R.id.text_detail_status);
        textHours = findViewById(R.id.text_detail_hours);

        textStationName = findViewById(R.id.text_detail_station_name);
        textStationCode = findViewById(R.id.text_detail_station_code);
        textSlotCode = findViewById(R.id.text_detail_slot_code);
        textDate = findViewById(R.id.text_detail_date);
        textTime = findViewById(R.id.text_detail_time);
        textType = findViewById(R.id.text_detail_type);
        textEnergy = findViewById(R.id.text_detail_energy);
        rowDelivered = findViewById(R.id.row_detail_delivered);
        textDelivered = findViewById(R.id.text_detail_delivered);

        cardQr = findViewById(R.id.card_detail_qr);
        progressQr = findViewById(R.id.progress_detail_qr);
        imageQr = findViewById(R.id.image_detail_qr);
        textQrHint = findViewById(R.id.text_detail_qr_hint);
        textQrError = findViewById(R.id.text_detail_qr_error);

        panelNotice = findViewById(R.id.panel_detail_notice);
        btnEdit = findViewById(R.id.btn_detail_edit);
        btnCancel = findViewById(R.id.btn_detail_cancel);
        rowStaff = findViewById(R.id.row_detail_staff);
        btnApprove = findViewById(R.id.btn_detail_approve);
        btnReject = findViewById(R.id.btn_detail_reject);

        cardAudit = findViewById(R.id.card_detail_audit);
        textApproved = findViewById(R.id.text_detail_approved);
        textCompleted = findViewById(R.id.text_detail_completed);
        textNotes = findViewById(R.id.text_detail_notes);
        textCancelReason = findViewById(R.id.text_detail_cancel_reason);
        textRejectedReason = findViewById(R.id.text_detail_rejected_reason);
    }

    /** Wires the retry button and the four action buttons. */
    private void setupActions() {
        // The listeners are attached once; whether a button is usable is decided later
        // in applyServerVerdicts() from the flags the API sent.
        btnRetry.setOnClickListener(new View.OnClickListener() {

            /** Requests the reservation again after a failed load. */
            @Override
            public void onClick(View view) {
                // Same call path as onResume, so there is only one loading routine.
                loadReservation();
            }
        });

        btnEdit.setOnClickListener(new View.OnClickListener() {

            /** Opens the edit screen for this booking. */
            @Override
            public void onClick(View view) {
                // Guarded again on the server verdict in case the flag changed since binding.
                openEditScreen();
            }
        });

        btnCancel.setOnClickListener(new View.OnClickListener() {

            /** Asks the user to confirm, then sends the cancellation to the API. */
            @Override
            public void onClick(View view) {
                // The 12 hour rule is not tested here - the API accepts or refuses the call.
                confirmCancel();
            }
        });

        btnApprove.setOnClickListener(new View.OnClickListener() {

            /** Approves a pending booking on behalf of the signed in grid operator. */
            @Override
            public void onClick(View view) {
                // The API issues the QR token as part of approving, so the screen just reloads.
                confirmApprove();
            }
        });

        btnReject.setOnClickListener(new View.OnClickListener() {

            /** Collects a reason and rejects a pending booking. */
            @Override
            public void onClick(View view) {
                // PATCH /reservations/{id}/reject requires a reason, so one is asked for.
                promptRejectReason();
            }
        });
    }

    // =====================================================================
    // Loading
    // =====================================================================

    /** Calls GET /reservations/{id} and paints the result. */
    private void loadReservation() {
        // The loading state is shown first so a stale record is never left on screen.
        showOnly(progressDetail, scrollDetail, stateError);

        api.get(Constants.EP_RESERVATIONS + "/" + reservationId, token(), new ApiClient.Callback() {

            /** Receives the reservation on the main thread. */
            @Override
            public void onResult(ApiResult result) {
                // ApiClient posts callbacks to the main looper, so views are touched directly.
                if (!result.isSuccess()) {
                    showLoadFailure(result);
                    return;
                }

                reservationJson = result.getDataObject();
                reservation = ReservationModel.fromJson(reservationJson);

                bindReservation();
                showOnly(scrollDetail, progressDetail, stateError);
            }
        });
    }

    /** Shows the error state carrying the server's own wording. */
    private void showLoadFailure(ApiResult result) {
        // An expired session is handled centrally by BaseActivity, so nothing else is shown.
        textError.setText(result.getMessage().isEmpty()
                ? getString(R.string.error_generic)
                : result.getMessage());
        showOnly(stateError, progressDetail, scrollDetail);
        showApiError(result);
    }

    // =====================================================================
    // Rendering
    // =====================================================================

    /** Fills every field of the screen from the loaded reservation. */
    private void bindReservation() {
        // Every value below was produced by the Web API; nothing is derived on the phone.
        textReservationNo.setText(reservation.getReservationNo());
        bindStatusChip(reservation.getStatus());
        textHours.setText(DateUtils.describeHoursUntil(reservation.getHoursUntilStart()));

        textStationName.setText(reservation.getStationName());
        textStationCode.setText(reservation.getStationCode());
        textSlotCode.setText(reservation.getSlotCode());
        textDate.setText(DateUtils.formatDate(reservation.getReservationStartUtc()));
        textTime.setText(reservation.getTimeRange());
        textType.setText(reservation.getSlotTypeLabel());
        textEnergy.setText(formatKwh(reservation.getEnergyKwh()));

        // Delivered energy exists only after an operator finalised the transfer.
        Double delivered = reservation.getDeliveredKwh();
        if (delivered == null) {
            rowDelivered.setVisibility(View.GONE);
        } else {
            textDelivered.setText(formatKwh(delivered));
            rowDelivered.setVisibility(View.VISIBLE);
        }

        bindQrCard();
        applyServerVerdicts();
        bindAuditTrail();
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

    /** Formats a kWh figure for display. */
    private String formatKwh(double value) {
        // One decimal place matches the wording used on the booking cards.
        return String.format(Locale.getDefault(), "%.1f kWh", value);
    }

    // =====================================================================
    // QR code - rendered by the server
    // =====================================================================

    /** Shows or hides the QR card and, when it is shown, fetches the image. */
    private void bindQrCard() {
        // hasQrCode() is true only for an approved booking that still holds the token the
        // server issued at approval; the server clears that token on cancel and completion.
        if (!reservation.hasQrCode()) {
            cardQr.setVisibility(View.GONE);
            return;
        }

        cardQr.setVisibility(View.VISIBLE);
        progressQr.setVisibility(View.VISIBLE);
        imageQr.setVisibility(View.GONE);
        textQrError.setVisibility(View.GONE);
        textQrHint.setVisibility(View.VISIBLE);
        loadQrImage();
    }

    /**
     * Calls GET /reservations/{id}/qr and displays the PNG it returns.
     *
     * IMPORTANT: the QR image is drawn by the WEB API and arrives base64 encoded in
     * qrImageBase64. The phone therefore ships no QR generation library at all - it
     * only decodes bytes the server produced, which keeps the token format and the
     * payload contents entirely a server concern.
     */
    private void loadQrImage() {
        // The scanner side is the mirror of this: it sends the token back for verification.
        api.get(Constants.EP_RESERVATIONS + "/" + reservationId + "/qr", token(),
                new ApiClient.Callback() {

                    /** Receives the encoded PNG on the main thread. */
                    @Override
                    public void onResult(ApiResult result) {
                        // The card is still on screen because the reservation itself loaded fine.
                        progressQr.setVisibility(View.GONE);

                        if (!result.isSuccess()) {
                            showQrError(result.getMessage().isEmpty()
                                    ? getString(R.string.error_generic)
                                    : result.getMessage());
                            return;
                        }

                        JSONObject data = result.getDataObject();
                        String base64 = data == null ? "" : data.optString("qrImageBase64", "");
                        Bitmap bitmap = decodeQrBitmap(base64);

                        if (bitmap == null) {
                            showQrError(getString(R.string.error_generic));
                            return;
                        }

                        imageQr.setImageBitmap(bitmap);
                        imageQr.setVisibility(View.VISIBLE);
                        textQrHint.setVisibility(View.VISIBLE);
                        textQrError.setVisibility(View.GONE);
                    }
                });
    }

    /** Turns the server's base64 PNG into a bitmap, or null when it cannot be read. */
    private Bitmap decodeQrBitmap(String base64) {
        // The API sends raw base64 with no data URI prefix, so it is decoded as-is.
        if (TextUtils.isEmpty(base64)) {
            return null;
        }
        try {
            byte[] bytes = Base64.decode(base64, Base64.DEFAULT);
            return BitmapFactory.decodeByteArray(bytes, 0, bytes.length);
        } catch (IllegalArgumentException ex) {
            // A malformed payload is reported to the user rather than crashing the screen.
            Log.e(TAG, "Could not decode the QR image sent by the API.", ex);
            return null;
        }
    }

    /** Replaces the QR image with an error message. */
    private void showQrError(String message) {
        // The booking itself is still readable, so only the QR area reports the problem.
        imageQr.setVisibility(View.GONE);
        textQrHint.setVisibility(View.GONE);
        textQrError.setText(message);
        textQrError.setVisibility(View.VISIBLE);
    }

    // =====================================================================
    // Action gating - decided entirely by the server
    // =====================================================================

    /**
     * Enables, disables and hides the action buttons.
     *
     * THE ONLY INPUTS ARE SERVER FIELDS. reservation.canModify() and
     * reservation.canCancel() are the API's verdicts on rules R2 and R3 (the 12
     * hour notice period). This method never compares timestamps, never reads
     * hoursUntilStart for a decision and never re-implements the rule - it simply
     * mirrors the two booleans. When the booking is still open but the server said
     * "no", the warning panel explains why instead of leaving a dead button.
     */
    private void applyServerVerdicts() {
        // A booking is "open" while the server still reports it as Pending or Approved.
        boolean open = reservation.isPending() || reservation.isApproved();

        btnEdit.setVisibility(open ? View.VISIBLE : View.GONE);
        btnCancel.setVisibility(open ? View.VISIBLE : View.GONE);
        btnEdit.setEnabled(reservation.canModify());
        btnCancel.setEnabled(reservation.canCancel());

        boolean blockedByServer = open && (!reservation.canModify() || !reservation.canCancel());
        panelNotice.setVisibility(blockedByServer ? View.VISIBLE : View.GONE);

        // Staff extras. Hiding them for a prosumer is presentation only: the API applies
        // rule R10 and refuses PATCH approve / reject for any caller who is not staff.
        boolean staffCanAct = session.isGridOperator() && reservation.isPending();
        rowStaff.setVisibility(staffCanAct ? View.VISIBLE : View.GONE);
    }

    // =====================================================================
    // Actions
    // =====================================================================

    /** Starts the edit screen, carrying the untouched server payload. */
    private void openEditScreen() {
        // The raw JSON travels so the edit screen starts from exactly what the API returned.
        if (reservation == null || !reservation.canModify()) {
            // Belt and braces: the button is already disabled when the server said no.
            info(getString(R.string.action_edit_booking), getString(R.string.notice_12_hour));
            return;
        }

        Intent intent = new Intent(this, EditBookingActivity.class);
        intent.putExtra(Constants.EXTRA_RESERVATION_ID, reservationId);
        intent.putExtra(Constants.EXTRA_RESERVATION_JSON,
                reservationJson == null ? "" : reservationJson.toString());
        startActivity(intent);
    }

    /** Asks the user to confirm the cancellation before any call is made. */
    private void confirmCancel() {
        // The wording of the confirmation lives in strings.xml so it can be reviewed once.
        confirm(getString(R.string.action_cancel_booking),
                getString(R.string.confirm_cancel_booking),
                getString(R.string.action_confirm),
                new Runnable() {

                    /** Sends the cancellation once the user has agreed. */
                    @Override
                    public void run() {
                        // The API applies rule R3 and answers with its own message either way.
                        sendCancel();
                    }
                });
    }

    /** Sends PATCH /reservations/{id}/cancel. */
    private void sendCancel() {
        // Whether the 12 hour notice period has passed is decided by the API, not here.
        JSONObject body = buildReasonBody(CANCEL_REASON);

        api.patch(Constants.EP_RESERVATIONS + "/" + reservationId + "/cancel", body, token(),
                new ApiClient.Callback() {

                    /** Handles the server's answer to the cancellation. */
                    @Override
                    public void onResult(ApiResult result) {
                        // A refusal (for example RES_CANCEL_12H) is shown with the API's own wording.
                        if (!result.isSuccess()) {
                            showApiError(result);
                            return;
                        }
                        openSummary(getString(R.string.summary_booking_cancelled), result.getMessage());
                    }
                });
    }

    /** Asks a grid operator to confirm before approving a pending booking. */
    private void confirmApprove() {
        // Approving issues the QR token, so it is treated as a deliberate action.
        confirm("Approve booking",
                "Approve this booking and issue its QR code?",
                getString(R.string.action_confirm),
                new Runnable() {

                    /** Sends the approval once the operator has agreed. */
                    @Override
                    public void run() {
                        // No body is required; the API decides everything from the route and the JWT.
                        sendStaffDecision("/approve", null);
                    }
                });
    }

    /** Collects the mandatory reason and rejects the booking. */
    private void promptRejectReason() {
        // PATCH /reservations/{id}/reject requires a reason, so an empty one is refused locally
        // purely to save a pointless round trip - the API validates it as well.
        promptForReason("Reject booking", new ReasonListener() {

            /** Sends the rejection with the reason the operator typed. */
            @Override
            public void onReason(String reason) {
                // The reason is stored by the server and shown afterwards in the audit trail.
                sendStaffDecision("/reject", buildReasonBody(reason));
            }
        });
    }

    /**
     * Sends one of the staff decisions and reloads the booking.
     *
     * Both routes are staff only. A prosumer never sees these buttons, and even if
     * the call were made the API would refuse it under rule R10, so no role check
     * is duplicated on the phone beyond hiding the controls.
     */
    private void sendStaffDecision(String suffix, JSONObject body) {
        // The reload afterwards is what brings back the QR code an approval just issued.
        api.patch(Constants.EP_RESERVATIONS + "/" + reservationId + suffix, body, token(),
                new ApiClient.Callback() {

                    /** Handles the server's answer to the staff decision. */
                    @Override
                    public void onResult(ApiResult result) {
                        // The API's message already reads well to an end user.
                        if (!result.isSuccess()) {
                            showApiError(result);
                            return;
                        }
                        toast(result.getMessage());
                        loadReservation();
                    }
                });
    }

    /** Opens the shared summary screen after a successful cancellation. */
    private void openSummary(String title, String message) {
        // finish() is called so the back stack never returns to a booking that no longer exists.
        Intent intent = new Intent(this, BookingSummaryActivity.class);
        intent.putExtra(Constants.EXTRA_SUMMARY_SUCCESS, true);
        intent.putExtra(Constants.EXTRA_SUMMARY_TITLE, title);
        intent.putExtra(Constants.EXTRA_SUMMARY_MESSAGE, message);
        startActivity(intent);
        finish();
    }

    // =====================================================================
    // Audit trail
    // =====================================================================

    /** Fills the audit card with whatever staff activity the server recorded. */
    private void bindAuditTrail() {
        // approvedAt and completedAt are read from the raw payload because they are
        // timestamps the model does not expose as typed fields.
        boolean any = false;

        any |= bindAuditRow(textApproved, "Approved by",
                reservation.getApprovedBy(), optText("approvedAt"));
        any |= bindAuditRow(textCompleted, "Completed by",
                reservation.getCompletedBy(), optText("completedAt"));
        any |= bindAuditNote(textNotes, "Operator notes", reservation.getOperatorNotes());
        any |= bindAuditNote(textCancelReason, "Cancellation reason", reservation.getCancelReason());
        any |= bindAuditNote(textRejectedReason, "Rejection reason", reservation.getRejectedReason());

        cardAudit.setVisibility(any ? View.VISIBLE : View.GONE);
    }

    /** Shows one "<label> <who> on <when>" row, and reports whether it was shown. */
    private boolean bindAuditRow(TextView view, String label, String who, String whenIso) {
        // A row with no actor is simply hidden; the timestamp alone would say nothing useful.
        if (TextUtils.isEmpty(who)) {
            view.setVisibility(View.GONE);
            return false;
        }

        String when = DateUtils.formatDateTime(whenIso);
        String line = when.isEmpty() ? label + " " + who : label + " " + who + " on " + when;
        view.setText(line);
        view.setVisibility(View.VISIBLE);
        return true;
    }

    /** Shows one "<label>: <text>" row, and reports whether it was shown. */
    private boolean bindAuditNote(TextView view, String label, String value) {
        // Used for the free text fields: operator notes and the two refusal reasons.
        if (TextUtils.isEmpty(value)) {
            view.setVisibility(View.GONE);
            return false;
        }
        view.setText(label + ": " + value);
        view.setVisibility(View.VISIBLE);
        return true;
    }

    /** Reads a string straight out of the raw reservation payload. */
    private String optText(String field) {
        // Returns an empty string for an absent or null field so callers need no null check.
        if (reservationJson == null || reservationJson.isNull(field)) {
            return "";
        }
        return reservationJson.optString(field, "");
    }

    // =====================================================================
    // Small helpers
    // =====================================================================

    /** Receives the reason a staff member typed into the reason dialog. */
    private interface ReasonListener {

        /** Delivers the trimmed, non empty reason. */
        void onReason(String reason);
    }

    /**
     * Shows the shared reason dialog and hands the typed text back.
     *
     * The dialog layout is shared with the other staff screens, so its input field
     * is located by type rather than by a hard coded identifier. That keeps this
     * screen working if the shared layout gains other widgets later.
     */
    private void promptForReason(String title, final ReasonListener listener) {
        // Inflating with a null parent is correct for a dialog: AlertDialog performs the attach.
        final View dialogView = getLayoutInflater().inflate(R.layout.dialog_reason_input, null);
        final EditText input = findFirstEditText(dialogView);

        new AlertDialog.Builder(this)
                .setTitle(title)
                .setView(dialogView)
                .setPositiveButton(R.string.action_confirm,
                        new DialogInterface.OnClickListener() {

                            /** Validates the reason and passes it on. */
                            @Override
                            public void onClick(DialogInterface dialog, int which) {
                                // A blank reason is refused locally only to avoid a wasted call.
                                String reason = input == null || input.getText() == null
                                        ? "" : input.getText().toString().trim();
                                if (reason.isEmpty()) {
                                    toast("A reason is required.");
                                    return;
                                }
                                listener.onReason(reason);
                            }
                        })
                .setNegativeButton(R.string.action_cancel,
                        new DialogInterface.OnClickListener() {

                            /** Closes the dialog without acting. */
                            @Override
                            public void onClick(DialogInterface dialog, int which) {
                                // Nothing is sent when the staff member changes their mind.
                                dialog.dismiss();
                            }
                        })
                .show();
    }

    /** Finds the first EditText anywhere inside an inflated view, or null. */
    private EditText findFirstEditText(View root) {
        // A short depth first walk; the reason dialog holds only a handful of views.
        if (root instanceof EditText) {
            return (EditText) root;
        }
        if (root instanceof ViewGroup) {
            ViewGroup group = (ViewGroup) root;
            for (int i = 0; i < group.getChildCount(); i++) {
                EditText found = findFirstEditText(group.getChildAt(i));
                if (found != null) {
                    return found;
                }
            }
        }
        return null;
    }

    /** Builds the {"reason": "..."} body the cancel and reject routes accept. */
    private JSONObject buildReasonBody(String reason) {
        // JSONException cannot occur for a plain string value, but it is handled anyway.
        try {
            JSONObject body = new JSONObject();
            body.put("reason", reason);
            return body;
        } catch (JSONException ex) {
            Log.e(TAG, "Could not build the reason body.", ex);
            return null;
        }
    }

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
