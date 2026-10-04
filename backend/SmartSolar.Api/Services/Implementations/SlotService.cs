/* =============================================================================
 * File        : SlotService.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Services / Implementations
 * Description : Implements slot searches, generation and maintenance rules.
 * Author      : IT22207272 - Nisal
 * Created     : 2026-09-12
 * =============================================================================
 */

using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartSolar.Api.Common;
using SmartSolar.Api.Data;
using SmartSolar.Api.DTOs.Slots;
using SmartSolar.Api.Models;
using SmartSolar.Api.Models.Enums;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Services.Implementations;

/// <summary>
/// MongoDB backed implementation of <see cref="ISlotService"/> - the single
/// authority on the bookable energy windows published by a microgrid node.
/// </summary>
public class SlotService : ISlotService
{
    /// <summary>
    /// Hard ceiling on how many slots one bulk generation may insert. It protects
    /// the server from an accidental multi-year request such as a two year date
    /// range crossed with hourly windows and fifty battery bays, which would ask
    /// MongoDB to accept hundreds of thousands of documents in a single call.
    /// </summary>
    private const int MaxGeneratedSlots = 2000;

    /// <summary>Longest date range a single bulk generation may cover, in days.</summary>
    private const int MaxGenerationSpanDays = 60;

    private readonly MongoDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<SlotService> _logger;

    /// <summary>Captures the data context, the injected clock and the logger.</summary>
    /// <param name="db">Access to the four MongoDB collections.</param>
    /// <param name="clock">Injected clock - every rule reads "now" from here so it stays testable.</param>
    /// <param name="logger">Diagnostics sink for the slot life cycle operations.</param>
    public SlotService(MongoDbContext db, IClock clock, ILogger<SlotService> logger)
    {
        // Dependencies are stored once and never resolved from a service locator, so the
        // rule code below reads as plain business language rather than plumbing.
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    // =========================================================================
    // Queries
    // =========================================================================

    /// <summary>Paged, filtered list of booking slots ordered by their absolute start instant.</summary>
    /// <param name="query">Optional node, date, status, type and "upcoming only" filters plus paging.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>One page of <see cref="SlotDto"/>, each carrying the server verdict on rule R8.</returns>
    public async Task<ServiceResult<PagedResult<SlotDto>>> GetSlotsAsync(
        SlotQuery query, CancellationToken ct = default)
    {
        // The filter is assembled with the driver's FilterDefinitionBuilder so the query stays index friendly.
        try
        {
            var builder = Builders<EnergyBookingSlot>.Filter;
            var filter = builder.Empty;

            if (!string.IsNullOrWhiteSpace(query.StationId))
            {
                // A malformed node id can never match a stored ObjectId, so an impossible filter is
                // used rather than handing the bad value to the driver, which would throw.
                if (!ObjectId.TryParse(query.StationId, out _))
                {
                    var empty = PagedResult<SlotDto>.Create(new List<SlotDto>(), query.Page, query.PageSize, 0);
                    return ServiceResult<PagedResult<SlotDto>>.Ok(empty, "Retrieved 0 of 0 booking slot(s).");
                }

                filter &= builder.Eq(s => s.StationId, query.StationId);
            }

            if (query.Date.HasValue)
            {
                // SlotDate is stored at UTC midnight, so a calendar day is a half open range [day, day + 1).
                var dayStart = DateTime.SpecifyKind(query.Date.Value.Date, DateTimeKind.Utc);
                filter &= builder.Gte(s => s.SlotDate, dayStart)
                          & builder.Lt(s => s.SlotDate, dayStart.AddDays(1));
            }

            if (query.Status.HasValue)
            {
                filter &= builder.Eq(s => s.Status, query.Status.Value);
            }

            if (query.SlotType.HasValue)
            {
                filter &= builder.Eq(s => s.SlotType, query.SlotType.Value);
            }

            if (query.UpcomingOnly)
            {
                // "Now" is read from the injected clock so a frozen clock in a unit test moves this boundary too.
                filter &= builder.Gte(s => s.StartUtc, _clock.UtcNow);
            }

            var totalCount = await _db.Slots.CountDocumentsAsync(filter, cancellationToken: ct);

            var slots = await _db.Slots
                .Find(filter)
                .SortBy(s => s.StartUtc)
                .Skip(query.Skip)
                .Limit(query.PageSize)
                .ToListAsync(ct);

            // One lookup for the whole page instead of one per slot - see LoadStationActivityAsync.
            var activity = await LoadStationActivityAsync(slots.Select(s => s.StationId), ct);

            var now = _clock.UtcNow;
            var items = slots
                .Select(slot => slot.ToDto(now, activity.TryGetValue(slot.StationId, out var active) && active))
                .ToList();

            var page = PagedResult<SlotDto>.Create(items, query.Page, query.PageSize, totalCount);
            return ServiceResult<PagedResult<SlotDto>>.Ok(
                page, $"Retrieved {items.Count} of {totalCount} booking slot(s).");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to read the booking slot list from MongoDB.");
            return ServiceResult<PagedResult<SlotDto>>.Error("Could not read the booking slots. Please try again.");
        }
    }

    /// <summary>Slots at a node that would accept a booking right now - rule R8 in list form.</summary>
    /// <param name="stationId">Mongo identifier of the microgrid node.</param>
    /// <param name="date">Optional calendar day filter; omit for every future day.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Bookable slots ordered by start instant, or the rule violation that stopped the query.</returns>
    public async Task<ServiceResult<List<SlotDto>>> GetAvailableAsync(
        string stationId, DateTime? date, CancellationToken ct = default)
    {
        // A malformed id is answered as NotFound rather than being handed to the driver, which would throw.
        if (!ObjectId.TryParse(stationId, out _))
        {
            return ServiceResult<List<SlotDto>>.NotFound("Microgrid node not found.");
        }

        try
        {
            var station = await _db.Stations
                .Find(Builders<SolarStationInfo>.Filter.Eq(s => s.Id, stationId))
                .FirstOrDefaultAsync(ct);

            if (station is null)
            {
                return ServiceResult<List<SlotDto>>.NotFound("Microgrid node not found.");
            }

            // ***RULE R8*** - the parent node must be in service before any of its slots may be booked.
            if (station.Status != StationStatus.Active)
            {
                return ServiceResult<List<SlotDto>>.Invalid(
                    "This microgrid node is not in service, so none of its slots can be booked.",
                    ErrorCodes.StationInactive);
            }

            var now = _clock.UtcNow;
            var builder = Builders<EnergyBookingSlot>.Filter;

            // BookedCount < MaxBookings compares two fields of the same document, which MongoDB
            // expresses with the aggregation operator $expr rather than a plain field/value match.
            FilterDefinition<EnergyBookingSlot> hasFreeCapacity = new BsonDocument(
                "$expr", new BsonDocument("$lt", new BsonArray { "$bookedCount", "$maxBookings" }));

            // ***RULE R8*** - Available, still has free capacity and has not started yet.
            var filter = builder.Eq(s => s.StationId, stationId)
                         & builder.Eq(s => s.Status, SlotStatus.Available)
                         & hasFreeCapacity
                         & builder.Gt(s => s.StartUtc, now);

            if (date.HasValue)
            {
                var dayStart = DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Utc);
                filter &= builder.Gte(s => s.SlotDate, dayStart)
                          & builder.Lt(s => s.SlotDate, dayStart.AddDays(1));
            }

            var slots = await _db.Slots
                .Find(filter)
                .SortBy(s => s.StartUtc)
                .ToListAsync(ct);

            // The node was already proven Active above, so the flag is passed as true without a second lookup.
            var items = slots.Select(slot => slot.ToDto(now, true)).ToList();

            return ServiceResult<List<SlotDto>>.Ok(items, $"{items.Count} slot(s) available.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to read available slots for node {StationId}.", stationId);
            return ServiceResult<List<SlotDto>>.Error("Could not read the available slots. Please try again.");
        }
    }

