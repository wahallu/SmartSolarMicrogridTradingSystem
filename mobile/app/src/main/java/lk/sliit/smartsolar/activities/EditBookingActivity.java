/* =============================================================================
 * File        : EditBookingActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Activities / Edit Booking
 * Description : Lets a prosumer change the slot or energy amount of a booking.
 * Author      : IT22228062 - Akash
 * Created     : 2026-09-16
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.app.DatePickerDialog;
import android.content.Intent;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.view.View;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.annotation.Nullable;
import androidx.core.content.ContextCompat;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.textfield.TextInputEditText;
import com.google.android.material.textfield.TextInputLayout;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.Calendar;
import java.util.Date;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.TimeZone;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.adapters.SlotAdapter;
import lk.sliit.smartsolar.api.ApiClient;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.models.ReservationModel;
import lk.sliit.smartsolar.models.SlotModel;
import lk.sliit.smartsolar.util.Constants;
import lk.sliit.smartsolar.util.DateUtils;
import lk.sliit.smartsolar.util.ValidationUtils;

/** Screen that changes the slot and the energy amount of an existing booking. */
public class EditBookingActivity extends BaseActivity {

    /** Tolerance used when deciding whether the typed kWh figure really differs. */
    private static final double ENERGY_EPSILON = 0.0001;

    // Read only summary of the booking as it stands today.
    private TextView currentReservationNo;
    private TextView currentStatus;
    private TextView currentStation;
    private TextView currentDate;
    private TextView currentTime;
    private TextView currentEnergy;

    // The editable section and the panel that replaces it when the API says no.
    private View lockedPanel;
    private TextView lockedText;
    private View editForm;

    // Date step.
    private TextInputLayout dateLayout;
    private TextInputEditText dateInput;

    // Slot step, with its loading / content / empty states.
    private ProgressBar slotProgress;
    private RecyclerView slotList;
    private View slotEmptyPanel;
    private TextView slotEmptyText;

    // Energy step and submission.
    private TextInputLayout energyLayout;
    private TextInputEditText energyInput;
    private MaterialButton saveButton;
    private ProgressBar submitProgress;

    /** The booking being edited, rebuilt from the JSON the previous screen passed. */
    private ReservationModel reservation;

    /** Adapter driving the alternative slot picker; holds the single selection. */
    private SlotAdapter slotAdapter;

    /** The slot the user picked, or the booking's own slot when it is still offered. */
    private SlotModel selectedSlot;

    /** Calendar day currently being looked at, in the "yyyy-MM-dd" form the API expects. */
    private String selectedDate;

    /**
     * Capacity of the slot the booking already holds, learned from the slot list
     * when that slot appears in it. Stays 0 when the API no longer offers the
     * slot, and validateEnergy treats 0 as "no local ceiling" so the server
     * remains the only authority on the limit.
     */
    private double currentSlotCapacity;

    /** Default wording of the empty panel, kept so an error message can be undone. */
    private String defaultEmptyText;

    /** True while the PUT is in flight, so it cannot be sent twice. */
    private boolean submitting;

    /** Builds the screen from the reservation handed over in the intent. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // BaseActivity creates api, session and localStore before anything else runs.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_edit_booking);
        setupToolbar(R.id.toolbar_edit_booking, getString(R.string.action_edit_booking), true);

        bindViews();

        reservation = readReservationFromIntent();
        if (reservation == null) {
            // Without a booking there is nothing to edit, so the screen closes at once.
            toastLong(getString(R.string.error_generic));
            finish();
            return;
        }

        bindCurrentBooking();
        wireListeners();
        prepareEditableState();
    }

    /** Looks up every view this screen drives. */
    private void bindViews() {
        // findViewById only - view binding and data binding are not permitted on this project.
        currentReservationNo = findViewById(R.id.text_current_reservation_no);
        currentStatus = findViewById(R.id.text_current_status);
        currentStation = findViewById(R.id.text_current_station);
        currentDate = findViewById(R.id.text_current_date);
        currentTime = findViewById(R.id.text_current_time);
        currentEnergy = findViewById(R.id.text_current_energy);

        lockedPanel = findViewById(R.id.panel_locked);
        lockedText = findViewById(R.id.text_locked);
        editForm = findViewById(R.id.layout_edit_form);

        dateLayout = findViewById(R.id.layout_edit_date);
        dateInput = findViewById(R.id.input_edit_date);

        slotProgress = findViewById(R.id.progress_edit_slots);
        slotList = findViewById(R.id.recycler_edit_slots);
        slotEmptyPanel = findViewById(R.id.panel_edit_slots_empty);
        slotEmptyText = findViewById(R.id.text_edit_slots_empty);

        energyLayout = findViewById(R.id.layout_edit_energy);
        energyInput = findViewById(R.id.input_edit_energy);
        saveButton = findViewById(R.id.btn_save_changes);
        submitProgress = findViewById(R.id.progress_edit_submit);

        // A vertical list of selectable slot cards, nested inside the scrolling body.
        slotList.setLayoutManager(new LinearLayoutManager(this));
        slotList.setNestedScrollingEnabled(false);
        slotList.setHasFixedSize(false);

        defaultEmptyText = slotEmptyText.getText().toString();
    }

