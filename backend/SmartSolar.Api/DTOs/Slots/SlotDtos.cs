/* =============================================================================
 * File        : SlotDtos.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : DTOs / Slots
 * Description : Contains the API models used to view and maintain booking
 *               slots.
 * Author      : IT22207272 - Nisal
 * Created     : 2026-09-11
 * =============================================================================
 */

using System.ComponentModel.DataAnnotations;
using SmartSolar.Api.Common;
using SmartSolar.Api.Models.Enums;

namespace SmartSolar.Api.DTOs.Slots;

/// <summary>Booking slot as returned to the clients.</summary>
public class SlotDto
{
    public string Id { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public string SlotCode { get; set; } = string.Empty;
    public DateTime SlotDate { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string SlotType { get; set; } = string.Empty;
    public double EnergyCapacityKwh { get; set; }
    public int BatterySlotNumber { get; set; }
    public int MaxBookings { get; set; }
    public int BookedCount { get; set; }

    /// <summary>MaxBookings minus BookedCount, never negative.</summary>
    public int RemainingCapacity { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>True when the API would accept a booking for this slot right now (rule R8).</summary>
    public bool IsBookable { get; set; }
}

/// <summary>Creates a single booking slot.</summary>
public class CreateSlotRequest
{
    [Required(ErrorMessage = "Station is required.")]
    public string StationId { get; set; } = string.Empty;

    /// <summary>Optional - the service allocates the next free code when omitted.</summary>
    public string? SlotCode { get; set; }

    [Required(ErrorMessage = "Slot date is required.")]
    public DateTime SlotDate { get; set; }

    [Required(ErrorMessage = "Start time is required.")]
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Start time must use HH:mm.")]
    public string StartTime { get; set; } = "08:00";

    [Required(ErrorMessage = "End time is required.")]
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "End time must use HH:mm.")]
    public string EndTime { get; set; } = "10:00";

    public SlotType SlotType { get; set; } = SlotType.Charging;

    [Range(0.1, 10000, ErrorMessage = "Energy capacity must be greater than zero.")]
    public double EnergyCapacityKwh { get; set; } = 20;

    [Range(1, 500, ErrorMessage = "Battery slot number must be at least 1.")]
    public int BatterySlotNumber { get; set; } = 1;

    [Range(1, 20, ErrorMessage = "A slot must accept between 1 and 20 bookings.")]
    public int MaxBookings { get; set; } = 1;
}

/// <summary>Edits an existing booking slot.</summary>
public class UpdateSlotRequest
{
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Start time must use HH:mm.")]
    public string StartTime { get; set; } = "08:00";

    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "End time must use HH:mm.")]
    public string EndTime { get; set; } = "10:00";

    public SlotType SlotType { get; set; } = SlotType.Charging;

    [Range(0.1, 10000)]
    public double EnergyCapacityKwh { get; set; } = 20;

    [Range(1, 500)]
    public int BatterySlotNumber { get; set; } = 1;

    [Range(1, 20)]
    public int MaxBookings { get; set; } = 1;

    public SlotStatus? Status { get; set; }
}

/// <summary>Lays out a whole date range of slots for one node in a single call.</summary>
public class GenerateSlotsRequest
{
    [Required(ErrorMessage = "Station is required.")]
    public string StationId { get; set; } = string.Empty;

    [Required(ErrorMessage = "From date is required.")]
    public DateTime FromDate { get; set; }

    [Required(ErrorMessage = "To date is required.")]
    public DateTime ToDate { get; set; }

    [Range(0, 23, ErrorMessage = "Start hour must be between 0 and 23.")]
    public int StartHour { get; set; } = 6;

    [Range(1, 24, ErrorMessage = "End hour must be between 1 and 24.")]
    public int EndHour { get; set; } = 20;

    [Range(1, 12, ErrorMessage = "Slot duration must be between 1 and 12 hours.")]
    public int SlotDurationHours { get; set; } = 2;

    public SlotType SlotType { get; set; } = SlotType.Charging;

    [Range(0.1, 10000)]
    public double EnergyCapacityKwh { get; set; } = 20;

    /// <summary>How many battery bays to publish per time window.</summary>
    [Range(1, 50, ErrorMessage = "Between 1 and 50 battery slots may be generated per window.")]
    public int BatterySlots { get; set; } = 2;

    /// <summary>Skip days the node is closed according to its weekly schedule.</summary>
    public bool SkipClosedDays { get; set; } = true;
}

/// <summary>Outcome summary of a bulk slot generation.</summary>
public class GenerateSlotsResultDto
{
    public int Created { get; set; }
    public int Skipped { get; set; }
    public List<string> Notes { get; set; } = new();
}

/// <summary>Filter options for the slot list.</summary>
public class SlotQuery : PagingQuery
{
    public string? StationId { get; set; }

    /// <summary>Calendar day filter; matched against SlotDate.</summary>
    public DateTime? Date { get; set; }

    public SlotStatus? Status { get; set; }
    public SlotType? SlotType { get; set; }

    /// <summary>When true, past slots are excluded from the result.</summary>
    public bool UpcomingOnly { get; set; }
}
