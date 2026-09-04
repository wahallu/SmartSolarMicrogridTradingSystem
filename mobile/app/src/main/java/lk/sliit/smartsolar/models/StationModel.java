/*
 * =============================================================================
 * File        : StationModel.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : models
 * Description : Client side representation of the StationDto / NearbyStationDto
 *               returned by the Web API. The latitude and longitude carried here
 *               are what the Google Maps screen plots, and distanceKm is the
 *               value the SERVER computed - the phone never calculates distances
 *               itself, because all business logic belongs to the FAT service.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.models;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.Serializable;
import java.util.ArrayList;
import java.util.List;

/** A microgrid node (solar grid hub). */
public class StationModel implements Serializable {

    private String id;
    private String stationCode;
    private String name;
    private String type;
    private String address;
    private String city;
    private double latitude;
    private double longitude;
    private double capacityKwh;
    private int totalBatterySlots;
    private int availableBatterySlots;
    private String openTime;
    private String closeTime;
    private String contactNumber;
    private String status;
    private long activeReservationCount;

    /** Distance from the user in kilometres, supplied by GET /stations/nearby. */
    private double distanceKm = -1;

    /** Creates an empty model. */
    public StationModel() {
        // Required so the SQLite cache reader can populate fields one at a time.
    }

    /** Builds a model from a station JSON object. */
    public static StationModel fromJson(JSONObject json) {
        // Handles both StationDto and NearbyStationDto; distanceKm is simply absent on the former.
        StationModel station = new StationModel();
        if (json == null) {
            return station;
        }

        station.id = json.optString("id", "");
        station.stationCode = json.optString("stationCode", "");
        station.name = json.optString("name", "");
        station.type = json.optString("type", "");
        station.address = json.optString("address", "");
        station.city = json.optString("city", "");
        station.latitude = json.optDouble("latitude", 0);
        station.longitude = json.optDouble("longitude", 0);
        station.capacityKwh = json.optDouble("capacityKwh", 0);
        station.totalBatterySlots = json.optInt("totalBatterySlots", 0);
        station.availableBatterySlots = json.optInt("availableBatterySlots", 0);
        station.contactNumber = json.optString("contactNumber", "");
        station.status = json.optString("status", "");
        station.activeReservationCount = json.optLong("activeReservationCount", 0);
        station.distanceKm = json.optDouble("distanceKm", -1);

        JSONObject hours = json.optJSONObject("operatingHours");
        if (hours != null) {
            station.openTime = hours.optString("openTime", "06:00");
            station.closeTime = hours.optString("closeTime", "22:00");
        }
        return station;
    }

    /** Builds a list of models from a station JSON array. */
    public static List<StationModel> fromJsonArray(JSONArray array) {
        // Used by the map screen, the station list and the booking station picker.
        List<StationModel> stations = new ArrayList<>();
        if (array == null) {
            return stations;
        }
        for (int i = 0; i < array.length(); i++) {
            JSONObject item = array.optJSONObject(i);
            if (item != null) {
                stations.add(fromJson(item));
            }
        }
        return stations;
    }

    /** True when the node is currently in service. */
    public boolean isActive() {
        // Inactive nodes are shown greyed out rather than hidden, so the user understands why.
        return "Active".equals(status);
    }

    /** Opening hours formatted for display, e.g. "06:00 - 22:00". */
    public String getHoursLabel() {
        // Falls back to a sensible default when a node omitted its hours.
        String open = openTime == null ? "06:00" : openTime;
        String close = closeTime == null ? "22:00" : closeTime;
        return open + " - " + close;
    }

    /** Battery availability formatted for display, e.g. "8 / 12 bays free". */
    public String getSlotsLabel() {
        // Shown on the map info window and on the station cards.
        return availableBatterySlots + " / " + totalBatterySlots + " bays free";
    }

    /** Distance formatted for display, or an empty string when unknown. */
    public String getDistanceLabel() {
        // A negative value means the server did not supply a distance for this row.
        if (distanceKm < 0) {
            return "";
        }
        return String.format(java.util.Locale.getDefault(), "%.1f km away", distanceKm);
    }

    // ---------------------------------------------------------------------
    // Getters and setters
    // ---------------------------------------------------------------------

    /** Mongo identifier of the node. */
    public String getId() { return id; }

    /** Sets the Mongo identifier. */
    public void setId(String id) { this.id = id; }

    /** Unique human readable code such as STN-001. */
    public String getStationCode() { return stationCode; }

    /** Sets the station code. */
    public void setStationCode(String code) { this.stationCode = code; }

    /** Display name of the node. */
    public String getName() { return name; }

    /** Sets the display name. */
    public void setName(String name) { this.name = name; }

    /** Node classification: Solar, Hybrid or Battery. */
    public String getType() { return type; }

    /** Sets the node classification. */
    public void setType(String type) { this.type = type; }

    /** Street address of the node. */
    public String getAddress() { return address; }

    /** Sets the street address. */
    public void setAddress(String address) { this.address = address; }

    /** City the node sits in. */
    public String getCity() { return city; }

    /** Sets the city. */
    public void setCity(String city) { this.city = city; }

    /** GPS latitude, plotted on the Google Map. */
    public double getLatitude() { return latitude; }

    /** Sets the GPS latitude. */
    public void setLatitude(double latitude) { this.latitude = latitude; }

    /** GPS longitude, plotted on the Google Map. */
    public double getLongitude() { return longitude; }

    /** Sets the GPS longitude. */
    public void setLongitude(double longitude) { this.longitude = longitude; }

    /** Rated capacity of the node in kilowatt hours. */
    public double getCapacityKwh() { return capacityKwh; }

    /** Sets the rated capacity. */
    public void setCapacityKwh(double capacity) { this.capacityKwh = capacity; }

    /** Total battery bays installed at the node. */
    public int getTotalBatterySlots() { return totalBatterySlots; }

    /** Sets the total battery bay count. */
    public void setTotalBatterySlots(int total) { this.totalBatterySlots = total; }

    /** Battery bays currently free. */
    public int getAvailableBatterySlots() { return availableBatterySlots; }

    /** Sets the free battery bay count. */
    public void setAvailableBatterySlots(int available) { this.availableBatterySlots = available; }

    /** Contact telephone number for the node. */
    public String getContactNumber() { return contactNumber; }

    /** Sets the contact number. */
    public void setContactNumber(String number) { this.contactNumber = number; }

    /** Node status: Active or Inactive. */
    public String getStatus() { return status; }

    /** Sets the node status. */
    public void setStatus(String status) { this.status = status; }

    /** Future Pending or Approved reservations held by this node. */
    public long getActiveReservationCount() { return activeReservationCount; }

    /** Server computed distance from the user, in kilometres. */
    public double getDistanceKm() { return distanceKm; }

    /** Sets the server computed distance. */
    public void setDistanceKm(double distanceKm) { this.distanceKm = distanceKm; }

    /** Opening time, "HH:mm". */
    public String getOpenTime() { return openTime; }

    /** Closing time, "HH:mm". */
    public String getCloseTime() { return closeTime; }
}
