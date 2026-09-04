/* =============================================================================
 * File        : ReservationDtos.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : DTOs / Reservations
 * Description : Contracts for the reservation workflow: create, update, cancel,
 *               approve, QR issue, QR verification and completion, plus the
 *               dashboard counters read live by both clients.
 *
 *               Note CanModify / CanCancel on ReservationDto. The 12 hour rule
 *               is evaluated on the server and the answer is shipped to the
 *               clients as a boolean, so no client ever re-implements the rule -
 *               it only enables or disables a button.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using System.ComponentModel.DataAnnotations;
using SmartSolar.Api.Common;
using SmartSolar.Api.Models.Enums;

namespace SmartSolar.Api.DTOs.Reservations;

/// <summary>Reservation as returned to the clients.</summary>
public class ReservationDto
{
    public string Id { get; set; } = string.Empty;
    public string ReservationNo { get; set; } = string.Empty;
    public string ProsumerNic { get; set; } = string.Empty;
    public string ProsumerName { get; set; } = string.Empty;
    public string ProsumerPhone { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public string SlotId { get; set; } = string.Empty;
    public string SlotCode { get; set; } = string.Empty;
    public DateTime ReservationStartUtc { get; set; }
    public DateTime ReservationEndUtc { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string SlotType { get; set; } = string.Empty;
    public double EnergyKwh { get; set; }
    public double? DeliveredKwh { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? QrToken { get; set; }
    public DateTime? QrIssuedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public string? RejectedReason { get; set; }
    public string? OperatorNotes { get; set; }

    /// <summary>Server verdict on rule R2 - may this booking still be changed?</summary>
    public bool CanModify { get; set; }

    /// <summary>Server verdict on rule R3 - may this booking still be cancelled?</summary>
    public bool CanCancel { get; set; }

    /// <summary>Hours remaining until the slot starts; negative once it has passed.</summary>
    public double HoursUntilStart { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Places a new reservation against a published slot.</summary>
public class CreateReservationRequest
{
    /// <summary>Ignored when the caller is a prosumer - the token NIC always wins (rule R11).</summary>
    public string? ProsumerNic { get; set; }

    [Required(ErrorMessage = "Station is required.")]
    public string StationId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Slot is required.")]
    public string SlotId { get; set; } = string.Empty;

    [Range(0.1, 10000, ErrorMessage = "Energy must be greater than zero.")]
    public double EnergyKwh { get; set; }

    [StringLength(250)]
    public string? Notes { get; set; }
}

/// <summary>Moves a reservation to another slot and/or changes the energy amount.</summary>
public class UpdateReservationRequest
{
    /// <summary>Optional - omit to keep the current slot.</summary>
    public string? SlotId { get; set; }

    [Range(0.1, 10000, ErrorMessage = "Energy must be greater than zero.")]
    public double? EnergyKwh { get; set; }
}

/// <summary>Reason attached to a cancellation.</summary>
public class CancelReservationRequest
{
    [StringLength(250)]
    public string? Reason { get; set; }
}

/// <summary>Reason attached to a rejection by staff.</summary>
public class RejectReservationRequest
{
    [Required(ErrorMessage = "A reason is required when rejecting a reservation.")]
    [StringLength(250)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>QR material handed to the prosumer's phone after approval.</summary>
public class QrCodeDto
{
    public string ReservationNo { get; set; } = string.Empty;
    public string QrToken { get; set; } = string.Empty;

    /// <summary>Exact string encoded in the image, so a client may re-render it locally.</summary>
    public string QrPayload { get; set; } = string.Empty;

    /// <summary>Base64 PNG without a data URI prefix.</summary>
    public string QrImageBase64 { get; set; } = string.Empty;

    public DateTime? IssuedAt { get; set; }
}

/// <summary>Token read out of a scanned QR image.</summary>
public class VerifyQrRequest
{
    [Required(ErrorMessage = "A QR token is required.")]
    public string QrToken { get; set; } = string.Empty;
}

/// <summary>Finalises the energy transfer after a successful scan.</summary>
public class CompleteReservationRequest
{
    [Required(ErrorMessage = "A QR token is required.")]
    public string QrToken { get; set; } = string.Empty;

    [Range(0, 10000, ErrorMessage = "Delivered energy cannot be negative.")]
    public double DeliveredKwh { get; set; }

    [StringLength(250)]
    public string? Notes { get; set; }
}

/// <summary>Counters and upcoming bookings shown on every dashboard.</summary>
public class DashboardDto
{
    /// <summary>Bookings awaiting staff approval.</summary>
    public long PendingReservations { get; set; }

    /// <summary>Approved bookings whose slot has not started yet.</summary>
    public long ApprovedFutureReservations { get; set; }

    public long TodayReservations { get; set; }
    public long CompletedReservations { get; set; }
    public long CancelledReservations { get; set; }
    public long TotalReservations { get; set; }
    public long ActiveStations { get; set; }
    public long TotalStations { get; set; }

    /// <summary>Prosumers awaiting activation - staff only, zero for a prosumer caller.</summary>
    public long PendingProsumerActivations { get; set; }

    public long TotalProsumers { get; set; }

    /// <summary>Sum of delivered kWh across completed bookings in scope.</summary>
    public double TotalEnergyKwh { get; set; }

    /// <summary>The next few bookings, newest slot first.</summary>
    public List<ReservationDto> Upcoming { get; set; } = new();
}

/// <summary>Filter options for the reservation list - the search screen on both clients.</summary>
public class ReservationQuery : PagingQuery
{
    public string? Nic { get; set; }
    public string? StationId { get; set; }
    public ReservationStatus? Status { get; set; }

    /// <summary>Inclusive lower bound applied to ReservationStartUtc.</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>Inclusive upper bound applied to ReservationStartUtc.</summary>
    public DateTime? ToDate { get; set; }

    /// <summary>Free text over reservation number, prosumer name, NIC and station.</summary>
    public string? Search { get; set; }
}

/// <summary>Which slice of their own bookings a prosumer wants.</summary>
public enum ReservationScope
{
    /// <summary>Approved bookings that have not started yet.</summary>
    Current = 0,

    /// <summary>Bookings waiting for approval.</summary>
    Pending = 1,

    /// <summary>Completed, cancelled, rejected or past bookings.</summary>
    History = 2,

    /// <summary>Everything the prosumer has ever booked.</summary>
    All = 3
}