    /// <summary>Reads a single booking slot by its MongoDB identifier.</summary>
    /// <param name="id">Mongo ObjectId of the slot, as a string.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The slot, or NotFound when the id is malformed or unknown.</returns>
    public async Task<ServiceResult<SlotDto>> GetByIdAsync(string id, CancellationToken ct = default)
    {
        // A malformed id is answered as NotFound rather than being handed to the driver, which would throw.
        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<SlotDto>.NotFound("Booking slot not found.");
        }

        try
        {
            var slot = await _db.Slots
                .Find(Builders<EnergyBookingSlot>.Filter.Eq(s => s.Id, id))
                .FirstOrDefaultAsync(ct);

            if (slot is null)
            {
                return ServiceResult<SlotDto>.NotFound("Booking slot not found.");
            }

            // IsBookable on the DTO depends on the parent node status, so it is resolved before projecting.
            var activity = await LoadStationActivityAsync(new[] { slot.StationId }, ct);
            var stationActive = activity.TryGetValue(slot.StationId, out var active) && active;

            return ServiceResult<SlotDto>.Ok(
                slot.ToDto(_clock.UtcNow, stationActive), "Booking slot retrieved successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to read booking slot {SlotId}.", id);
            return ServiceResult<SlotDto>.Error("Could not read the booking slot. Please try again.");
        }
    }

    // =========================================================================
    // Commands
    // =========================================================================

    /// <summary>Publishes one new booking slot - Backoffice or Grid Operator only.</summary>
    /// <param name="request">Node, date, wall clock window, energy capacity and battery bay.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Created with the stored slot, or the rule violation that stopped it.</returns>
    public async Task<ServiceResult<SlotDto>> CreateAsync(
        CreateSlotRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // Publishing capacity is an operational task, so both the back office and site operators may do it.
        if (!caller.IsStaff)
        {
            return ServiceResult<SlotDto>.Forbidden(
                "Only a Backoffice officer or a Grid Operator may publish a booking slot.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(request.StationId, out _))
        {
            return ServiceResult<SlotDto>.NotFound("Microgrid node not found.");
        }

        if (!CanManageSlotsAt(caller, request.StationId))
        {
            return ServiceResult<SlotDto>.Forbidden(
                "A Grid Operator may only publish slots at their assigned microgrid node.",
                ErrorCodes.Forbidden);
        }

        try
        {
            var station = await _db.Stations
                .Find(Builders<SolarStationInfo>.Filter.Eq(s => s.Id, request.StationId))
                .FirstOrDefaultAsync(ct);

            if (station is null)
            {
                return ServiceResult<SlotDto>.NotFound("Microgrid node not found.");
            }

            // A node that is out of service must not accumulate new bookable capacity (rule R8).
            if (station.Status != StationStatus.Active)
            {
                return ServiceResult<SlotDto>.Invalid(
                    "This microgrid node is not in service, so no booking slot can be published for it.",
                    ErrorCodes.StationInactive);
            }

            // The wall clock strings are validated before ComposeUtc parses them, so a bad value
            // becomes a 400 with a readable message instead of a FormatException.
            if (!TimeSpan.TryParse(request.StartTime, CultureInfo.InvariantCulture, out var startOfDay) ||
                !TimeSpan.TryParse(request.EndTime, CultureInfo.InvariantCulture, out var endOfDay))
            {
                return ServiceResult<SlotDto>.Invalid("Start time and end time must both use the HH:mm format.");
            }

            if (endOfDay <= startOfDay)
            {
                return ServiceResult<SlotDto>.Invalid("The end time must be later than the start time.");
            }

            // A slot can only occupy a battery bay the node physically owns.
            if (request.BatterySlotNumber > station.TotalBatterySlots)
            {
                return ServiceResult<SlotDto>.Invalid(
                    $"Battery slot number {request.BatterySlotNumber} does not exist at this node, " +
                    $"which has {station.TotalBatterySlots} battery slot(s).");
            }

            var slotDate = DateTime.SpecifyKind(request.SlotDate.Date, DateTimeKind.Utc);

            // Every code already published for this node on this date is read once, which serves both the
            // duplicate check below and the automatic allocation without a query per candidate code.
            var existingCodes = await LoadSlotCodesAsync(request.StationId, slotDate, slotDate, ct);
            var dateKey = slotDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            string slotCode;
            if (!string.IsNullOrWhiteSpace(request.SlotCode))
            {
                // Codes are stored trimmed and upper case so "s1" and "S1 " can never both exist.
                slotCode = request.SlotCode.Trim().ToUpperInvariant();

                if (existingCodes.Contains($"{dateKey}|{slotCode}"))
                {
                    return ServiceResult<SlotDto>.Conflict(
                        $"Slot code '{slotCode}' is already published for this node on " +
                        $"{dateKey}. Choose a different code.",
                        ErrorCodes.SlotCodeExists);
                }
            }
            else
            {
                slotCode = AllocateSlotCode(existingCodes, dateKey);
            }

            var now = _clock.UtcNow;
            var slot = new EnergyBookingSlot
            {
                StationId = request.StationId,
                // Node code and name are denormalised so a slot list needs no second query.
                StationCode = station.StationCode,
                StationName = station.Name,
                SlotCode = slotCode,
                SlotDate = slotDate,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                StartUtc = ComposeUtc(slotDate, request.StartTime),
                EndUtc = ComposeUtc(slotDate, request.EndTime),
                SlotType = request.SlotType,
                EnergyCapacityKwh = request.EnergyCapacityKwh,
                BatterySlotNumber = request.BatterySlotNumber,
                MaxBookings = request.MaxBookings,
                // A brand new slot carries no bookings and is immediately offered to prosumers.
                BookedCount = 0,
                Status = SlotStatus.Available,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = caller.Username,
                UpdatedBy = caller.Username
            };

            await _db.Slots.InsertOneAsync(slot, cancellationToken: ct);

            _logger.LogInformation(
                "Booking slot {SlotCode} published at node {StationCode} for {SlotDate} by {User}.",
                slot.SlotCode, slot.StationCode, dateKey, caller.Username);

            return ServiceResult<SlotDto>.Created(
                slot.ToDto(now, true), "Booking slot created successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to publish a booking slot at node {StationId}.", request.StationId);
            return ServiceResult<SlotDto>.Error("Could not publish the booking slot. Please try again.");
        }
    }

    /// <summary>Lays out a whole date range of booking slots for one node in a single call.</summary>
    /// <param name="request">Node, date range, daily hour window, slot length and battery bay count.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>How many slots were created and skipped, together with human readable notes.</returns>
    public async Task<ServiceResult<GenerateSlotsResultDto>> GenerateAsync(
        GenerateSlotsRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // Bulk publication is the operator's day to day tool, so staff of either kind may run it.
        if (!caller.IsStaff)
        {
            return ServiceResult<GenerateSlotsResultDto>.Forbidden(
                "Only a Backoffice officer or a Grid Operator may generate booking slots.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(request.StationId, out _))
        {
            return ServiceResult<GenerateSlotsResultDto>.NotFound("Microgrid node not found.");
        }

        if (!CanManageSlotsAt(caller, request.StationId))
        {
            return ServiceResult<GenerateSlotsResultDto>.Forbidden(
                "A Grid Operator may only generate slots at their assigned microgrid node.",
                ErrorCodes.Forbidden);
        }

        try
        {
            var station = await _db.Stations
                .Find(Builders<SolarStationInfo>.Filter.Eq(s => s.Id, request.StationId))
                .FirstOrDefaultAsync(ct);

            if (station is null)
            {
                return ServiceResult<GenerateSlotsResultDto>.NotFound("Microgrid node not found.");
            }

            // A node that is out of service must not accumulate new bookable capacity (rule R8).
            if (station.Status != StationStatus.Active)
            {
                return ServiceResult<GenerateSlotsResultDto>.Invalid(
                    "This microgrid node is not in service, so no booking slots can be generated for it.",
                    ErrorCodes.StationInactive);
            }

            // One generated row is published per requested battery bay. Refuse a request that
            // names bays the node does not physically own rather than storing impossible slot data.
            if (request.BatterySlots > station.TotalBatterySlots)
            {
                return ServiceResult<GenerateSlotsResultDto>.Invalid(
                    $"Cannot generate {request.BatterySlots} battery slot(s) per window because this node " +
                    $"has only {station.TotalBatterySlots} battery slot(s).");
            }

            var fromDate = request.FromDate.Date;
            var toDate = request.ToDate.Date;

            if (toDate < fromDate)
            {
                return ServiceResult<GenerateSlotsResultDto>.Invalid(
                    "The to date must be the same as, or later than, the from date.");
            }

            // The span is bounded so a typed-in year does not become a multi thousand document insert.
            var spanDays = (int)(toDate - fromDate).TotalDays + 1;
            if (spanDays > MaxGenerationSpanDays)
            {
                return ServiceResult<GenerateSlotsResultDto>.Invalid(
                    $"Booking slots may be generated for at most {MaxGenerationSpanDays} days at a time, " +
                    $"but {spanDays} days were requested.");
            }

            if (request.EndHour <= request.StartHour)
            {
                return ServiceResult<GenerateSlotsResultDto>.Invalid(
                    "The end hour must be later than the start hour.");
            }

            if (request.EndHour - request.StartHour < request.SlotDurationHours)
            {
                return ServiceResult<GenerateSlotsResultDto>.Invalid(
                    $"The daily window of {request.EndHour - request.StartHour} hour(s) is shorter than the " +
                    $"requested slot duration of {request.SlotDurationHours} hour(s).");
            }

            var fromUtc = DateTime.SpecifyKind(fromDate, DateTimeKind.Utc);
            var toUtc = DateTime.SpecifyKind(toDate, DateTimeKind.Utc);

            // Every slot code already published in the whole range is loaded ONCE into a set keyed
            // "yyyy-MM-dd|CODE". Duplicate detection then costs a hash lookup instead of one MongoDB
            // round trip per candidate slot, which for a 60 day generation would be thousands of queries.
            var publishedCodes = await LoadSlotCodesAsync(request.StationId, fromUtc, toUtc, ct);

            var notes = new List<string>();
            var batch = new List<EnergyBookingSlot>();
            var skipped = 0;
            var capReached = false;
            var now = _clock.UtcNow;

            for (var date = fromDate; date <= toDate && !capReached; date = date.AddDays(1))
            {
                var dateKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var slotDate = DateTime.SpecifyKind(date, DateTimeKind.Utc);

                if (request.SkipClosedDays)
                {
                    // Rule R12 refuses bookings on a closed day, so publishing slots there would be pointless.
                    var daySchedule = station.WeeklySchedule
                        .FirstOrDefault(d => d.DayOfWeek == (int)date.DayOfWeek);

                    if (daySchedule is { IsOpen: false })
                    {
                        notes.Add($"Skipped {dateKey} (node closed)");
                        continue;
                    }
                }

                // The short code restarts at S1 on every calendar day, because uniqueness is only
                // required within a station and a date (see the ux_slots_station_date_code index).
                var sequence = 1;

                for (var hour = request.StartHour;
                     hour + request.SlotDurationHours <= request.EndHour && !capReached;
                     hour += request.SlotDurationHours)
                {
                    var windowEndHour = hour + request.SlotDurationHours;
                    var startTime = hour.ToString("00", CultureInfo.InvariantCulture) + ":00";

                    // Midnight is expressed as 23:59 so the window never rolls over into the next day.
                    var endTime = windowEndHour >= 24
                        ? "23:59"
                        : windowEndHour.ToString("00", CultureInfo.InvariantCulture) + ":00";

                    for (var bay = 1; bay <= request.BatterySlots; bay++)
                    {
                        if (batch.Count >= MaxGeneratedSlots)
                        {
                            capReached = true;
                            break;
                        }

                        var slotCode = "S" + sequence.ToString(CultureInfo.InvariantCulture);
                        sequence++;

                        // Add returns false when the key was already present, so this single call both
                        // detects an existing code and registers the new one against later duplicates.
                        if (!publishedCodes.Add($"{dateKey}|{slotCode}"))
                        {
                            skipped++;
                            continue;
                        }

                        batch.Add(new EnergyBookingSlot
                        {
                            StationId = request.StationId,
                            StationCode = station.StationCode,
                            StationName = station.Name,
                            SlotCode = slotCode,
                            SlotDate = slotDate,
                            StartTime = startTime,
                            EndTime = endTime,
                            StartUtc = ComposeUtc(slotDate, startTime),
                            EndUtc = ComposeUtc(slotDate, endTime),
                            SlotType = request.SlotType,
                            EnergyCapacityKwh = request.EnergyCapacityKwh,
                            BatterySlotNumber = bay,
                            MaxBookings = 1,
                            BookedCount = 0,
                            Status = SlotStatus.Available,
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = caller.Username,
                            UpdatedBy = caller.Username
                        });
                    }
                }
            }

            if (capReached)
            {
                notes.Add(
                    $"Generation stopped at the safety cap of {MaxGeneratedSlots} slot(s). " +
                    "Narrow the date range or the hour window and run the generator again.");
            }

            // The whole batch travels in ONE InsertMany so a 60 day generation is a single round trip.
            if (batch.Count > 0)
            {
                await _db.Slots.InsertManyAsync(batch, cancellationToken: ct);
            }

            var created = batch.Count;

            _logger.LogInformation(
                "Generated {Created} booking slot(s) ({Skipped} skipped) at node {StationCode} by {User}.",
                created, skipped, station.StationCode, caller.Username);

            var result = new GenerateSlotsResultDto
            {
                Created = created,
                Skipped = skipped,
                Notes = notes
            };

            return ServiceResult<GenerateSlotsResultDto>.Ok(result, $"{created} booking slot(s) generated.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to generate booking slots at node {StationId}.", request.StationId);
            return ServiceResult<GenerateSlotsResultDto>.Error(
                "Could not generate the booking slots. Please try again.");
        }
    }

    /// <summary>Edits a booking slot - refuses changes that would strand existing bookings.</summary>
    /// <param name="id">Mongo identifier of the slot to edit.</param>
    /// <param name="request">New window, type, capacity, battery bay, booking limit and optional status.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The updated slot, or the rule violation that stopped the edit.</returns>
    public async Task<ServiceResult<SlotDto>> UpdateAsync(
        string id, UpdateSlotRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // Editing published capacity is an operational task, so staff of either kind may perform it.
        if (!caller.IsStaff)
        {
            return ServiceResult<SlotDto>.Forbidden(
                "Only a Backoffice officer or a Grid Operator may edit a booking slot.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<SlotDto>.NotFound("Booking slot not found.");
        }

        try
        {
            var idFilter = Builders<EnergyBookingSlot>.Filter.Eq(s => s.Id, id);
            var existing = await _db.Slots.Find(idFilter).FirstOrDefaultAsync(ct);
            if (existing is null)
            {
                return ServiceResult<SlotDto>.NotFound("Booking slot not found.");
            }

            if (!CanManageSlotsAt(caller, existing.StationId))
            {
                return ServiceResult<SlotDto>.Forbidden(
                    "A Grid Operator may only edit slots at their assigned microgrid node.",
                    ErrorCodes.Forbidden);
            }

            var station = await _db.Stations
                .Find(Builders<SolarStationInfo>.Filter.Eq(s => s.Id, existing.StationId))
                .FirstOrDefaultAsync(ct);

            if (station is null)
            {
                return ServiceResult<SlotDto>.NotFound("The slot's microgrid node no longer exists.");
            }

            // Editing must enforce the same physical bay limit as publishing a new slot.
            if (request.BatterySlotNumber > station.TotalBatterySlots)
            {
                return ServiceResult<SlotDto>.Invalid(
                    $"Battery slot number {request.BatterySlotNumber} does not exist at this node, " +
                    $"which has {station.TotalBatterySlots} battery slot(s).");
            }

            // Moving a slot that prosumers have already booked would silently change the instant they
            // agreed to, so the time window and the battery bay are frozen once a booking exists.
            var movesTheSlot =
                !string.Equals(existing.StartTime, request.StartTime, StringComparison.Ordinal) ||
                !string.Equals(existing.EndTime, request.EndTime, StringComparison.Ordinal) ||
                existing.BatterySlotNumber != request.BatterySlotNumber;

            if (existing.BookedCount > 0 && movesTheSlot)
            {
                _logger.LogWarning(
                    "Refused to move booked slot {SlotCode} at node {StationCode} ({Bookings} booking(s)).",
                    existing.SlotCode, existing.StationCode, existing.BookedCount);

                return ServiceResult<SlotDto>.Conflict(
                    "This slot already has bookings; its time cannot be changed. Cancel the bookings first.",
                    ErrorCodes.SlotHasBookings);
            }

            // Shrinking the limit below the bookings already taken would leave the slot over subscribed.
            if (request.MaxBookings < existing.BookedCount)
            {
                return ServiceResult<SlotDto>.Invalid(
                    $"The booking limit cannot be lowered to {request.MaxBookings} because this slot " +
                    $"already holds {existing.BookedCount} booking(s).");
            }

            // The wall clock strings are validated before ComposeUtc parses them.
            if (!TimeSpan.TryParse(request.StartTime, CultureInfo.InvariantCulture, out var startOfDay) ||
                !TimeSpan.TryParse(request.EndTime, CultureInfo.InvariantCulture, out var endOfDay))
            {
                return ServiceResult<SlotDto>.Invalid("Start time and end time must both use the HH:mm format.");
            }

            if (endOfDay <= startOfDay)
            {
                return ServiceResult<SlotDto>.Invalid("The end time must be later than the start time.");
            }

            // StartUtc and EndUtc are the only fields the rules ever compare, so they are always recomputed.
            var startUtc = ComposeUtc(existing.SlotDate, request.StartTime);
            var endUtc = ComposeUtc(existing.SlotDate, request.EndTime);

            // Blocked is a deliberate operator decision - it is never overwritten automatically here.
            // Only when the caller does not state a status, and the slot is not Blocked, is the
            // Available / Full pair recomputed from the booking counters.
            SlotStatus status;
            if (request.Status.HasValue)
            {
                status = request.Status.Value;
            }
            else if (existing.Status == SlotStatus.Blocked)
            {
                status = SlotStatus.Blocked;
            }
            else
            {
                status = existing.BookedCount >= request.MaxBookings ? SlotStatus.Full : SlotStatus.Available;
            }

            var update = Builders<EnergyBookingSlot>.Update
                .Set(s => s.StartTime, request.StartTime)
                .Set(s => s.EndTime, request.EndTime)
                .Set(s => s.StartUtc, startUtc)
                .Set(s => s.EndUtc, endUtc)
                .Set(s => s.SlotType, request.SlotType)
                .Set(s => s.EnergyCapacityKwh, request.EnergyCapacityKwh)
                .Set(s => s.BatterySlotNumber, request.BatterySlotNumber)
                .Set(s => s.MaxBookings, request.MaxBookings)
                .Set(s => s.Status, status)
                .Set(s => s.UpdatedAt, _clock.UtcNow)
                .Set(s => s.UpdatedBy, caller.Username);

            var updated = await _db.Slots.FindOneAndUpdateAsync(
                idFilter, update,
                new FindOneAndUpdateOptions<EnergyBookingSlot> { ReturnDocument = ReturnDocument.After },
                ct);

            if (updated is null)
            {
                return ServiceResult<SlotDto>.NotFound("Booking slot not found.");
            }

            _logger.LogInformation(
                "Booking slot {SlotCode} at node {StationCode} updated by {User}.",
                updated.SlotCode, updated.StationCode, caller.Username);

            var activity = await LoadStationActivityAsync(new[] { updated.StationId }, ct);
            var stationActive = activity.TryGetValue(updated.StationId, out var active) && active;

            return ServiceResult<SlotDto>.Ok(
                updated.ToDto(_clock.UtcNow, stationActive), "Booking slot updated successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to update booking slot {SlotId}.", id);
            return ServiceResult<SlotDto>.Error("Could not update the booking slot. Please try again.");
        }
    }

    /// <summary>Blocks a booking slot so it stops accepting new reservations.</summary>
    /// <param name="id">Mongo identifier of the slot to block.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The blocked slot, or NotFound when the id is malformed or unknown.</returns>
    public async Task<ServiceResult<SlotDto>> BlockAsync(
        string id, CurrentUser caller, CancellationToken ct = default)
    {
        // Taking capacity off the market is an operational decision, so staff of either kind may do it.
        if (!caller.IsStaff)
        {
            return ServiceResult<SlotDto>.Forbidden(
                "Only a Backoffice officer or a Grid Operator may block a booking slot.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<SlotDto>.NotFound("Booking slot not found.");
        }

        try
        {
            // Blocking stops NEW bookings only. It deliberately does NOT cancel the reservations that
            // already exist on the slot - those prosumers keep their agreement and must be cancelled
            // explicitly through the reservation workflow, which applies the 12 hour notice rule R3.
            var idFilter = Builders<EnergyBookingSlot>.Filter.Eq(s => s.Id, id);
            var existing = await _db.Slots.Find(idFilter).FirstOrDefaultAsync(ct);
            if (existing is null)
            {
                return ServiceResult<SlotDto>.NotFound("Booking slot not found.");
            }

            if (!CanManageSlotsAt(caller, existing.StationId))
            {
                return ServiceResult<SlotDto>.Forbidden(
                    "A Grid Operator may only block slots at their assigned microgrid node.",
                    ErrorCodes.Forbidden);
            }

            var update = Builders<EnergyBookingSlot>.Update
                .Set(s => s.Status, SlotStatus.Blocked)
                .Set(s => s.UpdatedAt, _clock.UtcNow)
                .Set(s => s.UpdatedBy, caller.Username);

            var updated = await _db.Slots.FindOneAndUpdateAsync(
                idFilter, update,
                new FindOneAndUpdateOptions<EnergyBookingSlot> { ReturnDocument = ReturnDocument.After },
                ct);

            if (updated is null)
            {
                return ServiceResult<SlotDto>.NotFound("Booking slot not found.");
            }

            _logger.LogInformation(
                "Booking slot {SlotCode} at node {StationCode} blocked by {User}; {Bookings} existing booking(s) kept.",
                updated.SlotCode, updated.StationCode, caller.Username, updated.BookedCount);

            var activity = await LoadStationActivityAsync(new[] { updated.StationId }, ct);
            var stationActive = activity.TryGetValue(updated.StationId, out var active) && active;

            return ServiceResult<SlotDto>.Ok(
                updated.ToDto(_clock.UtcNow, stationActive), "Booking slot blocked successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to block booking slot {SlotId}.", id);
            return ServiceResult<SlotDto>.Error("Could not block the booking slot. Please try again.");
        }
    }

    /// <summary>Permanently removes a booking slot - refused while it carries bookings.</summary>
    /// <param name="id">Mongo identifier of the slot to delete.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>True on deletion, or the rule violation that stopped it.</returns>
    public async Task<ServiceResult<bool>> DeleteAsync(
        string id, CurrentUser caller, CancellationToken ct = default)
    {
        // Deleting published capacity is an operational task, so staff of either kind may perform it.
        if (!caller.IsStaff)
        {
            return ServiceResult<bool>.Forbidden(
                "Only a Backoffice officer or a Grid Operator may delete a booking slot.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<bool>.NotFound("Booking slot not found.");
        }

        try
        {
            var idFilter = Builders<EnergyBookingSlot>.Filter.Eq(s => s.Id, id);
            var existing = await _db.Slots.Find(idFilter).FirstOrDefaultAsync(ct);
            if (existing is null)
            {
                return ServiceResult<bool>.NotFound("Booking slot not found.");
            }

            if (!CanManageSlotsAt(caller, existing.StationId))
            {
                return ServiceResult<bool>.Forbidden(
                    "A Grid Operator may only delete slots at their assigned microgrid node.",
                    ErrorCodes.Forbidden);
            }

            // Deleting a booked slot would orphan the reservations that point at it, so the slot is
            // kept and the operator is told to block it instead.
            if (existing.BookedCount > 0)
            {
                return ServiceResult<bool>.Conflict(
                    $"This slot holds {existing.BookedCount} booking(s) and cannot be deleted. " +
                    "Block it, or cancel the bookings first.",
                    ErrorCodes.SlotHasBookings);
            }

            var deleteResult = await _db.Slots.DeleteOneAsync(idFilter, ct);
            if (deleteResult.DeletedCount == 0)
            {
                return ServiceResult<bool>.NotFound("Booking slot not found.");
            }

            _logger.LogInformation(
                "Booking slot {SlotCode} at node {StationCode} deleted by {User}.",
                existing.SlotCode, existing.StationCode, caller.Username);

            return ServiceResult<bool>.Ok(true, "Booking slot deleted successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to delete booking slot {SlotId}.", id);
            return ServiceResult<bool>.Error("Could not delete the booking slot. Please try again.");
        }
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>Checks whether a staff caller may mutate slots at one microgrid node.</summary>
    /// <param name="caller">Authenticated staff caller.</param>
    /// <param name="stationId">Node that owns the slot or generation request.</param>
    /// <returns>True for Backoffice, or for a Grid Operator assigned to this exact node.</returns>
    private static bool CanManageSlotsAt(CurrentUser caller, string stationId)
    {
        return caller.IsBackoffice ||
               (caller.IsGridOperator &&
                !string.IsNullOrWhiteSpace(caller.AssignedStationId) &&
                string.Equals(caller.AssignedStationId, stationId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Turns a calendar day plus a "HH:mm" wall clock string into the absolute instant
    /// the business rules compare against.
    ///
    /// This project treats a microgrid node's wall clock time as UTC. Sri Lanka has one
    /// fixed offset and no daylight saving, so introducing a time zone database would add
    /// nothing but ambiguity to the demonstration - a slot labelled "08:00" is therefore
    /// stored as 08:00 UTC and every client renders exactly what the operator typed.
    ///
    /// StartUtc and EndUtc produced here are the ONLY fields any business rule ever
    /// compares against: the 7 day window (R1), the 12 hour notice (R2 / R3), the
    /// bookability test (R8) and the QR scan window (R9) all read these instants, never
    /// the display strings and never SlotDate.
    /// </summary>
    /// <param name="slotDate">Calendar day the slot belongs to; only its date part is used.</param>
    /// <param name="hhmm">Wall clock time of day in "HH:mm" form, for example "08:00".</param>
    /// <returns>The composed instant, explicitly tagged as UTC.</returns>
    private static DateTime ComposeUtc(DateTime slotDate, string hhmm)
    {
        // SpecifyKind rather than ToUniversalTime: the value is already UTC by definition here,
        // so converting it would shift it by the server's local offset.
        return DateTime.SpecifyKind(slotDate.Date.Add(TimeSpan.Parse(hhmm)), DateTimeKind.Utc);
    }

    /// <summary>
    /// Resolves whether each referenced microgrid node is currently Active, in ONE query.
    ///
    /// SlotDto.IsBookable is rule R8 evaluated on the server, and part of that rule is
    /// "the parent node must be Active". Asking MongoDB for the node of every slot on a
    /// page would be the classic N+1 problem - twenty slots would cost twenty extra round
    /// trips - so the distinct node ids of the whole page are fetched together and the
    /// answer is looked up from this dictionary instead.
    /// </summary>
    /// <param name="stationIds">Node ids referenced by the slots being projected; duplicates are fine.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Map of node id to "is this node Active"; ids that no longer exist are simply absent.</returns>
    private async Task<Dictionary<string, bool>> LoadStationActivityAsync(
        IEnumerable<string> stationIds, CancellationToken ct)
    {
        // Malformed or empty ids are dropped up front: they can never match a stored ObjectId
        // and would make the driver throw while translating the filter.
        var ids = stationIds
            .Where(stationId => !string.IsNullOrWhiteSpace(stationId) && ObjectId.TryParse(stationId, out _))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            return new Dictionary<string, bool>(StringComparer.Ordinal);
        }

        var stations = await _db.Stations
            .Find(Builders<SolarStationInfo>.Filter.In(s => s.Id, ids))
            .ToListAsync(ct);

        return stations.ToDictionary(
            station => station.Id,
            station => station.Status == StationStatus.Active,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads every slot code already published by a node between two dates into a set keyed
    /// "yyyy-MM-dd|CODE", so duplicate detection during creation and bulk generation costs a
    /// hash lookup rather than one MongoDB round trip per candidate slot.
    /// </summary>
    /// <param name="stationId">Node whose published codes are wanted.</param>
    /// <param name="fromUtc">First calendar day of the range, at UTC midnight, inclusive.</param>
    /// <param name="toUtc">Last calendar day of the range, at UTC midnight, inclusive.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Set of "yyyy-MM-dd|CODE" keys already taken at that node.</returns>
    private async Task<HashSet<string>> LoadSlotCodesAsync(
        string stationId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // The upper bound is exclusive on the day after "toUtc" so the last day is fully included.
        var builder = Builders<EnergyBookingSlot>.Filter;
        var filter = builder.Eq(s => s.StationId, stationId)
                     & builder.Gte(s => s.SlotDate, fromUtc)
                     & builder.Lt(s => s.SlotDate, toUtc.AddDays(1));

        var slots = await _db.Slots
            .Find(filter)
            .Project(s => new { s.SlotDate, s.SlotCode })
            .ToListAsync(ct);

        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            codes.Add($"{slot.SlotDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{slot.SlotCode}");
        }

        return codes;
    }

    /// <summary>
    /// Allocates the next free "S{n}" code for one node on one date by starting just past
    /// the number of codes already taken and probing upward until an unused one is found.
    /// </summary>
    /// <param name="existingCodes">Codes already published, keyed "yyyy-MM-dd|CODE".</param>
    /// <param name="dateKey">The calendar day being filled, formatted "yyyy-MM-dd".</param>
    /// <returns>A code that is not yet in use at that node on that date.</returns>
    private static string AllocateSlotCode(HashSet<string> existingCodes, string dateKey)
    {
        // Starting from "count + 1" means the common case - a tidy S1..Sn sequence - is found on the
        // first probe, while the loop still copes with gaps left behind by deleted slots.
        var prefix = $"{dateKey}|";
        var taken = existingCodes.Count(key => key.StartsWith(prefix, StringComparison.Ordinal));

        var sequence = taken + 1;
        while (existingCodes.Contains($"{prefix}S{sequence.ToString(CultureInfo.InvariantCulture)}"))
        {
            sequence++;
        }

        return "S" + sequence.ToString(CultureInfo.InvariantCulture);
    }
}
