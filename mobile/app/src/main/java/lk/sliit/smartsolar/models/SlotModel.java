/* =============================================================================
 * File        : SlotModel.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Models / Slot Model
 * Description : Parses booking-slot details returned by the Web API.
 * Author      : IT22207272 - Nisal
 * Created     : 2026-09-10
 * =============================================================================
 */

package lk.sliit.smartsolar.models;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.Serializable;
import java.util.ArrayList;
import java.util.List;

import lk.sliit.smartsolar.util.Constants;

/** A bookable energy window at a microgrid node. */
public class SlotModel implements Serializable {

    private String id;
    private String stationId;
    private String stationCode;
    private String stationName;
    private String slotCode;
    private String slotDate;
    private String startTime;
    private String endTime;
    private String startUtc;
    private String endUtc;
    private String slotType;
    private double energyCapacityKwh;
    private int batterySlotNumber;
    private int maxBookings;
    private int bookedCount;
    private int remainingCapacity;
    private String status;

    /** Server verdict on rule R8 - would a booking be accepted right now? */
    private boolean isBookable;

    /** Creates an empty model. */
    public SlotModel() {
        // Required for the spinner adapter's placeholder entry.
    }

    /** Builds a model from a slot JSON object. */
    public static SlotModel fromJson(JSONObject json) {
        // Field names match SlotDto in the Web API exactly.
        SlotModel slot = new SlotModel();
        if (json == null) {
            return slot;
        }

        slot.id = json.optString("id", "");
        slot.stationId = json.optString("stationId", "");
        slot.stationCode = json.optString("stationCode", "");
        slot.stationName = json.optString("stationName", "");
        slot.slotCode = json.optString("slotCode", "");
        slot.slotDate = json.optString("slotDate", "");
        slot.startTime = json.optString("startTime", "");
        slot.endTime = json.optString("endTime", "");
        slot.startUtc = json.optString("startUtc", "");
        slot.endUtc = json.optString("endUtc", "");
        slot.slotType = json.optString("slotType", "");
        slot.energyCapacityKwh = json.optDouble("energyCapacityKwh", 0);
        slot.batterySlotNumber = json.optInt("batterySlotNumber", 0);
        slot.maxBookings = json.optInt("maxBookings", 1);
        slot.bookedCount = json.optInt("bookedCount", 0);
        slot.remainingCapacity = json.optInt("remainingCapacity", 0);
        slot.status = json.optString("status", "");
        slot.isBookable = json.optBoolean("isBookable", false);
        return slot;
    }

    /** Builds a list of models from a slot JSON array. */
    public static List<SlotModel> fromJsonArray(JSONArray array) {
        // Used by the slot picker on the new booking screen.
        List<SlotModel> slots = new ArrayList<>();
        if (array == null) {
            return slots;
        }
        for (int i = 0; i < array.length(); i++) {
            JSONObject item = array.optJSONObject(i);
            if (item != null) {
                slots.add(fromJson(item));
            }
        }
        return slots;
    }

    /** Label shown in the slot picker, e.g. "S3  08:00 - 10:00  (Charging, 20 kWh)". */
    public String getPickerLabel() {
        // One dense line keeps the spinner readable on a narrow phone screen.
        String typeLabel = Constants.SLOT_DROP_OFF.equals(slotType) ? "Drop-off" : "Charging";
        return String.format(java.util.Locale.getDefault(),
                "%s  %s - %s  (%s, %.0f kWh)", slotCode, startTime, endTime, typeLabel, energyCapacityKwh);
    }

    /** Slot direction formatted for display. */
    public String getSlotTypeLabel() {
        // Matches the wording used on the reservation screens.
        return Constants.SLOT_DROP_OFF.equals(slotType) ? "Energy drop-off" : "Battery charging";
    }

    /** Time range formatted for display. */
    public String getTimeRange() {
        // Both values come from the server already formatted.
        return startTime + " - " + endTime;
    }

    // ---------------------------------------------------------------------
    // Getters
    // ---------------------------------------------------------------------

    /** Mongo identifier of the slot. */
    public String getId() { return id; }

    /** Identifier of the owning microgrid node. */
    public String getStationId() { return stationId; }

    /** Code of the owning microgrid node. */
    public String getStationCode() { return stationCode; }

    /** Name of the owning microgrid node. */
    public String getStationName() { return stationName; }

    /** Short label unique within a node and date, e.g. "S3". */
    public String getSlotCode() { return slotCode; }

    /** Calendar day the slot belongs to. */
    public String getSlotDate() { return slotDate; }

    /** Start wall clock time, "HH:mm". */
    public String getStartTime() { return startTime; }

    /** End wall clock time, "HH:mm". */
    public String getEndTime() { return endTime; }

    /** Absolute start instant, ISO-8601 UTC. */
    public String getStartUtc() { return startUtc; }

    /** Absolute end instant, ISO-8601 UTC. */
    public String getEndUtc() { return endUtc; }

    /** Direction of the transfer: Charging or DropOff. */
    public String getSlotType() { return slotType; }

    /** Maximum energy that may be traded in this window, in kWh. */
    public double getEnergyCapacityKwh() { return energyCapacityKwh; }

    /** Which battery bay at the node this slot occupies. */
    public int getBatterySlotNumber() { return batterySlotNumber; }

    /** How many bookings this slot accepts. */
    public int getMaxBookings() { return maxBookings; }

    /** How many bookings the slot currently holds. */
    public int getBookedCount() { return bookedCount; }

    /** Remaining booking capacity. */
    public int getRemainingCapacity() { return remainingCapacity; }

    /** Slot status: Available, Full or Blocked. */
    public String getStatus() { return status; }

    /** Server verdict on rule R8 - obeyed, never recomputed here. */
    public boolean isBookable() { return isBookable; }

    /** Picker labels are what the spinner renders, so toString mirrors them. */
    @Override
    public String toString() {
        // ArrayAdapter calls toString when no custom view is supplied.
        return getPickerLabel();
    }
}