    /** Rebuilds the reservation from Constants.EXTRA_RESERVATION_JSON. */
    private ReservationModel readReservationFromIntent() {
        // The whole record travels as JSON so the read only card can be drawn immediately,
        // including the server's canModify verdict that gates this screen.
        String json = getIntent().getStringExtra(Constants.EXTRA_RESERVATION_JSON);
        if (json == null || json.trim().isEmpty()) {
            return null;
        }
        try {
            return ReservationModel.fromJson(new JSONObject(json));
        } catch (JSONException ex) {
            // A malformed payload is treated exactly like a missing one.
            return null;
        }
    }

    // =====================================================================
    // The booking as it stands today
    // =====================================================================

    /** Fills the read only card with the booking's current values. */
    private void bindCurrentBooking() {
        // Every value below arrived from the Web API; nothing is derived on the phone.
        currentReservationNo.setText(reservation.getReservationNo());
        currentStatus.setText(reservation.getStatus());
        paintStatusChip(currentStatus, reservation.getStatus());

        String code = reservation.getStationCode() == null ? "" : reservation.getStationCode();
        String name = reservation.getStationName() == null ? "" : reservation.getStationName();
        currentStation.setText(code.isEmpty() ? name : code + " - " + name);

        currentDate.setText(DateUtils.formatDayDate(reservation.getReservationStartUtc()));
        currentTime.setText(reservation.getSlotCode() + "   " + reservation.getTimeRange());
        currentEnergy.setText(reservation.getEnergyLabel());
    }

    /** Recolours a status pill to match the reservation status. */
    private void paintStatusChip(TextView chip, String status) {
        // The single bg_status_chip drawable is retinted at runtime, exactly as the
        // list adapters do, so one drawable serves all five statuses.
        chip.setTextColor(ContextCompat.getColor(this, statusTextColour(status)));
        Drawable pill = chip.getBackground();
        if (pill instanceof GradientDrawable) {
            // mutate() keeps this chip's fill from leaking into other views.
            GradientDrawable shape = (GradientDrawable) pill.mutate();
            shape.setColor(ContextCompat.getColor(this, statusFillColour(status)));
        }
    }

