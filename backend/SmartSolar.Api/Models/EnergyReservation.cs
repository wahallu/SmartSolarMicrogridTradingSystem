/* =============================================================================
 * File        : EnergyReservation.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Models  (MongoDB collection: "EnergyReservation")
 * Description : A power trading reservation placed by a solar prosumer against a
 *               booking slot. This document is the subject of the two headline
 *               business rules - it must start within 7 days, and it may only be
 *               updated or cancelled while at least 12 hours of notice remain.
 *               Once approved it carries the QR token a grid operator scans in
 *               the field to finalise the energy transfer.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using SmartSolar.Api.Models.Enums;

namespace SmartSolar.Api.Models;

/// <summary>Persisted energy reservation document.</summary>
public class EnergyReservation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>Human readable unique reference, e.g. "RSV-20260904-0001".</summary>
    [BsonElement("reservationNo")]
    public string ReservationNo { get; set; } = string.Empty;

    /// <summary>Owning prosumer - references Users.nic (the prosumer primary key).</summary>
    [BsonElement("prosumerNic")]
    public string ProsumerNic { get; set; } = string.Empty;

    [BsonElement("prosumerName")]
    public string ProsumerName { get; set; } = string.Empty;

    [BsonElement("prosumerPhone")]
    public string ProsumerPhone { get; set; } = string.Empty;

    [BsonElement("stationId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string StationId { get; set; } = string.Empty;

    [BsonElement("stationCode")]
    public string StationCode { get; set; } = string.Empty;

    [BsonElement("stationName")]
    public string StationName { get; set; } = string.Empty;

    [BsonElement("slotId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string SlotId { get; set; } = string.Empty;

    [BsonElement("slotCode")]
    public string SlotCode { get; set; } = string.Empty;

    /// <summary>Copied from the slot - the instant the 7 day and 12 hour rules use.</summary>
    [BsonElement("reservationStartUtc")]
    public DateTime ReservationStartUtc { get; set; }

    [BsonElement("reservationEndUtc")]
    public DateTime ReservationEndUtc { get; set; }

    [BsonElement("startTime")]
    public string StartTime { get; set; } = string.Empty;

    [BsonElement("endTime")]
    public string EndTime { get; set; } = string.Empty;

    [BsonElement("slotType")]
    [BsonRepresentation(BsonType.String)]
    public SlotType SlotType { get; set; } = SlotType.Charging;

    /// <summary>Energy the prosumer asked to trade, in kWh.</summary>
    [BsonElement("energyKwh")]
    public double EnergyKwh { get; set; }

    /// <summary>Energy actually transferred, captured by the operator at completion.</summary>
    [BsonElement("deliveredKwh")]
    [BsonIgnoreIfNull]
    public double? DeliveredKwh { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

    /// <summary>Opaque 32 byte URL safe token issued when the booking is approved.</summary>
    [BsonElement("qrToken")]
    [BsonIgnoreIfNull]
    public string? QrToken { get; set; }

    /// <summary>Exact JSON string encoded inside the QR image shown on the phone.</summary>
    [BsonElement("qrPayload")]
    [BsonIgnoreIfNull]
    public string? QrPayload { get; set; }

    [BsonElement("qrIssuedAt")]
    [BsonIgnoreIfNull]
    public DateTime? QrIssuedAt { get; set; }

    [BsonElement("approvedBy")]
    [BsonIgnoreIfNull]
    public string? ApprovedBy { get; set; }

    [BsonElement("approvedAt")]
    [BsonIgnoreIfNull]
    public DateTime? ApprovedAt { get; set; }

    [BsonElement("completedBy")]
    [BsonIgnoreIfNull]
    public string? CompletedBy { get; set; }

    [BsonElement("completedAt")]
    [BsonIgnoreIfNull]
    public DateTime? CompletedAt { get; set; }

    [BsonElement("cancelledBy")]
    [BsonIgnoreIfNull]
    public string? CancelledBy { get; set; }

    [BsonElement("cancelledAt")]
    [BsonIgnoreIfNull]
    public DateTime? CancelledAt { get; set; }

    [BsonElement("cancelReason")]
    [BsonIgnoreIfNull]
    public string? CancelReason { get; set; }

    [BsonElement("rejectedReason")]
    [BsonIgnoreIfNull]
    public string? RejectedReason { get; set; }

    [BsonElement("operatorNotes")]
    [BsonIgnoreIfNull]
    public string? OperatorNotes { get; set; }

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
