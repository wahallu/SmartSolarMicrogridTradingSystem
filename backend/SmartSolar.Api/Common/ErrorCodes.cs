/* =============================================================================
 * File        : ErrorCodes.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Common
 * Description : Stable string constants returned in ApiResponse.ErrorCode. The
 *               clients branch on these codes rather than on English message
 *               text, so wording can change without breaking the web or the
 *               Android application. Each code maps to a rule in the contract.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

namespace SmartSolar.Api.Common;

/// <summary>Machine readable error identifiers shared with both clients.</summary>
public static class ErrorCodes
{
    // ---- generic ------------------------------------------------------------
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string Forbidden = "FORBIDDEN";
    public const string InternalError = "INTERNAL_ERROR";

    // ---- authentication and accounts ---------------------------------------
    public const string InvalidCredentials = "INVALID_CREDENTIALS";

    /// <summary>R5 - account is Deactivated; only a Backoffice officer may restore it.</summary>
    public const string AccountDeactivated = "ACCOUNT_DEACTIVATED";

    /// <summary>R6 - self registered prosumer still awaiting back office activation.</summary>
    public const string AccountPending = "ACCOUNT_PENDING";

    /// <summary>R7 - the National Identity Card number is already registered.</summary>
    public const string NicAlreadyExists = "NIC_ALREADY_EXISTS";

    public const string UsernameAlreadyExists = "USERNAME_ALREADY_EXISTS";
    public const string EmailAlreadyExists = "EMAIL_ALREADY_EXISTS";
    public const string CannotDeleteSelf = "CANNOT_DELETE_SELF";
    public const string WrongCurrentPassword = "WRONG_CURRENT_PASSWORD";

    // ---- stations -----------------------------------------------------------
    public const string StationCodeExists = "STATION_CODE_EXISTS";

    /// <summary>R4 - deactivation refused because future reservations still exist.</summary>
    public const string StationHasActiveReservations = "STATION_HAS_ACTIVE_RES";

    public const string StationInactive = "STATION_INACTIVE";

    /// <summary>R12 - the node is closed on the requested day of the week.</summary>
    public const string StationClosed = "STATION_CLOSED";

    // ---- slots --------------------------------------------------------------
    /// <summary>R8 - slot is full, blocked, in the past, or its station is inactive.</summary>
    public const string SlotUnavailable = "SLOT_UNAVAILABLE";

    public const string SlotHasBookings = "SLOT_HAS_BOOKINGS";
    public const string SlotCodeExists = "SLOT_CODE_EXISTS";

    // ---- reservations -------------------------------------------------------
    /// <summary>R1 - start instant must be in the future and within 7 days.</summary>
    public const string ReservationWindow = "RES_WINDOW_7_DAYS";

    /// <summary>R2 - updates need at least 12 hours of notice.</summary>
    public const string ReservationUpdateNotice = "RES_UPDATE_12H";

    /// <summary>R3 - cancellations need at least 12 hours of notice.</summary>
    public const string ReservationCancelNotice = "RES_CANCEL_12H";

    public const string ReservationBadState = "RES_BAD_STATE";

    /// <summary>R13 - the prosumer already holds an active booking on this slot.</summary>
    public const string DuplicateReservation = "DUPLICATE_RESERVATION";

    // ---- QR verification (R9) ----------------------------------------------
    public const string QrInvalid = "QR_INVALID";
    public const string QrNotApproved = "QR_NOT_APPROVED";
    public const string QrOutOfWindow = "QR_OUT_OF_WINDOW";
}
