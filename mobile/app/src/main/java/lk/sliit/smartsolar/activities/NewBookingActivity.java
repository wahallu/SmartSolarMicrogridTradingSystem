/*
 * =============================================================================
 * File        : NewBookingActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : activities
 * Description : The prosumer's booking creation flow, which is one of the marked
 *               criteria of the assignment. It walks the user through choosing a
 *               microgrid node, a date, a slot the API reports as bookable and an
 *               energy amount, then POSTs the request to the central Web API.
 *
 *               This screen is a UI layer only. The 7 day booking window (rule
 *               R1), slot availability (rule R8) and the ownership rule (R11) are
 *               all decided by the server; the phone only presents whatever the
 *               API answers. Every place that leans on a server verdict is
 *               commented as such.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.app.DatePickerDialog;
import android.content.Intent;
import android.os.Bundle;
import android.view.View;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.ProgressBar;
import android.widget.Spinner;
import android.widget.TextView;

import androidx.annotation.Nullable;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.textfield.TextInputEditText;
import com.google.android.material.textfield.TextInputLayout;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.Calendar;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.TimeZone;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.adapters.SlotAdapter;
import lk.sliit.smartsolar.api.ApiClient;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.models.SlotModel;
import lk.sliit.smartsolar.models.StationModel;
import lk.sliit.smartsolar.util.Constants;
import lk.sliit.smartsolar.util.DateUtils;
import lk.sliit.smartsolar.util.ValidationUtils;

/** Four step screen that creates a new energy reservation. */
public class NewBookingActivity extends BaseActivity {

    // Views
    private Spinner stationSpinner;
    private ProgressBar stationProgress;
    private TextInputLayout dateLayout;
    private TextInputEditText dateInput;
    private ProgressBar slotProgress;
    private RecyclerView slotList;
    private View slotEmptyPanel;
    private TextView slotEmptyText;
    private TextInputLayout energyLayout;
    private TextInputEditText energyInput;
    private MaterialButton submitButton;
    private ProgressBar submitProgress;

    // State
    private final List<StationModel> stations = new ArrayList<>();
    private StationModel selectedStation;
    private SlotModel selectedSlot;

    /** Calendar day currently chosen, in the "yyyy-MM-dd" form the API expects. */
    private String selectedDate;

    /** Default wording of the empty panel, kept so an error message can be undone. */
    private String defaultEmptyText;

    /** True while the create request is in flight, so it cannot be sent twice. */
    private boolean submitting;

    /** Builds the screen, wires the four steps and loads the node list. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // BaseActivity creates api, session and localStore before anything else runs.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_new_booking);
        setupToolbar(R.id.toolbar, getString(R.string.new_booking_title), true);

        bindViews();
        wireListeners();

        // The flow opens on today, which is always inside the API's booking window.
        selectedDate = DateUtils.today();
        dateInput.setText(DateUtils.formatDate(selectedDate));

        loadStations();
    }

    /** Looks up every view this screen drives. */
    private void bindViews() {
        // findViewById only - the assignment forbids view binding and data binding.
        stationSpinner = findViewById(R.id.spinner_station);
        stationProgress = findViewById(R.id.progress_stations);
        dateLayout = findViewById(R.id.layout_date);
        dateInput = findViewById(R.id.input_date);
        slotProgress = findViewById(R.id.progress_slots);
        slotList = findViewById(R.id.recycler_slots);
        slotEmptyPanel = findViewById(R.id.empty_slots);
        slotEmptyText = findViewById(R.id.text_slots_empty);
        energyLayout = findViewById(R.id.layout_energy);
        energyInput = findViewById(R.id.input_energy);
        submitButton = findViewById(R.id.btn_submit);
        submitProgress = findViewById(R.id.progress_submit);

        // A vertical list of selectable slot cards.
        slotList.setLayoutManager(new LinearLayoutManager(this));
        slotList.setHasFixedSize(false);

        defaultEmptyText = slotEmptyText.getText().toString();
    }

    /** Attaches the click and selection listeners for the four steps. */
    private void wireListeners() {
        // Step 1: choosing a different node reloads the slots for the current date.
        stationSpinner.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            /** Remembers the chosen node and refreshes the slot list. */
            @Override
            public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                // The spinner and the list share an index, so the position maps straight across.
                if (position >= 0 && position < stations.size()) {
                    selectedStation = stations.get(position);
                    loadSlots();
                }
            }

