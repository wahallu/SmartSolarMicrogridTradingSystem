/* =============================================================================
 * File        : StationService.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Services / Implementations
 * Description : Every operation that may be performed on a microgrid node lives
 *               here: listing and searching nodes, the map "nearby" projection
 *               whose great circle distances are computed on the SERVER, node
 *               registration, schedule and battery bay maintenance, and the
 *               activate / deactivate / delete life cycle. Rule R4 (a node that
 *               still holds future Pending or Approved reservations may not be
 *               deactivated) and rule R10 (only a Backoffice officer may create
 *               or modify a node) are enforced here and nowhere else, which is
 *               what keeps this a FAT service and the clients purely UI.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartSolar.Api.Common;
using SmartSolar.Api.Configuration;
using SmartSolar.Api.Data;
using SmartSolar.Api.DTOs.Stations;
using SmartSolar.Api.Models;
using SmartSolar.Api.Models.Enums;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Services.Implementations;

/// <summary>
/// MongoDB backed implementation of <see cref="IStationService"/> - the single
/// authority on microgrid node data for the web and Android applications.
/// </summary>
public class StationService : IStationService
{
    /// <summary>Mean radius of the earth in kilometres, used by the Haversine formula.</summary>
    private const double EarthRadiusKm = 6371.0;

    private readonly MongoDbContext _db;
    private readonly BusinessRuleSettings _rules;
    private readonly IClock _clock;
    private readonly ILogger<StationService> _logger;

    /// <summary>Captures the data context, the tunable rule constants, the clock and the logger.</summary>
    /// <param name="db">Access to the four MongoDB collections.</param>
    /// <param name="rules">Business rule constants bound from appsettings.json.</param>
    /// <param name="clock">Injected clock - every rule reads "now" from here so it stays testable.</param>
    /// <param name="logger">Diagnostics sink for the node life cycle operations.</param>
    public StationService(
        MongoDbContext db,
        IOptions<BusinessRuleSettings> rules,
        IClock clock,
        ILogger<StationService> logger)
    {
        // Dependencies are stored once; the options value is unwrapped here so rule code stays readable.
        _db = db;
        _rules = rules.Value;
        _clock = clock;
        _logger = logger;
    }

    // =========================================================================
    // Queries
    // =========================================================================

    /// <summary>Paged, filtered list of microgrid nodes sorted by station code.</summary>
    /// <param name="query">Optional status, city and free text filters plus paging.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>One page of <see cref="StationDto"/>, each carrying its active reservation count.</returns>
    public async Task<ServiceResult<PagedResult<StationDto>>> GetStationsAsync(
        StationQuery query, CancellationToken ct = default)
    {
        // The filter is assembled with the driver's FilterDefinitionBuilder so the query is index friendly.
        var builder = Builders<SolarStationInfo>.Filter;
        var filter = builder.Empty;

        if (query.Status.HasValue)
        {
            // Status is persisted as a string but the builder handles the enum representation for us.
            filter &= builder.Eq(s => s.Status, query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.City))
        {
            // City is matched case insensitively and anchored, so "colombo" finds "Colombo" but not "Colombo North".
            var cityPattern = new BsonRegularExpression($"^{Regex.Escape(query.City.Trim())}$", "i");
            filter &= builder.Regex(s => s.City, cityPattern);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // The search term is escaped before it becomes a regex so a user typed "(" cannot break the query.
            var searchPattern = new BsonRegularExpression(Regex.Escape(query.Search.Trim()), "i");
            filter &= builder.Or(
                builder.Regex(s => s.StationCode, searchPattern),
                builder.Regex(s => s.Name, searchPattern),
                builder.Regex(s => s.Address, searchPattern),
                builder.Regex(s => s.City, searchPattern));
        }

        var totalCount = await _db.Stations.CountDocumentsAsync(filter, cancellationToken: ct);

        var stations = await _db.Stations
            .Find(filter)
            .SortBy(s => s.StationCode)
            .Skip(query.Skip)
            .Limit(query.PageSize)
            .ToListAsync(ct);

        // Each row carries its own future booking count so the web grid can grey out the deactivate button (R4).
        var items = new List<StationDto>(stations.Count);
        foreach (var station in stations)
        {
            var activeCount = await CountActiveReservationsAsync(station.Id, ct);
            items.Add(station.ToDto(activeCount));
        }

        var page = PagedResult<StationDto>.Create(items, query.Page, query.PageSize, totalCount);
        return ServiceResult<PagedResult<StationDto>>.Ok(
            page, $"Retrieved {items.Count} of {totalCount} microgrid node(s).");
    }

    /// <summary>Reads a single microgrid node by its MongoDB identifier.</summary>
    /// <param name="id">Mongo ObjectId of the node, as a string.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The node, or NotFound when the id is malformed or unknown.</returns>
    public async Task<ServiceResult<StationDto>> GetByIdAsync(string id, CancellationToken ct = default)
    {
        // A malformed id is answered as NotFound rather than being handed to the driver, which would throw.
        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        var station = await _db.Stations
            .Find(Builders<SolarStationInfo>.Filter.Eq(s => s.Id, id))
            .FirstOrDefaultAsync(ct);

        if (station is null)
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        var activeCount = await CountActiveReservationsAsync(station.Id, ct);
        return ServiceResult<StationDto>.Ok(station.ToDto(activeCount), "Microgrid node retrieved successfully.");
    }

    /// <summary>Active nodes within a radius of a GPS point, nearest first - the map feature.</summary>
    /// <param name="query">Caller position, optional radius override and a result limit.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Nodes ordered by ascending distance, each carrying its distance in kilometres.</returns>
    public async Task<ServiceResult<List<NearbyStationDto>>> GetNearbyAsync(
        NearbyStationQuery query, CancellationToken ct = default)
    {
        // When the caller does not state a radius the configured default from appsettings.json applies.
        var radiusKm = query.RadiusKm ?? _rules.DefaultNearbyRadiusKm;

        // Only nodes that are in service are worth showing on a map, so inactive ones never leave the server.
        var activeFilter = Builders<SolarStationInfo>.Filter.Eq(s => s.Status, StationStatus.Active);
        var stations = await _db.Stations.Find(activeFilter).ToListAsync(ct);

        // Distances are computed here, on the server, and shipped as a number: neither the React map nor the
        // Android Google Maps screen is allowed to re-derive them (FAT service - clients hold no logic).
        var list = stations
            .Select(station => new
            {
                Station = station,
                DistanceKm = HaversineKm(
                    query.Lat, query.Lng,
                    station.Location.Latitude, station.Location.Longitude)
            })
            .Where(x => x.DistanceKm <= radiusKm)
            .OrderBy(x => x.DistanceKm)
            .Take(query.Limit)
            // The map screen only needs position and distance, so the per node reservation count is
            // deliberately not queried here - it would cost one extra round trip per pin.
            .Select(x => x.Station.ToNearbyDto(x.DistanceKm))
            .ToList();

        return ServiceResult<List<NearbyStationDto>>.Ok(
            list, $"Found {list.Count} microgrid node(s) within {radiusKm} km.");
    }

    // =========================================================================
    // Commands
    // =========================================================================

    /// <summary>Registers a new microgrid node - Backoffice only (rule R10).</summary>
    /// <param name="request">Node details including GPS position, capacity and battery bays.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Created with the stored node, or the rule violation that stopped it.</returns>
    public async Task<ServiceResult<StationDto>> CreateAsync(
        CreateStationRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - node registration is a back office privilege; operators may only maintain existing nodes.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<StationDto>.Forbidden(
                "Only a Backoffice officer may register a microgrid node.", ErrorCodes.Forbidden);
        }

        // Codes are stored upper case and trimmed so "stn-001" and "STN-001 " can never both exist.
        var stationCode = (request.StationCode ?? string.Empty).Trim().ToUpperInvariant();

        var duplicates = await _db.Stations.CountDocumentsAsync(
            Builders<SolarStationInfo>.Filter.Eq(s => s.StationCode, stationCode), cancellationToken: ct);
        if (duplicates > 0)
        {
            return ServiceResult<StationDto>.Conflict(
                $"A microgrid node with the code '{stationCode}' already exists.",
                ErrorCodes.StationCodeExists);
        }

        // A node cannot report more free battery bays than it physically owns.
        if (request.AvailableBatterySlots > request.TotalBatterySlots)
        {
            return ServiceResult<StationDto>.Invalid(
                "Available battery slots cannot exceed the total number of battery slots.");
        }

        // A caller supplied schedule must be complete; otherwise a sensible open-all-week default is generated.
        List<DaySchedule> weeklySchedule;
        if (request.WeeklySchedule is { Count: > 0 })
        {
            var shapeErrors = ValidateScheduleShape(request.WeeklySchedule);
            if (shapeErrors is not null)
            {
                return ServiceResult<StationDto>.Invalid(
                    "The weekly schedule must contain exactly seven days, one for each day of the week.",
                    ErrorCodes.ValidationFailed, shapeErrors);
            }

            weeklySchedule = ToEntitySchedule(request.WeeklySchedule);
        }
        else
        {
            weeklySchedule = DomainMapper.DefaultWeeklySchedule(
                request.OperatingHours.OpenTime, request.OperatingHours.CloseTime);
        }

        var now = _clock.UtcNow;
        var station = new SolarStationInfo
        {
            StationCode = stationCode,
            Name = (request.Name ?? string.Empty).Trim(),
            Type = request.Type,
            Address = (request.Address ?? string.Empty).Trim(),
            City = (request.City ?? string.Empty).Trim(),
            Location = new GeoLocation { Latitude = request.Latitude, Longitude = request.Longitude },
            CapacityKwh = request.CapacityKwh,
            TotalBatterySlots = request.TotalBatterySlots,
            AvailableBatterySlots = request.AvailableBatterySlots,
            OperatingHours = new OperatingHours
            {
                OpenTime = request.OperatingHours.OpenTime,
                CloseTime = request.OperatingHours.CloseTime
            },
            WeeklySchedule = weeklySchedule,
            ContactNumber = (request.ContactNumber ?? string.Empty).Trim(),
            // A brand new node enters service immediately so slots may be published against it.
            Status = StationStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = caller.Username,
            UpdatedBy = caller.Username
        };

        await _db.Stations.InsertOneAsync(station, cancellationToken: ct);
        _logger.LogInformation("Microgrid node {StationCode} created by {User}.", station.StationCode, caller.Username);

        return ServiceResult<StationDto>.Created(station.ToDto(0), "Microgrid node created successfully.");
    }

    /// <summary>Edits an existing microgrid node - Backoffice only (rule R10).</summary>
    /// <param name="id">Mongo identifier of the node to edit.</param>
    /// <param name="request">New values; the station code itself is immutable.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The updated node, or the rule violation that stopped the edit.</returns>
    public async Task<ServiceResult<StationDto>> UpdateAsync(
        string id, UpdateStationRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - only the back office may change node master data.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<StationDto>.Forbidden(
                "Only a Backoffice officer may edit a microgrid node.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        var idFilter = Builders<SolarStationInfo>.Filter.Eq(s => s.Id, id);
        var existing = await _db.Stations.Find(idFilter).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        // The same physical constraint as on creation - free bays can never exceed installed bays.
        if (request.AvailableBatterySlots > request.TotalBatterySlots)
        {
            return ServiceResult<StationDto>.Invalid(
                "Available battery slots cannot exceed the total number of battery slots.");
        }

        var newName = (request.Name ?? string.Empty).Trim();

        // StationCode is deliberately absent from UpdateStationRequest: it is the business key that slots and
        // reservations were denormalised against, so it is immutable once the node has been registered.
        var update = Builders<SolarStationInfo>.Update
            .Set(s => s.Name, newName)
            .Set(s => s.Type, request.Type)
            .Set(s => s.Address, (request.Address ?? string.Empty).Trim())
            .Set(s => s.City, (request.City ?? string.Empty).Trim())
            .Set(s => s.Location, new GeoLocation { Latitude = request.Latitude, Longitude = request.Longitude })
            .Set(s => s.CapacityKwh, request.CapacityKwh)
            .Set(s => s.TotalBatterySlots, request.TotalBatterySlots)
            .Set(s => s.AvailableBatterySlots, request.AvailableBatterySlots)
            .Set(s => s.OperatingHours, new OperatingHours
            {
                OpenTime = request.OperatingHours.OpenTime,
                CloseTime = request.OperatingHours.CloseTime
            })
            .Set(s => s.ContactNumber, (request.ContactNumber ?? string.Empty).Trim())
            .Set(s => s.UpdatedAt, _clock.UtcNow)
            .Set(s => s.UpdatedBy, caller.Username);

        var updated = await _db.Stations.FindOneAndUpdateAsync(
            idFilter, update,
            new FindOneAndUpdateOptions<SolarStationInfo> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is null)
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        // Slot and reservation documents each carry a denormalised copy of the node code and name so that a
        // list screen needs no second query. When the master record changes those copies must be rewritten in
        // the same operation, otherwise the phone would keep showing the node's previous name for ever.
        if (!string.Equals(existing.Name, updated.Name, StringComparison.Ordinal) ||
            !string.Equals(existing.StationCode, updated.StationCode, StringComparison.Ordinal))
        {
            await _db.Slots.UpdateManyAsync(
                Builders<EnergyBookingSlot>.Filter.Eq(s => s.StationId, id),
                Builders<EnergyBookingSlot>.Update
                    .Set(s => s.StationCode, updated.StationCode)
                    .Set(s => s.StationName, updated.Name)
                    .Set(s => s.UpdatedAt, _clock.UtcNow),
                cancellationToken: ct);

            await _db.Reservations.UpdateManyAsync(
                Builders<EnergyReservation>.Filter.Eq(r => r.StationId, id),
                Builders<EnergyReservation>.Update
                    .Set(r => r.StationCode, updated.StationCode)
                    .Set(r => r.StationName, updated.Name)
                    .Set(r => r.UpdatedAt, _clock.UtcNow),
                cancellationToken: ct);

            _logger.LogInformation(
                "Denormalised node code/name refreshed on slots and reservations of {StationCode}.",
                updated.StationCode);
        }

        var activeCount = await CountActiveReservationsAsync(updated.Id, ct);
        return ServiceResult<StationDto>.Ok(updated.ToDto(activeCount), "Microgrid node updated successfully.");
    }

    /// <summary>Replaces the seven day operating schedule - Backoffice or Grid Operator.</summary>
    /// <param name="id">Mongo identifier of the node.</param>
    /// <param name="request">Default operating hours plus exactly seven day rows.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The updated node, or the validation failure that stopped the change.</returns>
    public async Task<ServiceResult<StationDto>> UpdateScheduleAsync(
        string id, UpdateScheduleRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // Schedule maintenance is an operational task, so site operators may perform it as well as the back office.
        if (!caller.IsStaff)
        {
            return ServiceResult<StationDto>.Forbidden(
                "Only a Backoffice officer or a Grid Operator may change a node schedule.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        var idFilter = Builders<SolarStationInfo>.Filter.Eq(s => s.Id, id);
        var existing = await _db.Stations.Find(idFilter).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        // Rule R12 is evaluated against this schedule when a booking is placed, so it must be complete and sane.
        var shapeErrors = ValidateScheduleShape(request.WeeklySchedule);
        if (shapeErrors is not null)
        {
            return ServiceResult<StationDto>.Invalid(
                "The weekly schedule must contain exactly seven days, one for each day of the week.",
                ErrorCodes.ValidationFailed, shapeErrors);
        }

        // On every day the node is open the closing time must genuinely come after the opening time.
        var timeErrors = new Dictionary<string, List<string>>();
        foreach (var day in request.WeeklySchedule.Where(d => d.IsOpen))
        {
            var field = $"weeklySchedule[{day.DayOfWeek}]";
            if (!TimeSpan.TryParse(day.OpenTime, out var open) || !TimeSpan.TryParse(day.CloseTime, out var close))
            {
                timeErrors[field] = new List<string>
                {
                    $"{DomainMapper.DayName(day.DayOfWeek)} has an opening or closing time that is not in HH:mm format."
                };
                continue;
            }

            if (close <= open)
            {
                timeErrors[field] = new List<string>
                {
                    $"{DomainMapper.DayName(day.DayOfWeek)} closing time must be later than its opening time."
                };
            }
        }

        if (timeErrors.Count > 0)
        {
            return ServiceResult<StationDto>.Invalid(
                "One or more days in the weekly schedule have invalid opening and closing times.",
                ErrorCodes.ValidationFailed, timeErrors);
        }

        var update = Builders<SolarStationInfo>.Update
            .Set(s => s.OperatingHours, new OperatingHours
            {
                OpenTime = request.OperatingHours.OpenTime,
                CloseTime = request.OperatingHours.CloseTime
            })
            .Set(s => s.WeeklySchedule, ToEntitySchedule(request.WeeklySchedule))
            .Set(s => s.UpdatedAt, _clock.UtcNow)
            .Set(s => s.UpdatedBy, caller.Username);

        var updated = await _db.Stations.FindOneAndUpdateAsync(
            idFilter, update,
            new FindOneAndUpdateOptions<SolarStationInfo> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is null)
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        _logger.LogInformation(
            "Weekly schedule of node {StationCode} updated by {User}.", updated.StationCode, caller.Username);

        var activeCount = await CountActiveReservationsAsync(updated.Id, ct);
        return ServiceResult<StationDto>.Ok(updated.ToDto(activeCount), "Operating schedule updated successfully.");
    }

    /// <summary>Updates the free battery bay count - Backoffice or Grid Operator.</summary>
    /// <param name="id">Mongo identifier of the node.</param>
    /// <param name="request">The new number of free battery bays.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The updated node, or the validation failure that stopped the change.</returns>
    public async Task<ServiceResult<StationDto>> UpdateAvailabilityAsync(
        string id, UpdateAvailabilityRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // Bay availability changes hour by hour on site, so the grid operator maintains it from the mobile app.
        if (!caller.IsStaff)
        {
            return ServiceResult<StationDto>.Forbidden(
                "Only a Backoffice officer or a Grid Operator may update battery bay availability.",
                ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        var idFilter = Builders<SolarStationInfo>.Filter.Eq(s => s.Id, id);
        var existing = await _db.Stations.Find(idFilter).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        // The reported figure has to fall inside the physical capacity of the node.
        if (request.AvailableBatterySlots < 0 || request.AvailableBatterySlots > existing.TotalBatterySlots)
        {
            return ServiceResult<StationDto>.Invalid(
                $"Available battery slots must be between 0 and {existing.TotalBatterySlots}.");
        }

        var update = Builders<SolarStationInfo>.Update
            .Set(s => s.AvailableBatterySlots, request.AvailableBatterySlots)
            .Set(s => s.UpdatedAt, _clock.UtcNow)
            .Set(s => s.UpdatedBy, caller.Username);

        var updated = await _db.Stations.FindOneAndUpdateAsync(
            idFilter, update,
            new FindOneAndUpdateOptions<SolarStationInfo> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is null)
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        var activeCount = await CountActiveReservationsAsync(updated.Id, ct);
        return ServiceResult<StationDto>.Ok(
            updated.ToDto(activeCount), "Battery bay availability updated successfully.");
    }

    /// <summary>Returns a microgrid node to service - Backoffice only (rule R10).</summary>
    /// <param name="id">Mongo identifier of the node.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The reactivated node, or the rule violation that stopped it.</returns>
    public async Task<ServiceResult<StationDto>> ActivateAsync(
        string id, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - returning a node to service is a back office decision.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<StationDto>.Forbidden(
                "Only a Backoffice officer may activate a microgrid node.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        var idFilter = Builders<SolarStationInfo>.Filter.Eq(s => s.Id, id);
        var update = Builders<SolarStationInfo>.Update
            .Set(s => s.Status, StationStatus.Active)
            .Set(s => s.UpdatedAt, _clock.UtcNow)
            .Set(s => s.UpdatedBy, caller.Username);

        var updated = await _db.Stations.FindOneAndUpdateAsync(
            idFilter, update,
            new FindOneAndUpdateOptions<SolarStationInfo> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is null)
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        _logger.LogInformation("Node {StationCode} activated by {User}.", updated.StationCode, caller.Username);

        var activeCount = await CountActiveReservationsAsync(updated.Id, ct);
        return ServiceResult<StationDto>.Ok(updated.ToDto(activeCount), "Microgrid node activated successfully.");
    }

    /// <summary>Takes a microgrid node out of service - Backoffice only, guarded by rule R4.</summary>
    /// <param name="id">Mongo identifier of the node.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The deactivated node, or a Conflict when future bookings still exist.</returns>
    public async Task<ServiceResult<StationDto>> DeactivateAsync(
        string id, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - taking a node out of service is a back office decision.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<StationDto>.Forbidden(
                "Only a Backoffice officer may deactivate a microgrid node.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        var idFilter = Builders<SolarStationInfo>.Filter.Eq(s => s.Id, id);
        var existing = await _db.Stations.Find(idFilter).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        // ***RULE R4*** - a node holding Pending or Approved reservations that have not started yet cannot be
        // switched off, because those prosumers are still expecting to trade energy there.
        var activeReservations = await CountActiveReservationsAsync(existing.Id, ct);
        if (activeReservations > 0)
        {
            _logger.LogWarning(
                "R4 blocked deactivation of node {StationCode}: {Count} active reservation(s).",
                existing.StationCode, activeReservations);

            return ServiceResult<StationDto>.Conflict(
                $"This microgrid node cannot be deactivated because it still has {activeReservations} " +
                "active energy reservation(s). Cancel or complete them first.",
                ErrorCodes.StationHasActiveReservations);
        }

        var update = Builders<SolarStationInfo>.Update
            .Set(s => s.Status, StationStatus.Inactive)
            .Set(s => s.UpdatedAt, _clock.UtcNow)
            .Set(s => s.UpdatedBy, caller.Username);

        var updated = await _db.Stations.FindOneAndUpdateAsync(
            idFilter, update,
            new FindOneAndUpdateOptions<SolarStationInfo> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is null)
        {
            return ServiceResult<StationDto>.NotFound("Microgrid node not found.");
        }

        _logger.LogInformation("Node {StationCode} deactivated by {User}.", updated.StationCode, caller.Username);

        return ServiceResult<StationDto>.Ok(updated.ToDto(0), "Microgrid node deactivated successfully.");
    }

    /// <summary>Permanently removes a node and its slots - refused once it has booking history.</summary>
    /// <param name="id">Mongo identifier of the node.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>True on deletion, or a Conflict when reservation history references the node.</returns>
    public async Task<ServiceResult<bool>> DeleteAsync(
        string id, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - a hard delete is the most destructive operation on a node, so it is back office only.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<bool>.Forbidden(
                "Only a Backoffice officer may delete a microgrid node.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<bool>.NotFound("Microgrid node not found.");
        }

        var idFilter = Builders<SolarStationInfo>.Filter.Eq(s => s.Id, id);
        var existing = await _db.Stations.Find(idFilter).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            return ServiceResult<bool>.NotFound("Microgrid node not found.");
        }

        // ANY reservation - not only future ones - makes the node part of the audit trail, so it is kept.
        var reservationCount = await _db.Reservations.CountDocumentsAsync(
            Builders<EnergyReservation>.Filter.Eq(r => r.StationId, id), cancellationToken: ct);

        if (reservationCount > 0)
        {
            return ServiceResult<bool>.Conflict(
                "This node has reservation history and cannot be deleted. Deactivate it instead.",
                ErrorCodes.Conflict);
        }

        // Slots exist only to serve their node, so they are removed with it and never left orphaned.
        var deletedSlots = await _db.Slots.DeleteManyAsync(
            Builders<EnergyBookingSlot>.Filter.Eq(s => s.StationId, id), ct);

        await _db.Stations.DeleteOneAsync(idFilter, ct);

        _logger.LogInformation(
            "Node {StationCode} and {SlotCount} of its slots deleted by {User}.",
            existing.StationCode, deletedSlots.DeletedCount, caller.Username);

        return ServiceResult<bool>.Ok(true, "Microgrid node deleted successfully.");
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Counts the reservations that keep a node "in use": status Pending or Approved
    /// with a start instant that has not passed yet. This one helper backs both the
    /// StationDto.ActiveReservationCount field and the rule R4 deactivation guard, so
    /// the number the clients display and the number the rule enforces can never drift.
    /// </summary>
    /// <param name="stationId">Mongo identifier of the node being inspected.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Number of future Pending or Approved reservations held by the node.</returns>
    private async Task<long> CountActiveReservationsAsync(string stationId, CancellationToken ct)
    {
        // A malformed id can never match a stored ObjectId, so answering zero avoids a driver exception.
        if (!ObjectId.TryParse(stationId, out _))
        {
            return 0;
        }

        // "Now" comes from the injected clock so a frozen clock in a unit test moves this boundary too.
        var now = _clock.UtcNow;
        var builder = Builders<EnergyReservation>.Filter;
        var filter = builder.Eq(r => r.StationId, stationId)
                     & builder.In(r => r.Status, new[] { ReservationStatus.Pending, ReservationStatus.Approved })
                     & builder.Gte(r => r.ReservationStartUtc, now);

        return await _db.Reservations.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    /// <summary>
    /// Great circle distance between two GPS positions using the Haversine formula:
    /// a = sin^2(dLat/2) + cos(lat1) * cos(lat2) * sin^2(dLon/2), then
    /// c = 2 * atan2(sqrt(a), sqrt(1 - a)) and finally d = R * c with R = 6371 km.
    /// It treats the earth as a sphere, which is accurate to a fraction of a percent
    /// over the distances a microgrid map covers.
    ///
    /// The calculation is performed HERE, on the server, on purpose. This is a FAT
    /// service: the React map and the Android Google Maps screen receive a finished
    /// distanceKm number and must never compute distances themselves, so the ordering
    /// and the radius filter behave identically on both clients.
    /// </summary>
    /// <param name="lat1">Latitude of the first point in degrees.</param>
    /// <param name="lon1">Longitude of the first point in degrees.</param>
    /// <param name="lat2">Latitude of the second point in degrees.</param>
    /// <param name="lon2">Longitude of the second point in degrees.</param>
    /// <returns>Distance between the two points in kilometres.</returns>
    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        // Trigonometry in .NET works in radians, so every degree value is converted first.
        var lat1Rad = DegreesToRadians(lat1);
        var lat2Rad = DegreesToRadians(lat2);
        var deltaLat = DegreesToRadians(lat2 - lat1);
        var deltaLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
                + Math.Cos(lat1Rad) * Math.Cos(lat2Rad)
                  * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusKm * c;
    }

    /// <summary>Converts an angle expressed in degrees into radians.</summary>
    /// <param name="degrees">Angle in degrees.</param>
    /// <returns>The same angle in radians.</returns>
    private static double DegreesToRadians(double degrees)
    {
        // Kept as a named helper so the Haversine formula above reads like the textbook version.
        return degrees * Math.PI / 180.0;
    }

    /// <summary>Checks that a weekly schedule carries exactly seven distinct days numbered 0 to 6.</summary>
    /// <param name="schedule">The schedule rows supplied by the caller.</param>
    /// <returns>Null when the shape is valid, otherwise the field error dictionary to return.</returns>
    private static Dictionary<string, List<string>>? ValidateScheduleShape(List<DayScheduleDto> schedule)
    {
        // Both clients render a fixed seven row table, so a partial schedule is rejected rather than padded.
        var errors = new Dictionary<string, List<string>>();

        if (schedule.Count != 7)
        {
            errors["weeklySchedule"] = new List<string>
            {
                $"The weekly schedule must contain exactly 7 entries but {schedule.Count} were supplied."
            };
            return errors;
        }

        var days = schedule.Select(d => d.DayOfWeek).ToList();

        if (days.Any(d => d is < 0 or > 6))
        {
            errors["weeklySchedule"] = new List<string>
            {
                "Every schedule entry must use a day of week between 0 (Sunday) and 6 (Saturday)."
            };
            return errors;
        }

        if (days.Distinct().Count() != 7)
        {
            errors["weeklySchedule"] = new List<string>
            {
                "The weekly schedule must contain each day of the week exactly once."
            };
            return errors;
        }

        return null;
    }

    /// <summary>Projects validated schedule DTO rows onto the persisted DaySchedule shape.</summary>
    /// <param name="schedule">Seven validated schedule rows.</param>
    /// <returns>The rows ordered by day of week, ready to be stored.</returns>
    private static List<DaySchedule> ToEntitySchedule(List<DayScheduleDto> schedule)
    {
        // Rows are stored in day order so a document read in Compass is immediately readable.
        return schedule
            .OrderBy(d => d.DayOfWeek)
            .Select(d => new DaySchedule
            {
                DayOfWeek = d.DayOfWeek,
                IsOpen = d.IsOpen,
                OpenTime = d.OpenTime,
                CloseTime = d.CloseTime
            })
            .ToList();
    }
}