    /** Maps a reservation status onto its pill fill colour. */
    private int statusFillColour(String status) {
        // Mirrors the palette used by ReservationAdapter so the two look identical.
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
    private int statusTextColour(String status) {
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

    // =====================================================================
    // Wiring and initial state
    // =====================================================================

    /** Attaches the date picker and the save button. */
    private void wireListeners() {
        // The date field is read only, so both it and its wrapper open the picker.
        dateInput.setOnClickListener(new View.OnClickListener() {

            /** Opens the date picker when the field is tapped. */
            @Override
            public void onClick(View view) {
                // The picker itself decides nothing; it only narrows what is offered.
                openDatePicker();
            }
        });

        dateLayout.setOnClickListener(new View.OnClickListener() {

            /** Opens the date picker when the surrounding box is tapped. */
            @Override
            public void onClick(View view) {
                // Same handler so the whole field feels tappable.
                openDatePicker();
            }
        });

        saveButton.setOnClickListener(new View.OnClickListener() {

            /** Validates the form and sends the change to the API. */
            @Override
            public void onClick(View view) {
                // All the real checking happens on the server; this only shapes the request.
                submitChanges();
            }
        });
    }

    /**
     * Decides whether the form may be used at all, then seeds the date and the
     * energy field from the booking and loads the alternative slots.
     */
    private void prepareEditableState() {
        // canModify() is the SERVER's verdict on the 12 hour notice rule (R2). The app never
        // recomputes it from hoursUntilStart - it simply shows or hides the form accordingly.
        if (!reservation.canModify()) {
            showLocked();
            return;
        }

        lockedPanel.setVisibility(View.GONE);
        editForm.setVisibility(View.VISIBLE);

        // Open on the day the booking is currently scheduled for.
        Date start = DateUtils.parseIso(reservation.getReservationStartUtc());
        selectedDate = start == null ? DateUtils.today() : DateUtils.toApiDate(start);
        dateInput.setText(DateUtils.formatDate(selectedDate));

        // Pre-fill the energy field with the amount already booked.
        energyInput.setText(String.format(Locale.getDefault(), "%.1f", reservation.getEnergyKwh()));

        loadSlots();
    }

    /** Hides the form and explains that the API refused any further change. */
    private void showLocked() {
        // The countdown in this sentence is cosmetic. The DECISION is the server's canModify
        // flag - the 12 hour notice rule is evaluated by the API and never on the phone.
        lockedPanel.setVisibility(View.VISIBLE);
        editForm.setVisibility(View.GONE);
        lockedText.setText("This booking can no longer be changed - its slot starts "
                + DateUtils.describeHoursUntil(reservation.getHoursUntilStart())
                + ". The 12 hour notice rule is enforced by the Smart Solar API, which sent this verdict.");
        saveButton.setEnabled(false);
    }

    // =====================================================================
    // The date
    // =====================================================================

    /** Opens a date picker limited to today plus the next seven days. */
    private void openDatePicker() {
        // The minDate / maxDate bounds MIRROR business rule R1 as a convenience only, so the
        // user is not offered a day that would obviously be refused. They are NOT the rule:
        // the Web API owns the 7 day window, and it also owns the 12 hour notice rule that
        // governs this edit. Whatever this picker allows, the server has the final say.
        Calendar initial = Calendar.getInstance(TimeZone.getTimeZone("UTC"));
        Date parsed = DateUtils.parseIso(selectedDate);
        if (parsed != null) {
            initial.setTime(parsed);
        }

        DatePickerDialog dialog = new DatePickerDialog(
                this,
                new DatePickerDialog.OnDateSetListener() {

                    /** Records the chosen day and reloads the slot list. */
                    @Override
                    public void onDateSet(android.widget.DatePicker view, int year, int month, int day) {
                        // Changing the day always invalidates the slot choice made before it.
                        onDateChosen(year, month, day);
                    }
                },
                initial.get(Calendar.YEAR),
                initial.get(Calendar.MONTH),
                initial.get(Calendar.DAY_OF_MONTH));

        long nowMillis = System.currentTimeMillis();
        long windowMillis = Constants.RESERVATION_WINDOW_DAYS * 24L * 60L * 60L * 1000L;
        dialog.getDatePicker().setMinDate(nowMillis - 1000L);
        dialog.getDatePicker().setMaxDate(nowMillis + windowMillis);
        dialog.show();
    }

    /** Stores the chosen calendar day and reloads the slot list. */
    private void onDateChosen(int year, int month, int dayOfMonth) {
        // The API's date filter is a UTC calendar day and DateUtils formats in UTC. Midday is
        // used as the instant so the conversion never rolls onto the neighbouring day.
        Calendar chosen = Calendar.getInstance(TimeZone.getDefault());
        chosen.set(year, month, dayOfMonth, 12, 0, 0);
        chosen.set(Calendar.MILLISECOND, 0);

        selectedDate = DateUtils.toApiDate(chosen.getTime());
        dateInput.setText(DateUtils.formatDate(selectedDate));
        loadSlots();
    }

    // =====================================================================
    // Alternative slots at the same node
    // =====================================================================

    /** Loads the slots the API considers bookable at this node on the chosen date. */
    private void loadSlots() {
        // GET /slots/available returns ONLY slots the server would accept, so the app never
        // reasons about capacity, blocking or start times itself (rule R8). The stationId is
        // the booking's own node: an edit may move the slot but never the microgrid node.
        if (selectedDate == null || reservation.getStationId() == null) {
            return;
        }

        selectedSlot = null;
        currentSlotCapacity = 0;
        energyLayout.setError(null);
        updateEnergyHelper();
        updateSaveState();

        showOnly(slotProgress, slotList, slotEmptyPanel);

        Map<String, String> query = new LinkedHashMap<>();
        query.put("stationId", reservation.getStationId());
        query.put("date", selectedDate);

        api.get(ApiClient.withQuery(Constants.EP_SLOTS_AVAILABLE, query), token(),
                new ApiClient.Callback() {

                    /** Receives the slot list on the main thread. */
                    @Override
                    public void onResult(ApiResult result) {
                        // ApiClient posts callbacks to the main looper, so views are touched directly.
                        if (!result.isSuccess()) {
                            showSlotMessage(result.getMessage());
                            showApiError(result);
                            return;
                        }
                        renderSlots(result);
                    }
                });
    }

    /** Binds a successful slot response to the picker. */
    private void renderSlots(ApiResult result) {
        // The endpoint answers with a bare array; the paged shape is accepted defensively.
        List<SlotModel> slots = result.getDataArray() != null
                ? SlotModel.fromJsonArray(result.getDataArray())
                : SlotModel.fromJsonArray(result.getPagedItems());

        if (slots.isEmpty()) {
            showSlotMessage(defaultEmptyText);
            return;
        }

        slotAdapter = new SlotAdapter(slots, new SlotAdapter.OnSlotClick() {

            /** Records the slot the user tapped. */
            @Override
            public void onClick(SlotModel slot) {
                // SlotAdapter only offers slots whose isBookable() flag the SERVER set,
                // so no availability decision is taken here.
                onSlotSelected(slot);
            }
        });
        slotList.setAdapter(slotAdapter);

        preselectCurrentSlot(slots);
        showOnly(slotList, slotProgress, slotEmptyPanel);
        updateEnergyHelper();
        updateSaveState();
    }

    /** Highlights the booking's existing slot when the API still offers it. */
    private void preselectCurrentSlot(List<SlotModel> slots) {
        // Starting on the current slot means "I only want to change the kWh" needs no taps,
        // and it also tells the app the capacity ceiling the server set for that window.
        String bookedSlotId = reservation.getSlotId();
        if (bookedSlotId == null || bookedSlotId.isEmpty()) {
            return;
        }

        for (int i = 0; i < slots.size(); i++) {
            SlotModel slot = slots.get(i);
            if (bookedSlotId.equals(slot.getId())) {
                currentSlotCapacity = slot.getEnergyCapacityKwh();
                selectedSlot = slot;
                slotAdapter.setSelectedPosition(i);
                return;
            }
        }
    }

    /** Records the slot the user tapped and refreshes the energy field. */
    private void onSlotSelected(SlotModel slot) {
        // Only the capacity hint and the button state depend on this; the request body is
        // assembled at submit time.
        selectedSlot = slot;
        energyLayout.setError(null);
        updateEnergyHelper();
        updateSaveState();
    }

    /** Shows the empty / error panel with the given message. */
    private void showSlotMessage(String message) {
        // Falls back to the layout's standard wording when the server sent nothing useful.
        slotEmptyText.setText(message == null || message.trim().isEmpty()
                ? defaultEmptyText
                : message);
        showOnly(slotEmptyPanel, slotProgress, slotList);
        updateSaveState();
    }

    // =====================================================================
    // The energy amount
    // =====================================================================

    /** Shows the effective capacity ceiling as helper text under the energy field. */
    private void updateEnergyHelper() {
        // The ceiling shown is the figure the API sent for the slot, never a local guess.
        double capacity = effectiveCapacity();
        if (capacity <= 0) {
            energyLayout.setHelperText(null);
            return;
        }
        energyLayout.setHelperText(String.format(Locale.getDefault(), "%s: %.1f kWh",
                getString(R.string.label_capacity), capacity));
    }

    /** Capacity of the chosen slot, or of the booking's own slot when nothing is chosen. */
    private double effectiveCapacity() {
        // Both figures came from the server. A zero here simply means the app has no ceiling
        // to check against, and the API then applies its own.
        if (selectedSlot != null) {
            return selectedSlot.getEnergyCapacityKwh();
        }
        return currentSlotCapacity;
    }

    /** Enables the save button only when the form is usable and no request is in flight. */
    private void updateSaveState() {
        // canModify() is the server's verdict; a booking it locked can never reach this state.
        saveButton.setEnabled(!submitting && reservation != null && reservation.canModify());
    }

    // =====================================================================
    // Submit
    // =====================================================================

    /** Validates the form and sends PUT /reservations/{id} with only the changed fields. */
    private void submitChanges() {
        // A second tap while the first request is running is ignored.
        if (submitting || reservation == null) {
            return;
        }

        String energyText = energyInput.getText() == null ? "" : energyInput.getText().toString();
        String energyError = ValidationUtils.validateEnergy(energyText, effectiveCapacity());
        if (energyError != null) {
            energyLayout.setError(energyError);
            energyInput.requestFocus();
            return;
        }
        energyLayout.setError(null);

        double energy;
        try {
            energy = Double.parseDouble(energyText.trim());
        } catch (NumberFormatException ex) {
            // validateEnergy already parsed this successfully, so this is defensive only.
            energyLayout.setError(getString(R.string.error_generic));
            return;
        }

        boolean slotChanged = selectedSlot != null
                && selectedSlot.getId() != null
                && !selectedSlot.getId().equals(reservation.getSlotId());
        boolean energyChanged = Math.abs(energy - reservation.getEnergyKwh()) > ENERGY_EPSILON;

        if (!slotChanged && !energyChanged) {
            // UpdateReservationRequest has both fields optional, so an empty body would be a
            // pointless round trip. The user is told rather than being left wondering.
            info(getString(R.string.action_edit_booking),
                    "Nothing has changed yet. Pick a different slot or enter a different energy amount.");
            return;
        }

        JSONObject body = new JSONObject();
        try {
            // UpdateReservationRequest = { slotId?, energyKwh? } - only what changed is sent.
            if (slotChanged) {
                body.put("slotId", selectedSlot.getId());
            }
            if (energyChanged) {
                body.put("energyKwh", energy);
            }
        } catch (JSONException ex) {
            energyLayout.setError(getString(R.string.error_generic));
            return;
        }

        sendUpdate(body);
    }

    /** Performs the PUT and routes the answer to the summary screen or to an explanation. */
    private void sendUpdate(JSONObject body) {
        // The path carries the Mongo id of the booking being changed.
        setSubmitting(true);
        String path = Constants.EP_RESERVATIONS + "/" + reservation.getId();

        api.put(path, body, token(), new ApiClient.Callback() {

            /** Receives the server's decision on the main thread. */
            @Override
            public void onResult(ApiResult result) {
                // The button is re-enabled whichever way the call went.
                setSubmitting(false);

                if (result.isSuccess()) {
                    openSummary(result.getMessage(),
                            result.getDataObject() == null ? null : result.getDataObject().toString());
                    return;
                }
                handleUpdateFailure(result);
            }
        });
    }

    /** Toggles the submitting state, the spinner and the save button. */
    private void setSubmitting(boolean inFlight) {
        // Prevents a duplicate update from a double tap.
        submitting = inFlight;
        submitProgress.setVisibility(inFlight ? View.VISIBLE : View.GONE);
        updateSaveState();
    }

    /** Explains a rejected update, branching on the API's error code. */
    private void handleUpdateFailure(ApiResult result) {
        // The server's own wording is always shown; the app only picks how to present it.
        String code = result.getErrorCode();

        if (Constants.ERR_RES_UPDATE_12H.equals(code)) {
            // THE 12 HOUR RULE LIVES IN THE API. The canModify flag this screen was opened with
            // may simply have gone stale while the form was on screen, so the server refuses the
            // change and its message is presented verbatim in a dialog the user must acknowledge.
            info(getString(R.string.action_edit_booking), result.getMessage());
            return;
        }

        if (Constants.ERR_SLOT_UNAVAILABLE.equals(code)) {
            // Rule R8 lives in the API too. Another prosumer may have taken the slot between the
            // list being drawn and this request, so the list is fetched again.
            info(getString(R.string.label_select_slot), result.getMessage());
            loadSlots();
            return;
        }

        if (Constants.ERR_RES_WINDOW.equals(code)) {
            // Rule R1 lives in the API. The picker's bounds are only a hint.
            info(getString(R.string.label_select_date), result.getMessage());
            return;
        }

        showApiError(result);
    }

    /** Hands the updated reservation to the shared summary screen. */
    private void openSummary(String message, String reservationJson) {
        // The summary screen renders whatever the server returned, so the prosumer sees the
        // booking exactly as the API now holds it.
        Intent intent = new Intent(this, BookingSummaryActivity.class);
        intent.putExtra(Constants.EXTRA_SUMMARY_SUCCESS, true);
        intent.putExtra(Constants.EXTRA_SUMMARY_TITLE, getString(R.string.summary_booking_updated));
        intent.putExtra(Constants.EXTRA_SUMMARY_MESSAGE, message);
        if (reservationJson != null) {
            intent.putExtra(Constants.EXTRA_RESERVATION_JSON, reservationJson);
        }
        startActivity(intent);
        finish();
    }
}
