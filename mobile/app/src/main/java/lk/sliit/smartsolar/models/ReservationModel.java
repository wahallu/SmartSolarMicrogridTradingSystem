/* =============================================================================
 * File        : ReservationModel.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Models / Reservation Model
 * Description : Parses reservation details returned by the Web API.
 * Author      : IT22228062 - Akash
 * Created     : 2026-09-17
 * =============================================================================
 */

package lk.sliit.smartsolar.models;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.Serializable;
import java.util.ArrayList;
import java.util.List;

import lk.sliit.smartsolar.util.Constants;

/** A power trading reservation placed by a solar prosumer. */
public class ReservationModel implements Serializable {

    private String id;
    private String reservationNo;
    private String prosumerNic;
    private String prosumerName;
    private String prosumerPhone;
    private String stationId;
    private String stationCode;
    private String stationName;
    private String slotId;
    private String slotCode;
    private String reservationStartUtc;
    private String reservationEndUtc;
    private String startTime;
    private String endTime;
    private String slotType;
    private double energyKwh;
    private Double deliveredKwh;
    private String status;
    private String qrToken;
    private String approvedBy;
    private String completedBy;
    private String cancelReason;
    private String rejectedReason;
    private String operatorNotes;

    /** Server verdict on rule R2 - may this booking still be changed? */
    private boolean canModify;

    /** Server verdict on rule R3 - may this booking still be cancelled? */
    private boolean canCancel;

    private double hoursUntilStart;
    private String createdAt;

    /** Creates an empty model. */
    public ReservationModel() {
        // Required by the SQLite cache reader.
    }

    /** Builds a model from a reservation JSON object. */
    public static ReservationModel fromJson(JSONObject json) {
        // Every field name here matches ReservationDto in the Web API exactly.
        ReservationModel r = new ReservationModel();
        if (json == null) {
            return r;
        }

        r.id = json.optString("id", "");
        r.reservationNo = json.optString("reservationNo", "");
        r.prosumerNic = json.optString("prosumerNic", "");
        r.prosumerName = json.optString("prosumerName", "");
        r.prosumerPhone = json.optString("prosumerPhone", "");
        r.stationId = json.optString("stationId", "");
        r.stationCode = json.optString("stationCode", "");
        r.stationName = json.optString("stationName", "");
        r.slotId = json.optString("slotId", "");
        r.slotCode = json.optString("slotCode", "");
        r.reservationStartUtc = json.optString("reservationStartUtc", "");
        r.reservationEndUtc = json.optString("reservationEndUtc", "");
        r.startTime = json.optString("startTime", "");
        r.endTime = json.optString("endTime", "");
        r.slotType = json.optString("slotType", "");
        r.energyKwh = json.optDouble("energyKwh", 0);
        r.deliveredKwh = json.isNull("deliveredKwh") ? null : json.optDouble("deliveredKwh");
        r.status = json.optString("status", "");
        r.qrToken = json.isNull("qrToken") ? null : json.optString("qrToken", null);
        r.approvedBy = json.isNull("approvedBy") ? null : json.optString("approvedBy", null);
        r.completedBy = json.isNull("completedBy") ? null : json.optString("completedBy", null);
        r.cancelReason = json.isNull("cancelReason") ? null : json.optString("cancelReason", null);
        r.rejectedReason = json.isNull("rejectedReason") ? null : json.optString("rejectedReason", null);
        r.operatorNotes = json.isNull("operatorNotes") ? null : json.optString("operatorNotes", null);
        r.canModify = json.optBoolean("canModify", false);
        r.canCancel = json.optBoolean("canCancel", false);
        r.hoursUntilStart = json.optDouble("hoursUntilStart", 0);
        r.createdAt = json.optString("createdAt", "");
        return r;
    }

    /** Builds a list of models from a reservation JSON array. */
    public static List<ReservationModel> fromJsonArray(JSONArray array) {
        // Used by every booking list screen and by the dashboard's "upcoming" strip.
        List<ReservationModel> list = new ArrayList<>();
        if (array == null) {
            return list;
        }
        for (int i = 0; i < array.length(); i++) {
            JSONObject item = array.optJSONObject(i);
            if (item != null) {
                list.add(fromJson(item));
            }
        }
        return list;
    }

    /** True when the booking is waiting for staff approval. */
    public boolean isPending() {
        // Pending bookings show an "awaiting approval" chip and no QR code.
        return Constants.RES_PENDING.equals(status);
    }

    /** True when the booking has been approved and carries a QR code. */
    public boolean isApproved() {
        // Only an approved booking can display a QR code for the operator to scan.
        return Constants.RES_APPROVED.equals(status);
    }

    /** True when the energy transfer has been finalised. */
    public boolean isCompleted() {
        // Completed bookings appear in history with the delivered kWh.
        return Constants.RES_COMPLETED.equals(status);
    }

    /** True when the booking was cancelled. */
    public boolean isCancelled() {
        // Cancelled bookings are read only.
        return Constants.RES_CANCELLED.equals(status);
    }

    /** True when the booking was refused by staff. */
    public boolean isRejected() {
        // Rejected bookings display the staff reason.
        return Constants.RES_REJECTED.equals(status);
    }

    /** True when the app should offer a QR code for this booking. */
    public boolean hasQrCode() {
        // The token is cleared by the server on cancel and on completion, so this is reliable.
        return isApproved() && qrToken != null && !qrToken.isEmpty();
    }

    /** Human readable summary of the slot, e.g. "08:00 - 10:00". */
    public String getTimeRange() {
        // Both times come straight from the server so no local formatting is needed.
        return startTime + " - " + endTime;
    }