            /** Clears the chosen node when the spinner selection is lost. */
            @Override
            public void onNothingSelected(AdapterView<?> parent) {
                // Leaves the form in its "nothing chosen yet" state.
                selectedStation = null;
                updateSubmitState();
            }
        });

        // Step 2: the field is read only, so both it and its wrapper open the picker.
        dateInput.setOnClickListener(v -> openDatePicker());
        dateLayout.setOnClickListener(v -> openDatePicker());

        // Step 4 / submit.
        submitButton.setOnClickListener(v -> submitBooking());
    }

    // =====================================================================
    // Step 1 - the microgrid node
    // =====================================================================

    /** Loads every Active microgrid node into the picker. */
    private void loadStations() {
        // Only Active nodes are requested: an out of service node cannot take a booking.
        showOnly(stationProgress);
        Map<String, String> query = new LinkedHashMap<>();
        query.put("status", Constants.STATUS_ACTIVE);
        query.put("pageSize", "100");

        api.get(ApiClient.withQuery(Constants.EP_STATIONS, query), token(), result -> {
            // Callbacks arrive on the main thread, so views are touched directly.
            stationProgress.setVisibility(View.GONE);

            if (!result.isSuccess()) {
                showApiError(result);
                showSlotMessage(result.getMessage());
                return;
            }

            stations.clear();
            stations.addAll(StationModel.fromJsonArray(result.getPagedItems()));

            if (stations.isEmpty()) {
                selectedStation = null;
                updateSubmitState();
                showSlotMessage(getString(R.string.empty_no_stations));
                return;
            }

            // Selecting index 0 fires onItemSelected, which loads that node's slots.
            stationSpinner.setAdapter(buildStationAdapter());
            stationSpinner.setSelection(0);
        });
    }

    /** Builds the spinner adapter holding one readable label per node. */
    private ArrayAdapter<String> buildStationAdapter() {
        // A plain string adapter keeps the picker light; the model list holds the real data.
        List<String> labels = new ArrayList<>();
        for (StationModel station : stations) {
            String city = station.getCity() == null ? "" : station.getCity();
            labels.add(station.getStationCode() + "  -  " + station.getName()
                    + (city.isEmpty() ? "" : ", " + city));
        }

        ArrayAdapter<String> adapter = new ArrayAdapter<>(
                this, android.R.layout.simple_spinner_item, labels);
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        return adapter;
    }

    // =====================================================================
    // Step 2 - the date
    // =====================================================================

    /** Opens a date picker limited to the next seven days. */
    private void openDatePicker() {
        // The minDate / maxDate bounds MIRROR business rule R1 purely as a convenience so the
        // user is not offered a day that would obviously be refused. They are NOT the rule:
        // the Web API owns the 7 day window and rejects anything outside it with
        // Constants.ERR_RES_WINDOW no matter what this picker allowed.
        Calendar today = Calendar.getInstance();

        DatePickerDialog dialog = new DatePickerDialog(
                this,
                (view, year, month, dayOfMonth) -> onDateChosen(year, month, dayOfMonth),
                today.get(Calendar.YEAR),
                today.get(Calendar.MONTH),
                today.get(Calendar.DAY_OF_MONTH));

        long nowMillis = System.currentTimeMillis();
        long windowMillis = Constants.RESERVATION_WINDOW_DAYS * 24L * 60L * 60L * 1000L;
        dialog.getDatePicker().setMinDate(nowMillis - 1000L);
        dialog.getDatePicker().setMaxDate(nowMillis + windowMillis);
        dialog.show();
    }

    /** Stores the chosen calendar day and reloads the slot list. */
    private void onDateChosen(int year, int month, int dayOfMonth) {
        // The API's date filters are UTC calendar days, and DateUtils formats in UTC. Midday is
        // used as the instant so the UTC conversion never rolls onto the neighbouring day.
        Calendar chosen = Calendar.getInstance(TimeZone.getDefault());
        chosen.set(year, month, dayOfMonth, 12, 0, 0);
        chosen.set(Calendar.MILLISECOND, 0);

        selectedDate = DateUtils.toApiDate(chosen.getTime());
        dateInput.setText(DateUtils.formatDate(selectedDate));
        loadSlots();
    }

    // =====================================================================
    // Step 3 - the energy slot
    // =====================================================================

    /** Loads the slots the API considers bookable for the chosen node and date. */
    private void loadSlots() {
        // GET /slots/available returns ONLY the slots the server would accept a booking for,
        // so the app never has to reason about capacity, blocking or start times (rule R8).
        if (selectedStation == null || selectedDate == null) {
            return;
        }

        // A new query invalidates any earlier choice.
        selectedSlot = null;
        energyLayout.setError(null);
        updateEnergyHelper();
        updateSubmitState();

        showOnly(slotProgress, slotList, slotEmptyPanel);

        Map<String, String> query = new LinkedHashMap<>();
        query.put("stationId", selectedStation.getId());
        query.put("date", selectedDate);

        api.get(ApiClient.withQuery(Constants.EP_SLOTS_AVAILABLE, query), token(), result -> {
            // Error state: the empty panel doubles as the error panel for this section.
            if (!result.isSuccess()) {
                showApiError(result);
                showSlotMessage(result.getMessage());
                return;
            }

            // The endpoint answers with a bare array; the paged shape is accepted defensively.
            List<SlotModel> slots = result.getDataArray() != null
                    ? SlotModel.fromJsonArray(result.getDataArray())
                    : SlotModel.fromJsonArray(result.getPagedItems());

            if (slots.isEmpty()) {
                showSlotMessage(defaultEmptyText);
                return;
            }

            // A fresh adapter also clears the previous single selection.
            slotList.setAdapter(new SlotAdapter(slots, slot -> onSlotSelected(slot)));
            showOnly(slotList, slotProgress, slotEmptyPanel);
        });
    }

    /** Records the slot the user tapped and refreshes the energy field. */
    private void onSlotSelected(SlotModel slot) {
        // SlotAdapter only offers slots whose isBookable() flag the SERVER set, so no
        // availability decision is taken here.
        selectedSlot = slot;
        energyLayout.setError(null);
        updateEnergyHelper();
        updateSubmitState();
    }

    /** Shows the empty / error panel with the given message. */
    private void showSlotMessage(String message) {
        // Falls back to the standard wording when the server sent nothing useful.
        slotEmptyText.setText(message == null || message.trim().isEmpty()
                ? defaultEmptyText
                : message);
        showOnly(slotEmptyPanel, slotProgress, slotList);
    }

    // =====================================================================
    // Step 4 - the energy amount
    // =====================================================================

    /** Shows the selected slot's capacity as helper text under the energy field. */
    private void updateEnergyHelper() {
        // The ceiling shown is the figure the API sent for that slot, never a local guess.
        if (selectedSlot == null) {
            energyLayout.setHelperText(null);
            return;
        }
        energyLayout.setHelperText(String.format(Locale.getDefault(), "%s: %.1f kWh",
                getString(R.string.label_capacity), selectedSlot.getEnergyCapacityKwh()));
    }

    /** Enables the submit button only when a node, a slot and no request in flight line up. */
    private void updateSubmitState() {
        // Keeping the button disabled removes the need for "please pick a slot" prompts.
        submitButton.setEnabled(!submitting && selectedStation != null && selectedSlot != null);
    }

    // =====================================================================
    // Submit
    // =====================================================================

    /** Validates the form and POSTs the reservation request. */
    private void submitBooking() {
        // A second tap while the first request is running is ignored.
        if (submitting || selectedStation == null || selectedSlot == null) {
            return;
        }

        String energyText = energyInput.getText() == null ? "" : energyInput.getText().toString();
        String energyError = ValidationUtils.validateEnergy(
                energyText, selectedSlot.getEnergyCapacityKwh());
        if (energyError != null) {
            energyLayout.setError(energyError);
            energyInput.requestFocus();
            return;
        }
        energyLayout.setError(null);

        JSONObject body = new JSONObject();
        try {
            // prosumerNic is deliberately NOT sent: under rule R11 the API forces the
            // reservation onto the calling prosumer's own NIC, taken from the JWT.
            body.put("stationId", selectedStation.getId());
            body.put("slotId", selectedSlot.getId());
            body.put("energyKwh", Double.parseDouble(energyText.trim()));
        } catch (JSONException | NumberFormatException ex) {
            energyLayout.setError(getString(R.string.error_generic));
            return;
        }

        setSubmitting(true);
        api.post(Constants.EP_RESERVATIONS, body, token(), result -> {
            // The button is re-enabled whichever way the call went.
            setSubmitting(false);

            if (result.isSuccess()) {
                openSummary(result.getMessage(),
                        result.getDataObject() == null ? null : result.getDataObject().toString());
                return;
            }
            handleCreateFailure(result);
        });
    }

    /** Toggles the submitting state, the spinner and the button. */
    private void setSubmitting(boolean inFlight) {
        // Prevents a duplicate reservation from a double tap.
        submitting = inFlight;
        submitProgress.setVisibility(inFlight ? View.VISIBLE : View.GONE);
        updateSubmitState();
    }

    /** Explains a rejected booking, branching on the API's error code. */
    private void handleCreateFailure(ApiResult result) {
        // The server's own wording is always shown; the app only picks how to present it.
        String code = result.getErrorCode();

        if (Constants.ERR_RES_WINDOW.equals(code)) {
            // Rule R1 lives in the API. The picker's bounds are only a hint, so the server
            // can still refuse a date - for example one that has just moved into the past.
            info(getString(R.string.new_booking_title), result.getMessage());
            return;
        }

        if (Constants.ERR_SLOT_UNAVAILABLE.equals(code)) {
            // Rule R8 lives in the API too. Another prosumer may have taken the slot between
            // the list being drawn and this request, so the list is fetched again.
            info(getString(R.string.label_select_slot), result.getMessage());
            loadSlots();
            return;
        }

        showApiError(result);
    }

    /** Hands the created reservation to the shared summary screen. */
    private void openSummary(String message, String reservationJson) {
        // The summary screen renders whatever the server returned, including the reference number.
        Intent intent = new Intent(this, BookingSummaryActivity.class);
        intent.putExtra(Constants.EXTRA_SUMMARY_SUCCESS, true);
        intent.putExtra(Constants.EXTRA_SUMMARY_TITLE, getString(R.string.summary_booking_created));
        intent.putExtra(Constants.EXTRA_SUMMARY_MESSAGE, message);
        if (reservationJson != null) {
            intent.putExtra(Constants.EXTRA_RESERVATION_JSON, reservationJson);
        }
        startActivity(intent);
        finish();
    }
}
