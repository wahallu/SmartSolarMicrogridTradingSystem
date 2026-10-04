/* =============================================================================
 * File        : StationDtos.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : DTOs / Stations
 * Description : Contains the API models used for station details and
 *               schedules.
 * Author      : IT22207272 - Nisal
 * Created     : 2026-09-11
 * =============================================================================
 */

using System.ComponentModel.DataAnnotations;
using SmartSolar.Api.Common;
using SmartSolar.Api.Models.Enums;

namespace SmartSolar.Api.DTOs.Stations;

/// <summary>One day of the published operating schedule.</summary>
public class DayScheduleDto
{
    [Range(0, 6, ErrorMessage = "Day of week must be between 0 (Sunday) and 6 (Saturday).")]
    public int DayOfWeek { get; set; }

    /// <summary>Read only convenience for the clients, e.g. "Monday".</summary>
    public string DayName { get; set; } = string.Empty;

    public bool IsOpen { get; set; } = true;

    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Time must use HH:mm.")]
    public string OpenTime { get; set; } = "06:00";

    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Time must use HH:mm.")]
    public string CloseTime { get; set; } = "22:00";
}

/// <summary>Default opening and closing times of a node.</summary>
public class OperatingHoursDto
{
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Time must use HH:mm.")]
    public string OpenTime { get; set; } = "06:00";

    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Time must use HH:mm.")]
    public string CloseTime { get; set; } = "22:00";
}

/// <summary>Microgrid node as returned to the clients.</summary>
public class StationDto
{
    public string Id { get; set; } = string.Empty;
    public string StationCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double CapacityKwh { get; set; }
    public int TotalBatterySlots { get; set; }
    public int AvailableBatterySlots { get; set; }
    public OperatingHoursDto OperatingHours { get; set; } = new();
    public List<DayScheduleDto> WeeklySchedule { get; set; } = new();
    public string ContactNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary>Future Pending/Approved reservations - drives the R4 deactivation guard.</summary>
    public long ActiveReservationCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Station projection returned by the map endpoint, ordered by distance.</summary>
public class NearbyStationDto : StationDto
{
    /// <summary>Great circle distance from the caller in kilometres.</summary>
    public double DistanceKm { get; set; }
}

/// <summary>Payload that registers a new microgrid node.</summary>
public class CreateStationRequest
{
    [Required(ErrorMessage = "Station code is required.")]
    [RegularExpression(@"^[A-Za-z0-9\-]{3,20}$",
        ErrorMessage = "Station code may contain letters, digits and hyphens only.")]
    public string StationCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Station name is required.")]
    [StringLength(120, MinimumLength = 3)]
    public string Name { get; set; } = string.Empty;

    public StationType Type { get; set; } = StationType.Solar;

    [Required(ErrorMessage = "Address is required.")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "City is required.")]
    public string City { get; set; } = string.Empty;

    [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90.")]
    public double Latitude { get; set; }

    [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180.")]
    public double Longitude { get; set; }

    [Range(0.1, 100000, ErrorMessage = "Capacity must be greater than zero.")]
    public double CapacityKwh { get; set; }

    [Range(1, 500, ErrorMessage = "A node must have between 1 and 500 battery slots.")]
    public int TotalBatterySlots { get; set; }

    [Range(0, 500, ErrorMessage = "Available slots cannot be negative.")]
    public int AvailableBatterySlots { get; set; }

    public string ContactNumber { get; set; } = string.Empty;

    public OperatingHoursDto OperatingHours { get; set; } = new();

    /// <summary>Optional - a sensible seven day default is generated when omitted.</summary>
    public List<DayScheduleDto>? WeeklySchedule { get; set; }
}

/// <summary>Payload that edits an existing microgrid node.</summary>
public class UpdateStationRequest
{
    [Required(ErrorMessage = "Station name is required.")]
    public string Name { get; set; } = string.Empty;

    public StationType Type { get; set; } = StationType.Solar;

    [Required(ErrorMessage = "Address is required.")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "City is required.")]
    public string City { get; set; } = string.Empty;

    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Range(-180, 180)]
    public double Longitude { get; set; }

    [Range(0.1, 100000)]
    public double CapacityKwh { get; set; }

    [Range(1, 500)]
    public int TotalBatterySlots { get; set; }

    [Range(0, 500)]
    public int AvailableBatterySlots { get; set; }

    public string ContactNumber { get; set; } = string.Empty;

    public OperatingHoursDto OperatingHours { get; set; } = new();
}

/// <summary>Schedule maintenance payload used by back office and operators.</summary>
public class UpdateScheduleRequest
{
    public OperatingHoursDto OperatingHours { get; set; } = new();

    [Required(ErrorMessage = "A weekly schedule is required.")]
    [MinLength(7, ErrorMessage = "The weekly schedule must contain all seven days.")]
    [MaxLength(7, ErrorMessage = "The weekly schedule must contain all seven days.")]
    public List<DayScheduleDto> WeeklySchedule { get; set; } = new();
}

/// <summary>Battery bay availability update raised by a grid operator.</summary>
public class UpdateAvailabilityRequest
{
    [Range(0, 500, ErrorMessage = "Available slots cannot be negative.")]
    public int AvailableBatterySlots { get; set; }
}

/// <summary>Filter and paging options for the station list.</summary>
public class StationQuery : PagingQuery
{
    public StationStatus? Status { get; set; }
    public string? Search { get; set; }
    public string? City { get; set; }
}

/// <summary>Query string of the map endpoint GET /stations/nearby.</summary>
public class NearbyStationQuery
{
    [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90.")]
    public double Lat { get; set; }

    [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180.")]
    public double Lng { get; set; }

    /// <summary>Search radius in kilometres; the configured default applies when omitted.</summary>
    [Range(0.1, 1000, ErrorMessage = "Radius must be between 0.1 and 1000 km.")]
    public double? RadiusKm { get; set; }

    [Range(1, 200)]
    public int Limit { get; set; } = 50;
}
