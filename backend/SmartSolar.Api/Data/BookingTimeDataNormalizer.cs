/* =============================================================================
 * File        : BookingTimeDataNormalizer.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Data / Compatibility
 * Description : Repairs booking instants created with the legacy UTC-label bug.
 * Author      : IT22207272 - Nisal
 * Created     : 2026-10-17
 * =============================================================================
 */

using MongoDB.Driver;
using SmartSolar.Api.Common;
using SmartSolar.Api.Models;

namespace SmartSolar.Api.Data;

/// <summary>
/// One-time, versioned compatibility repair for records created before booking
/// wall-clock values were correctly converted from Asia/Colombo to UTC.
/// </summary>
public sealed class BookingTimeDataNormalizer
{
    private readonly MongoDbContext _db;
    private readonly ILogger<BookingTimeDataNormalizer> _logger;

    public BookingTimeDataNormalizer(
        MongoDbContext db,
        ILogger<BookingTimeDataNormalizer> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Repairs every unversioned slot, then copies the corrected instants to its
    /// unversioned reservations. Versioned records are never rewritten.
    /// </summary>
    public async Task NormalizeAsync(CancellationToken ct = default)
    {
        var slots = await _db.Slots
            .Find(Builders<EnergyBookingSlot>.Filter.Empty)
            .ToListAsync(ct);

        var slotTimes = new Dictionary<string, (DateTime StartUtc, DateTime EndUtc)>(
            StringComparer.Ordinal);
        var slotWrites = new List<WriteModel<EnergyBookingSlot>>();

        foreach (var slot in slots)
        {
            try
            {
                var startUtc = BookingTime.ToUtc(slot.SlotDate, slot.StartTime);
                var endUtc = BookingTime.ToUtc(slot.SlotDate, slot.EndTime);
                slotTimes[slot.Id] = (startUtc, endUtc);

                if (slot.TimeSemanticsVersion >= BookingTime.CurrentSemanticsVersion)
                {
                    continue;
                }

                var update = Builders<EnergyBookingSlot>.Update
                    .Set(s => s.StartUtc, startUtc)
                    .Set(s => s.EndUtc, endUtc)
                    .Set(s => s.TimeSemanticsVersion, BookingTime.CurrentSemanticsVersion);

                slotWrites.Add(new UpdateOneModel<EnergyBookingSlot>(
                    Builders<EnergyBookingSlot>.Filter.Eq(s => s.Id, slot.Id), update));
            }
            catch (FormatException)
            {
                _logger.LogWarning(
                    "Could not normalise slot {SlotId}: '{StartTime}-{EndTime}' is not a valid HH:mm window.",
                    slot.Id, slot.StartTime, slot.EndTime);
            }
        }

        if (slotWrites.Count > 0)
        {
            await _db.Slots.BulkWriteAsync(slotWrites, cancellationToken: ct);
        }

        var reservationBuilder = Builders<EnergyReservation>.Filter;
        var legacyReservations = reservationBuilder.Or(
            reservationBuilder.Exists(r => r.TimeSemanticsVersion, false),
            reservationBuilder.Lt(
                r => r.TimeSemanticsVersion, BookingTime.CurrentSemanticsVersion));

        var reservations = await _db.Reservations.Find(legacyReservations).ToListAsync(ct);
        var reservationWrites = new List<WriteModel<EnergyReservation>>();

        foreach (var reservation in reservations)
        {
            if (!slotTimes.TryGetValue(reservation.SlotId, out var corrected))
            {
                _logger.LogWarning(
                    "Could not normalise reservation {ReservationNo}: slot {SlotId} was not found.",
                    reservation.ReservationNo, reservation.SlotId);
                continue;
            }

            var update = Builders<EnergyReservation>.Update
                .Set(r => r.ReservationStartUtc, corrected.StartUtc)
                .Set(r => r.ReservationEndUtc, corrected.EndUtc)
                .Set(r => r.TimeSemanticsVersion, BookingTime.CurrentSemanticsVersion)

                // The payload contains a copy of the start instant. Removing the cached
                // value makes GetQrCode rebuild it from the corrected reservation. Old
                // printed codes remain valid because verification trusts only the token.
                .Unset(r => r.QrPayload);

            reservationWrites.Add(new UpdateOneModel<EnergyReservation>(
                Builders<EnergyReservation>.Filter.Eq(r => r.Id, reservation.Id), update));
        }

        if (reservationWrites.Count > 0)
        {
            await _db.Reservations.BulkWriteAsync(reservationWrites, cancellationToken: ct);
        }

        if (slotWrites.Count > 0 || reservationWrites.Count > 0)
        {
            _logger.LogInformation(
                "Normalised Asia/Colombo booking times for {SlotCount} slot(s) and {ReservationCount} reservation(s).",
                slotWrites.Count, reservationWrites.Count);
        }
    }
}