    /** Energy figure formatted for display. */
    public String getEnergyLabel() {
        // Shows the delivered amount once the transfer has been finalised.
        if (deliveredKwh != null) {
            return String.format(java.util.Locale.getDefault(), "%.1f kWh delivered", deliveredKwh);
        }
        return String.format(java.util.Locale.getDefault(), "%.1f kWh", energyKwh);
    }

    /** Slot direction formatted for display. */
    public String getSlotTypeLabel() {
        // "DropOff" is not a phrase to show a user; expand it.
        if (Constants.SLOT_DROP_OFF.equals(slotType)) {
            return "Energy drop-off";
        }
        return "Battery charging";
    }

    // ---------------------------------------------------------------------
    // Getters and setters
    // ---------------------------------------------------------------------

    /** Mongo identifier of the reservation. */
    public String getId() { return id; }

    /** Sets the Mongo identifier. */
    public void setId(String id) { this.id = id; }

    /** Human readable reference, e.g. RSV-20260904-0001. */
    public String getReservationNo() { return reservationNo; }

    /** Sets the reservation reference. */
    public void setReservationNo(String no) { this.reservationNo = no; }

    /** NIC of the owning prosumer. */
    public String getProsumerNic() { return prosumerNic; }

    /** Sets the owning prosumer NIC. */
    public void setProsumerNic(String nic) { this.prosumerNic = nic; }

    /** Name of the owning prosumer. */
    public String getProsumerName() { return prosumerName; }

    /** Sets the prosumer name. */
    public void setProsumerName(String name) { this.prosumerName = name; }

    /** Telephone number of the owning prosumer. */
    public String getProsumerPhone() { return prosumerPhone; }

    /** Identifier of the microgrid node. */
    public String getStationId() { return stationId; }

    /** Sets the node identifier. */
    public void setStationId(String id) { this.stationId = id; }

    /** Code of the microgrid node. */
    public String getStationCode() { return stationCode; }

    /** Sets the node code. */
    public void setStationCode(String code) { this.stationCode = code; }

    /** Name of the microgrid node. */
    public String getStationName() { return stationName; }

    /** Sets the node name. */
    public void setStationName(String name) { this.stationName = name; }

    /** Identifier of the booked slot. */
    public String getSlotId() { return slotId; }

    /** Sets the booked slot identifier. */
    public void setSlotId(String id) { this.slotId = id; }

    /** Code of the booked slot. */
    public String getSlotCode() { return slotCode; }

    /** Sets the booked slot code. */
    public void setSlotCode(String code) { this.slotCode = code; }

    /** Slot start instant, ISO-8601 UTC. */
    public String getReservationStartUtc() { return reservationStartUtc; }

    /** Sets the slot start instant. */
    public void setReservationStartUtc(String value) { this.reservationStartUtc = value; }

    /** Slot end instant, ISO-8601 UTC. */
    public String getReservationEndUtc() { return reservationEndUtc; }

    /** Sets the slot end instant. */
    public void setReservationEndUtc(String value) { this.reservationEndUtc = value; }

    /** Slot start wall clock time, "HH:mm". */
    public String getStartTime() { return startTime; }

    /** Sets the slot start time. */
    public void setStartTime(String value) { this.startTime = value; }

    /** Slot end wall clock time, "HH:mm". */
    public String getEndTime() { return endTime; }

    /** Sets the slot end time. */
    public void setEndTime(String value) { this.endTime = value; }

    /** Direction of the transfer: Charging or DropOff. */
    public String getSlotType() { return slotType; }

    /** Sets the transfer direction. */
    public void setSlotType(String value) { this.slotType = value; }

    /** Energy requested, in kWh. */
    public double getEnergyKwh() { return energyKwh; }

    /** Sets the requested energy. */
    public void setEnergyKwh(double value) { this.energyKwh = value; }

    /** Energy actually transferred, or null before completion. */
    public Double getDeliveredKwh() { return deliveredKwh; }

    /** Sets the delivered energy. */
    public void setDeliveredKwh(Double value) { this.deliveredKwh = value; }

    /** Reservation status name. */
    public String getStatus() { return status; }

    /** Sets the reservation status. */
    public void setStatus(String status) { this.status = status; }

    /** QR token issued at approval, or null. */
    public String getQrToken() { return qrToken; }

    /** Sets the QR token. */
    public void setQrToken(String token) { this.qrToken = token; }

    /** Username of the staff member who approved the booking. */
    public String getApprovedBy() { return approvedBy; }

    /** Username of the operator who finalised the transfer. */
    public String getCompletedBy() { return completedBy; }

    /** Reason recorded when the booking was cancelled. */
    public String getCancelReason() { return cancelReason; }

    /** Reason recorded when the booking was rejected. */
    public String getRejectedReason() { return rejectedReason; }

    /** Free text notes captured by the operator at completion. */
    public String getOperatorNotes() { return operatorNotes; }

    /** Server verdict on rule R2 - never recomputed on the phone. */
    public boolean canModify() { return canModify; }

    /** Sets the server's modify verdict. */
    public void setCanModify(boolean value) { this.canModify = value; }

    /** Server verdict on rule R3 - never recomputed on the phone. */
    public boolean canCancel() { return canCancel; }

    /** Sets the server's cancel verdict. */
    public void setCanCancel(boolean value) { this.canCancel = value; }

    /** Hours remaining until the slot starts; negative once it has passed. */
    public double getHoursUntilStart() { return hoursUntilStart; }

    /** Sets the hours remaining until start. */
    public void setHoursUntilStart(double value) { this.hoursUntilStart = value; }

    /** When the booking was created. */
    public String getCreatedAt() { return createdAt; }
}
