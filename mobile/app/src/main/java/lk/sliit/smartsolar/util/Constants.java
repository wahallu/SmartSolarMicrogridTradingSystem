/*
 * =============================================================================
 * File        : Constants.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : util
 * Description : Every literal shared across the application: API paths, intent
 *               extra keys, role and status names and the error codes returned
 *               by the central Web API. Keeping them here means a typo is a
 *               compile error rather than a silent runtime mismatch.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.util;

/** Shared literals used across activities, the API client and the database. */
public final class Constants {

    /** Utility class - never instantiated. */
    private Constants() {
        // Prevents accidental construction of a constants holder.
    }

    // ---------------------------------------------------------------------
    // Roles - these strings must match the UserRole enum names in the Web API.
    // ---------------------------------------------------------------------
    public static final String ROLE_BACKOFFICE = "Backoffice";
    public static final String ROLE_GRID_OPERATOR = "GridOperator";
    public static final String ROLE_PROSUMER = "Prosumer";

    // ---------------------------------------------------------------------
    // Account statuses - must match AccountStatus in the Web API.
    // ---------------------------------------------------------------------
    public static final String STATUS_PENDING = "Pending";
    public static final String STATUS_ACTIVE = "Active";
    public static final String STATUS_DEACTIVATED = "Deactivated";

    // ---------------------------------------------------------------------
    // Reservation statuses - must match ReservationStatus in the Web API.
    // ---------------------------------------------------------------------
    public static final String RES_PENDING = "Pending";
    public static final String RES_APPROVED = "Approved";
    public static final String RES_COMPLETED = "Completed";
    public static final String RES_CANCELLED = "Cancelled";
    public static final String RES_REJECTED = "Rejected";

    // ---------------------------------------------------------------------
    // Slot types.
    // ---------------------------------------------------------------------
    public static final String SLOT_CHARGING = "Charging";
    public static final String SLOT_DROP_OFF = "DropOff";

    // ---------------------------------------------------------------------
    // API endpoint paths, relative to BuildConfig.API_BASE_URL.
    // ---------------------------------------------------------------------
    public static final String EP_LOGIN = "auth/login";
    public static final String EP_REGISTER = "auth/register";
    public static final String EP_ME = "auth/me";
    public static final String EP_CHANGE_PASSWORD = "auth/change-password";

    public static final String EP_PROSUMERS = "prosumers";
    public static final String EP_STATIONS = "stations";
    public static final String EP_STATIONS_NEARBY = "stations/nearby";
    public static final String EP_SLOTS = "slots";
    public static final String EP_SLOTS_AVAILABLE = "slots/available";

    public static final String EP_RESERVATIONS = "reservations";
    public static final String EP_RESERVATIONS_MY = "reservations/my";
    public static final String EP_RESERVATIONS_HISTORY = "reservations/history";
    public static final String EP_RESERVATIONS_DASHBOARD = "reservations/dashboard";
    public static final String EP_VERIFY_QR = "reservations/verify-qr";
    public static final String EP_COMPLETE = "reservations/complete";

    // ---------------------------------------------------------------------
    // Error codes returned by the Web API (see docs/API_CONTRACT.md section 5).
    // The app branches on these rather than on English message text.
    // ---------------------------------------------------------------------
    public static final String ERR_ACCOUNT_PENDING = "ACCOUNT_PENDING";
    public static final String ERR_ACCOUNT_DEACTIVATED = "ACCOUNT_DEACTIVATED";
    public static final String ERR_NIC_EXISTS = "NIC_ALREADY_EXISTS";
    public static final String ERR_RES_WINDOW = "RES_WINDOW_7_DAYS";
    public static final String ERR_RES_UPDATE_12H = "RES_UPDATE_12H";
    public static final String ERR_RES_CANCEL_12H = "RES_CANCEL_12H";
    public static final String ERR_SLOT_UNAVAILABLE = "SLOT_UNAVAILABLE";
    public static final String ERR_QR_INVALID = "QR_INVALID";
    public static final String ERR_QR_NOT_APPROVED = "QR_NOT_APPROVED";
    public static final String ERR_QR_OUT_OF_WINDOW = "QR_OUT_OF_WINDOW";

    // ---------------------------------------------------------------------
    // Intent extra keys used to pass data between activities.
    // ---------------------------------------------------------------------
    public static final String EXTRA_RESERVATION_ID = "extra_reservation_id";
    public static final String EXTRA_RESERVATION_JSON = "extra_reservation_json";
    public static final String EXTRA_STATION_ID = "extra_station_id";
    public static final String EXTRA_STATION_JSON = "extra_station_json";
    public static final String EXTRA_SUMMARY_TITLE = "extra_summary_title";
    public static final String EXTRA_SUMMARY_MESSAGE = "extra_summary_message";
    public static final String EXTRA_SUMMARY_SUCCESS = "extra_summary_success";
    public static final String EXTRA_SUMMARY_ACTION = "extra_summary_action";
    public static final String EXTRA_QR_TOKEN = "extra_qr_token";
    public static final String EXTRA_SCOPE = "extra_scope";

    // ---------------------------------------------------------------------
    // Reservation scopes accepted by GET /reservations/my.
    // ---------------------------------------------------------------------
    public static final String SCOPE_CURRENT = "Current";
    public static final String SCOPE_PENDING = "Pending";
    public static final String SCOPE_HISTORY = "History";
    public static final String SCOPE_ALL = "All";

    // ---------------------------------------------------------------------
    // Business rule figures. These are DISPLAY ONLY - every rule is enforced by
    // the Web API. The app uses them purely to word its own help text, and it
    // always honours the canModify / canCancel booleans the server sends.
    // ---------------------------------------------------------------------
    public static final int RESERVATION_WINDOW_DAYS = 7;
    public static final int MODIFICATION_NOTICE_HOURS = 12;

    /** Default map radius in kilometres when searching for nearby nodes. */
    public static final double DEFAULT_RADIUS_KM = 25.0;

    /** Fallback map centre (Malabe, Sri Lanka) when device location is unavailable. */
    public static final double FALLBACK_LAT = 6.9061;
    public static final double FALLBACK_LNG = 79.9696;

    /** Request codes for runtime permission prompts. */
    public static final int REQ_LOCATION_PERMISSION = 1001;
    public static final int REQ_CAMERA_PERMISSION = 1002;
}
