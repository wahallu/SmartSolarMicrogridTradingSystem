/* =============================================================================
 * File        : SolarStationInfo.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Models  (MongoDB collection: "SolarStationInfo")
 * Description : A microgrid node (solar grid hub). Carries the GPS position used
 *               by the Google Maps "nearby stations" feature on Android, the
 *               generation capacity in kWh, the number of battery storage bays
 *               and the weekly operating schedule maintained by the back office.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using SmartSolar.Api.Models.Enums;

namespace SmartSolar.Api.Models;

/// <summary>Latitude / longitude pair for a microgrid node.</summary>
public class GeoLocation
{
    [BsonElement("latitude")]
    public double Latitude { get; set; }

    [BsonElement("longitude")]
    public double Longitude { get; set; }
}

/// <summary>Plain opening and closing wall clock times, "HH:mm".</summary>
public class OperatingHours
{
    [BsonElement("openTime")]
    public string OpenTime { get; set; } = "06:00";

    [BsonElement("closeTime")]
    public string CloseTime { get; set; } = "22:00";
}

/// <summary>One row of the seven day operating schedule of a node.</summary>
public class DaySchedule
{
    /// <summary>0 = Sunday .. 6 = Saturday, matching System.DayOfWeek.</summary>
    [BsonElement("dayOfWeek")]
    public int DayOfWeek { get; set; }

    [BsonElement("isOpen")]
    public bool IsOpen { get; set; } = true;

    [BsonElement("openTime")]
    public string OpenTime { get; set; } = "06:00";

    [BsonElement("closeTime")]
    public string CloseTime { get; set; } = "22:00";
}

/// <summary>Persisted microgrid node document.</summary>
public class SolarStationInfo
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>Human readable unique code, e.g. "STN-001".</summary>
    [BsonElement("stationCode")]
    public string StationCode { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("type")]
    [BsonRepresentation(BsonType.String)]
    public StationType Type { get; set; } = StationType.Solar;

    [BsonElement("address")]
    public string Address { get; set; } = string.Empty;

    [BsonElement("city")]
    public string City { get; set; } = string.Empty;

    /// <summary>GPS position - drives the map screens on both clients.</summary>
    [BsonElement("location")]
    public GeoLocation Location { get; set; } = new();

    /// <summary>Rated capacity of the node in kilowatt hours.</summary>
    [BsonElement("capacityKwh")]
    public double CapacityKwh { get; set; }

    /// <summary>Total number of battery storage bays physically installed.</summary>
    [BsonElement("totalBatterySlots")]
    public int TotalBatterySlots { get; set; }

    /// <summary>Bays currently free - maintained by the grid operator.</summary>
    [BsonElement("availableBatterySlots")]
    public int AvailableBatterySlots { get; set; }

    [BsonElement("operatingHours")]
    public OperatingHours OperatingHours { get; set; } = new();

    /// <summary>Seven entries, one per day of the week.</summary>
    [BsonElement("weeklySchedule")]
    public List<DaySchedule> WeeklySchedule { get; set; } = new();

    [BsonElement("contactNumber")]
    public string ContactNumber { get; set; } = string.Empty;

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public StationStatus Status { get; set; } = StationStatus.Active;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("createdBy")]
    [BsonIgnoreIfNull]
    public string? CreatedBy { get; set; }

    [BsonElement("updatedBy")]
    [BsonIgnoreIfNull]
    public string? UpdatedBy { get; set; }
}
