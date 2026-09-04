/* =============================================================================
 * File        : EnergyBookingSlot.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Models  (MongoDB collection: "EnergyBookingSlots")
 * Description : One bookable energy window at a microgrid node. A slot pins a
 *               battery bay to a time range; a prosumer reserves a slot in order
 *               to charge from, or drop energy off into, the node. StartUtc and
 *               EndUtc are the absolute instants every business rule is measured
 *               against - the "HH:mm" strings exist only for display.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using SmartSolar.Api.Models.Enums;

namespace SmartSolar.Api.Models;

/// <summary>Persisted energy booking slot document.</summary>
public class EnergyBookingSlot
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>Reference to SolarStationInfo._id.</summary>
    [BsonElement("stationId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string StationId { get; set; } = string.Empty;

    /// <summary>Denormalised station code so slot lists need no second query.</summary>
    [BsonElement("stationCode")]
    public string StationCode { get; set; } = string.Empty;

    [BsonElement("stationName")]
    public string StationName { get; set; } = string.Empty;

    /// <summary>Short label, unique within a station and a date, e.g. "S1".</summary>
    [BsonElement("slotCode")]
    public string SlotCode { get; set; } = string.Empty;

    /// <summary>UTC midnight of the calendar day this slot belongs to.</summary>
    [BsonElement("slotDate")]
    public DateTime SlotDate { get; set; }

    [BsonElement("startTime")]
    public string StartTime { get; set; } = "08:00";

    [BsonElement("endTime")]
    public string EndTime { get; set; } = "10:00";

    /// <summary>Absolute start instant - all rule arithmetic uses this field.</summary>
    [BsonElement("startUtc")]
    public DateTime StartUtc { get; set; }

    [BsonElement("endUtc")]
    public DateTime EndUtc { get; set; }

    [BsonElement("slotType")]
    [BsonRepresentation(BsonType.String)]
    public SlotType SlotType { get; set; } = SlotType.Charging;

    /// <summary>Energy that may be traded during this window, in kWh.</summary>
    [BsonElement("energyCapacityKwh")]
    public double EnergyCapacityKwh { get; set; }

    /// <summary>Which physical battery bay at the node this slot occupies.</summary>
    [BsonElement("batterySlotNumber")]
    public int BatterySlotNumber { get; set; }

    /// <summary>How many reservations this slot accepts (normally one).</summary>
    [BsonElement("maxBookings")]
    public int MaxBookings { get; set; } = 1;

    /// <summary>Maintained by ReservationService as bookings are made / cancelled.</summary>
    [BsonElement("bookedCount")]
    public int BookedCount { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public SlotStatus Status { get; set; } = SlotStatus.Available;

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
