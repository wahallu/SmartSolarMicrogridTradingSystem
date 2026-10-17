/* =============================================================================
 * File        : ReservationService.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Services / Implementations
 * Description : Implements the reservation workflow and its booking rules.
 * Author      : IT22228062 - Akash
 * Created     : 2026-09-14
 * =============================================================================
 */

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartSolar.Api.Common;
using SmartSolar.Api.Configuration;
using SmartSolar.Api.Data;
using SmartSolar.Api.DTOs.Reservations;
using SmartSolar.Api.Models;
using SmartSolar.Api.Models.Enums;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Services.Implementations;

/// <summary>
/// MongoDB backed implementation of <see cref="IReservationService"/> - the single
/// authority on energy reservations and the only place the reservation rules run.
/// </summary>
public class ReservationService : IReservationService
{
    /// <summary>How many sequence numbers the reservation number probe will try before giving up.</summary>
    private const int MaxReservationNoAttempts = 20;

    /// <summary>How many future bookings the dashboard "upcoming" strip shows.</summary>
    private const int UpcomingLimit = 5;

    /// <summary>
    /// Serialiser settings for the QR payload. The naming policy is deliberately left
    /// null so the anonymous object's member names (v, t, r, n, s, e) are written to
///
/// In short: passing null keeps the anonymous object's original member names
/// (v, t, r, n, s, e) in the output.
    /// the JSON verbatim - the contract fixes those single letter names exactly.
    /// </summary>
    private static readonly JsonSerializerOptions QrPayloadJsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private readonly MongoDbContext _db;
    private readonly IQrCodeGenerator _qr;
    private readonly BusinessRuleSettings _rules;
    private readonly IClock _clock;
    private readonly ILogger<ReservationService> _logger;

    /// <summary>Captures the data context, the QR generator, the rule constants, the clock and the logger.</summary>
    /// <param name="db">Access to the four MongoDB collections.</param>
    /// <param name="qr">Issues QR tokens and renders the PNG shown on the prosumer's phone.</param>
    /// <param name="rules">Business rule constants bound from appsettings.json.</param>
    /// <param name="clock">Injected clock - every rule reads "now" from here so it stays testable.</param>
    /// <param name="logger">Diagnostics sink for the reservation life cycle.</param>
    public ReservationService(
        MongoDbContext db,
        IQrCodeGenerator qr,
        IOptions<BusinessRuleSettings> rules,
        IClock clock,
        ILogger<ReservationService> logger)
    {
        // Dependencies are stored once; the options value is unwrapped here so the rule code below stays readable.
        _db = db;
        _qr = qr;
        _rules = rules.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>R2 and R3 - hours of notice a change or cancellation needs (12 by configuration).</summary>
    private int NoticeHours => _rules.ModificationNoticeHours;

    /// <summary>R1 - how far ahead a booking may be scheduled (7 days by configuration).</summary>
    private int WindowDays => _rules.ReservationWindowDays;

    // =========================================================================
    // Queries
    // =========================================================================

    /// <summary>Paged, filtered reservation list for the staff search screens.</summary>
    /// <param name="query">NIC, node, status, date range and free text filters plus paging.</param>
    /// <param name="caller">The authenticated caller; a prosumer is confined to their own rows (rule R11).</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>One page of <see cref="ReservationDto"/>, newest slot first.</returns>
    public async Task<ServiceResult<PagedResult<ReservationDto>>> GetReservationsAsync(
        ReservationQuery query, CurrentUser caller, CancellationToken ct = default)
    {
        // ***RULE R11*** - a prosumer may never widen the search past their own NIC, whatever the query string says.
        if (caller.IsProsumer)
        {
            query.Nic = caller.Nic;
        }

        try
        {
            var builder = Builders<EnergyReservation>.Filter;
            var filter = builder.Empty;

            if (!string.IsNullOrWhiteSpace(query.Nic))
            {
                // Anchored and case insensitive so "199845600789v" still matches the stored "199845600789V".
                filter &= builder.Regex(r => r.ProsumerNic, AnchoredIgnoreCase(query.Nic));
            }

            if (!string.IsNullOrWhiteSpace(query.StationId))
            {
                // StationId is persisted as an ObjectId, so a malformed value can only ever match nothing.
                if (!ObjectId.TryParse(query.StationId.Trim(), out _))
                {
                    var empty = PagedResult<ReservationDto>.Create(
                        new List<ReservationDto>(), query.Page, query.PageSize, 0);
                    return ServiceResult<PagedResult<ReservationDto>>.Ok(empty, "Retrieved 0 of 0 reservation(s).");
                }

                filter &= builder.Eq(r => r.StationId, query.StationId.Trim());
            }

            if (query.Status.HasValue)
            {
                filter &= builder.Eq(r => r.Status, query.Status.Value);
            }

            if (query.FromDate.HasValue)
            {
                filter &= builder.Gte(r => r.ReservationStartUtc, query.FromDate.Value);
            }

            if (query.ToDate.HasValue)
            {
                filter &= builder.Lte(r => r.ReservationStartUtc, query.ToDate.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                // The term is escaped before it becomes a regex so a user typed "(" cannot break the query.
                var pattern = new BsonRegularExpression(Regex.Escape(query.Search.Trim()), "i");
                filter &= builder.Or(
                    builder.Regex(r => r.ReservationNo, pattern),
                    builder.Regex(r => r.ProsumerName, pattern),
                    builder.Regex(r => r.ProsumerNic, pattern),
                    builder.Regex(r => r.StationName, pattern),
                    builder.Regex(r => r.StationCode, pattern));
            }

            var totalCount = await _db.Reservations.CountDocumentsAsync(filter, cancellationToken: ct);

            // Skip/Limit paging keeps a long booking history off a phone's memory.
            var reservations = await _db.Reservations
                .Find(filter)
                .SortByDescending(r => r.ReservationStartUtc)
                .Skip(query.Skip)
                .Limit(query.PageSize)
                .ToListAsync(ct);

            var items = reservations.Select(Project).ToList();
            var page = PagedResult<ReservationDto>.Create(items, query.Page, query.PageSize, totalCount);

            return ServiceResult<PagedResult<ReservationDto>>.Ok(
                page, $"Retrieved {items.Count} of {totalCount} reservation(s).");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to read the reservation list.");
            return ServiceResult<PagedResult<ReservationDto>>.Error("Reservations could not be read at this time.");
        }
    }

    /// <summary>.</summary>
    /// <param name="id">Mongo ObjectId of the reservation, as a string.</param>
    /// <param name="caller">The authenticated caller.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The reservation, NotFound for an unknown id, or Forbidden for someone else's booking.</returns>
    public async Task<ServiceResult<ReservationDto>> GetByIdAsync(
        string id, CurrentUser caller, CancellationToken ct = default)
    {
        try
        {
            var reservation = await LoadByIdAsync(id, ct);
            if (reservation is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Reservation not found.");
            }

            // ***RULE R11*** - a prosumer may only ever read their own booking.
            if (!CanTouch(reservation, caller))
            {
                return ServiceResult<ReservationDto>.Forbidden(
                    "You may only view your own reservations.", ErrorCodes.Forbidden);
            }

            return ServiceResult<ReservationDto>.Ok(Project(reservation), "Reservation retrieved successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to read reservation {ReservationId}.", id);
            return ServiceResult<ReservationDto>.Error("The reservation could not be read at this time.");
        }
    }

    /// <summary>A prosumer's own bookings, sliced by the scope the mobile app asked for.</summary>
    /// <param name="scope">Current, Pending, History or All.</param>
    /// <param name="caller">The authenticated caller - must carry a NIC.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The matching bookings, ordered so the most relevant one is first.</returns>
    public async Task<ServiceResult<List<ReservationDto>>> GetMyReservationsAsync(
        ReservationScope scope, CurrentUser caller, CancellationToken ct = default)
    {
        // Only a prosumer account owns bookings, so a staff token has nothing personal to return here.
        if (string.IsNullOrWhiteSpace(caller.Nic))
        {
            return ServiceResult<List<ReservationDto>>.Forbidden(
                "Only a prosumer account has personal reservations.", ErrorCodes.Forbidden);
        }

        try
        {
            var now = _clock.UtcNow;
            var builder = Builders<EnergyReservation>.Filter;

            // ***RULE R11*** - the base filter is pinned to the caller's own NIC and is never relaxed.
            var baseFilter = builder.Regex(r => r.ProsumerNic, AnchoredIgnoreCase(caller.Nic));

            FilterDefinition<EnergyReservation> filter;
            bool ascending;

            switch (scope)
            {
                case ReservationScope.Current:
                    // "Current" means an approved reservation whose start time hasn't passed yet.
                    filter = baseFilter
                             & builder.Eq(r => r.Status, ReservationStatus.Approved)
                             & builder.Gt(r => r.ReservationStartUtc, now);
                    ascending = true;
                    break;

                case ReservationScope.Pending:
                    // Bookings still waiting for a staff decision.
                    filter = baseFilter & builder.Eq(r => r.Status, ReservationStatus.Pending);
                    ascending = true;
                    break;

                case ReservationScope.History:
                    // Anything finished by status, plus anything whose window has simply passed.
                    filter = baseFilter
                             & (builder.In(r => r.Status, new[]
                                {
                                    ReservationStatus.Completed,
                                    ReservationStatus.Cancelled,
                                    ReservationStatus.Rejected
                                })
                                | builder.Lt(r => r.ReservationEndUtc, now));
                    ascending = false;
                    break;

                default:
                    // All - every booking the prosumer has ever placed.
                    filter = baseFilter;
                    ascending = false;
                    break;
            }

            var find = _db.Reservations.Find(filter);
            var sorted = ascending
                ? find.SortBy(r => r.ReservationStartUtc)
                : find.SortByDescending(r => r.ReservationStartUtc);

            var reservations = await sorted.ToListAsync(ct);
            var items = reservations.Select(Project).ToList();

            return ServiceResult<List<ReservationDto>>.Ok(items, $"Retrieved {items.Count} reservation(s).");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to read the personal reservations of {Nic}.", caller.Nic);
            return ServiceResult<List<ReservationDto>>.Error("Your reservations could not be read at this time.");
        }
    }

    /// <summary>Full booking history for a NIC; a prosumer is forced onto their own (rule R11).</summary>
    /// <param name="nic">NIC to report on - ignored when the caller is a prosumer.</param>
    /// <param name="caller">The authenticated caller.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Every reservation held by that NIC, newest first.</returns>
    public async Task<ServiceResult<List<ReservationDto>>> GetHistoryAsync(
        string? nic, CurrentUser caller, CancellationToken ct = default)
    {
        // ***RULE R11*** - staff may inspect any NIC, a prosumer is silently pinned to their own.
        var targetNic = caller.IsProsumer ? caller.Nic : nic;

        if (string.IsNullOrWhiteSpace(targetNic))
        {
            // A prosumer token without a NIC cannot own history; a staff caller simply forgot the parameter.
            return caller.IsProsumer
                ? ServiceResult<List<ReservationDto>>.Forbidden(
                    "Only a prosumer account has personal reservations.", ErrorCodes.Forbidden)
                : ServiceResult<List<ReservationDto>>.Invalid("A NIC is required.");
        }

        try
        {
            var filter = Builders<EnergyReservation>.Filter.Regex(
                r => r.ProsumerNic, AnchoredIgnoreCase(targetNic));

            var reservations = await _db.Reservations
                .Find(filter)
                .SortByDescending(r => r.ReservationStartUtc)
                .ToListAsync(ct);

            var items = reservations.Select(Project).ToList();
            return ServiceResult<List<ReservationDto>>.Ok(
                items, $"Retrieved {items.Count} reservation(s) for NIC {targetNic}.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to read the reservation history of {Nic}.", targetNic);
            return ServiceResult<List<ReservationDto>>.Error("The booking history could not be read at this time.");
        }
    }

    /// <summary>Live dashboard counters, scoped to a node and/or a prosumer.</summary>
    /// <param name="stationId">Optional node scope - ignored for a prosumer caller.</param>
    /// <param name="nic">Optional prosumer scope - forced to the caller's own NIC for a prosumer.</param>
    /// <param name="caller">The authenticated caller; staff see the account counters too.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The fully populated <see cref="DashboardDto"/>.</returns>
    public async Task<ServiceResult<DashboardDto>> GetDashboardAsync(
        string? stationId, string? nic, CurrentUser caller, CancellationToken ct = default)
    {
        // ***RULE R11*** - a prosumer only ever sees their own numbers, so the node scope is dropped entirely
        // and the NIC scope is overwritten with the NIC on their token.
        if (caller.IsProsumer)
        {
            nic = caller.Nic;
            stationId = null;
        }

        try
        {
            var now = _clock.UtcNow;
            var todayUtc = now.Date;
            var tomorrowUtc = todayUtc.AddDays(1);

            var builder = Builders<EnergyReservation>.Filter;

            // Every counter below is this scope filter combined with one more condition, so the whole
            // dashboard is guaranteed to describe the same set of reservations.
            var baseFilter = builder.Empty;

            if (!string.IsNullOrWhiteSpace(nic))
            {
                baseFilter &= builder.Regex(r => r.ProsumerNic, AnchoredIgnoreCase(nic));
            }

            if (!string.IsNullOrWhiteSpace(stationId) && ObjectId.TryParse(stationId.Trim(), out _))
            {
                baseFilter &= builder.Eq(r => r.StationId, stationId.Trim());
            }

            var dto = new DashboardDto
            {
                PendingReservations = await _db.Reservations.CountDocumentsAsync(
                    baseFilter & builder.Eq(r => r.Status, ReservationStatus.Pending), cancellationToken: ct),

                // This is the "count of approved future reservations" the marking scheme names: status
                // Approved AND a start instant that has not arrived yet.
                ApprovedFutureReservations = await _db.Reservations.CountDocumentsAsync(
                    baseFilter
                    & builder.Eq(r => r.Status, ReservationStatus.Approved)
                    & builder.Gt(r => r.ReservationStartUtc, now), cancellationToken: ct),

                TodayReservations = await _db.Reservations.CountDocumentsAsync(
                    baseFilter
                    & builder.Gte(r => r.ReservationStartUtc, todayUtc)
                    & builder.Lt(r => r.ReservationStartUtc, tomorrowUtc), cancellationToken: ct),

                CompletedReservations = await _db.Reservations.CountDocumentsAsync(
                    baseFilter & builder.Eq(r => r.Status, ReservationStatus.Completed), cancellationToken: ct),

                CancelledReservations = await _db.Reservations.CountDocumentsAsync(
                    baseFilter & builder.Eq(r => r.Status, ReservationStatus.Cancelled), cancellationToken: ct),

                TotalReservations = await _db.Reservations.CountDocumentsAsync(
                    baseFilter, cancellationToken: ct),

                ActiveStations = await _db.Stations.CountDocumentsAsync(
                    Builders<SolarStationInfo>.Filter.Eq(s => s.Status, StationStatus.Active), cancellationToken: ct),

                TotalStations = await _db.Stations.CountDocumentsAsync(
                    Builders<SolarStationInfo>.Filter.Empty, cancellationToken: ct)
            };

            // The account counters are a back office / operator concern, so they stay zero for a prosumer.
            if (caller.IsStaff)
            {
                var userBuilder = Builders<User>.Filter;

                dto.PendingProsumerActivations = await _db.Users.CountDocumentsAsync(
                    userBuilder.Eq(u => u.Role, UserRole.Prosumer)
                    & userBuilder.Eq(u => u.Status, AccountStatus.Pending), cancellationToken: ct);

                dto.TotalProsumers = await _db.Users.CountDocumentsAsync(
                    userBuilder.Eq(u => u.Role, UserRole.Prosumer), cancellationToken: ct);
            }

            // Only the delivered energy field is fetched and the sum is done here in memory. Projecting a
            // single field keeps the payload tiny, and it avoids an aggregation pipeline that would be much
            // harder to explain at the viva than this three line loop.
            var completedFilter = baseFilter & builder.Eq(r => r.Status, ReservationStatus.Completed);
            var deliveredDocuments = await _db.Reservations
                .Find(completedFilter)
                .Project(Builders<EnergyReservation>.Projection
                    .Include(r => r.DeliveredKwh)
                    .Exclude(r => r.Id))
                .ToListAsync(ct);

            var totalEnergyKwh = 0.0;
            foreach (var document in deliveredDocuments)
            {
                // A completed booking always carries a figure, but a missing or non numeric value is skipped
                // rather than allowed to throw while a dashboard is being rendered.
                if (document.TryGetValue("deliveredKwh", out var value) && value.IsNumeric)
                {
                    totalEnergyKwh += value.ToDouble();
                }
            }

            dto.TotalEnergyKwh = Math.Round(totalEnergyKwh, 2);

            // The upcoming strip shows what the caller has to deal with next, soonest first.
            var upcoming = await _db.Reservations
                .Find(baseFilter
                      & builder.In(r => r.Status, new[] { ReservationStatus.Pending, ReservationStatus.Approved })
                      & builder.Gt(r => r.ReservationStartUtc, now))
                .SortBy(r => r.ReservationStartUtc)
                .Limit(UpcomingLimit)
                .ToListAsync(ct);

            dto.Upcoming = upcoming.Select(Project).ToList();

            return ServiceResult<DashboardDto>.Ok(dto, "Dashboard loaded.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to build the dashboard for {User}.", caller.Username);
            return ServiceResult<DashboardDto>.Error("The dashboard could not be loaded at this time.");
        }
    }

    // =========================================================================
    // Commands - the booking life cycle
    // =========================================================================

    /// <summary>Places a new booking - enforces rules R1, R8, R11, R12 and R13.</summary>
    /// <param name="request">Target NIC (staff only), node, slot and requested energy.</param>
    /// <param name="caller">The authenticated caller.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Created with the stored booking, or the rule violation that stopped it.</returns>
    public async Task<ServiceResult<ReservationDto>> CreateAsync(
        CreateReservationRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        try
        {
            // ***RULE R11*** - when a prosumer books, the NIC on the token always wins and any prosumerNic in
            // the request body is ignored outright. That is what stops one prosumer booking energy as another.
            string targetNic;
            if (caller.IsProsumer)
            {
                targetNic = (caller.Nic ?? string.Empty).Trim();
                if (targetNic.Length == 0)
                {
                    return ServiceResult<ReservationDto>.Forbidden(
                        "Your account does not carry a NIC, so it cannot hold reservations.", ErrorCodes.Forbidden);
                }
            }
            else
            {
                // Staff book on behalf of somebody, so they must name that somebody.
                targetNic = (request.ProsumerNic ?? string.Empty).Trim();
                if (targetNic.Length == 0)
                {
                    return ServiceResult<ReservationDto>.Invalid("A prosumer NIC is required.");
                }
            }

            // The owning account has to exist, be a prosumer, and be allowed to transact right now.
            var prosumer = await _db.Users
                .Find(Builders<User>.Filter.Regex(u => u.Nic, AnchoredIgnoreCase(targetNic)))
                .FirstOrDefaultAsync(ct);

            if (prosumer is null || prosumer.Role != UserRole.Prosumer)
            {
                return ServiceResult<ReservationDto>.NotFound($"No prosumer is registered with NIC {targetNic}.");
            }

            if (prosumer.Status == AccountStatus.Pending)
            {
                // R6 - a self registered account cannot transact until the back office activates it.
                return ServiceResult<ReservationDto>.Forbidden(
                    "This prosumer account is still awaiting back office activation and cannot book energy yet.",
                    ErrorCodes.AccountPending);
            }

            if (prosumer.Status == AccountStatus.Deactivated)
            {
                // R5 - a deactivated account is blocked until a Backoffice officer restores it.
                return ServiceResult<ReservationDto>.Forbidden(
                    "This prosumer account has been deactivated and cannot book energy.",
                    ErrorCodes.AccountDeactivated);
            }

            // The slot has to exist before anything can be checked against it.
            var slotId = (request.SlotId ?? string.Empty).Trim();
            if (!ObjectId.TryParse(slotId, out _))
            {
                return ServiceResult<ReservationDto>.NotFound("Booking slot not found.");
            }

            var slot = await _db.Slots
                .Find(Builders<EnergyBookingSlot>.Filter.Eq(s => s.Id, slotId))
                .FirstOrDefaultAsync(ct);

            if (slot is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Booking slot not found.");
            }

            // A slot belongs to exactly one node; a mismatched pair is a client bug, not a valid booking.
            var stationId = (request.StationId ?? string.Empty).Trim();
            if (!string.Equals(slot.StationId, stationId, StringComparison.Ordinal))
            {
                return ServiceResult<ReservationDto>.Invalid(
                    "The selected slot does not belong to the selected microgrid node.");
            }

            if (!ObjectId.TryParse(stationId, out _))
            {
                return ServiceResult<ReservationDto>.NotFound("Microgrid node not found.");
            }

            var station = await _db.Stations
                .Find(Builders<SolarStationInfo>.Filter.Eq(s => s.Id, stationId))
                .FirstOrDefaultAsync(ct);

            if (station is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Microgrid node not found.");
            }

            // ***RULE R13*** - the same prosumer may not hold two live bookings on one slot.
            //
            // ORDERING MATTERS: this runs BEFORE the R8 availability check on purpose. A slot
            // normally has MaxBookings = 1, so a prosumer's own second attempt would otherwise be
            // reported as SLOT_UNAVAILABLE ("this slot is full") when the truthful answer is
            // "you already booked this slot". Checking the duplicate first gives the caller the
            // accurate error code and a message they can act on.
            var duplicateCheck = await HasActiveReservationOnSlotAsync(prosumer.Nic!, slot.Id, null, ct);
            if (duplicateCheck)
            {
                return ServiceResult<ReservationDto>.Conflict(
                    "You already hold an active reservation for this slot.",
                    ErrorCodes.DuplicateReservation);
            }

            // The shared helper carries the node status check plus rules R1, R12, R8 and the energy cap, so
            // create and update can never drift apart on what counts as a bookable slot.
            var slotCheck = ValidateSlotForBooking(slot, station, request.EnergyKwh);
            if (!slotCheck.IsSuccess)
            {
                return AsReservationFailure(slotCheck);
            }

            var now = _clock.UtcNow;
            var reservation = new EnergyReservation
            {
                ReservationNo = await NextReservationNoAsync(ct),

                // Prosumer and node details are denormalised so a list screen needs no second query.
                ProsumerNic = prosumer.Nic!,
                ProsumerName = prosumer.FullName,
                ProsumerPhone = prosumer.Phone,
                StationId = station.Id,
                StationCode = station.StationCode,
                StationName = station.Name,
                SlotId = slot.Id,
                SlotCode = slot.SlotCode,
                ReservationStartUtc = slot.StartUtc,
                ReservationEndUtc = slot.EndUtc,
                TimeSemanticsVersion = BookingTime.CurrentSemanticsVersion,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                SlotType = slot.SlotType,
                EnergyKwh = request.EnergyKwh,

                // A new booking always starts Pending - staff approval is what issues the QR token.
                Status = ReservationStatus.Pending,
                OperatorNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = caller.Username,
                UpdatedBy = caller.Username
            };

            await _db.Reservations.InsertOneAsync(reservation, cancellationToken: ct);

            // The slot's booked count is claimed only once the document exists, so a failed insert can never
            // leave a slot looking fuller than it really is.
            await AdjustSlotCountAsync(slot.Id, 1, ct);

            _logger.LogInformation(
                "Reservation {ReservationNo} created for NIC {Nic} on slot {SlotCode} by {User}.",
                reservation.ReservationNo, reservation.ProsumerNic, reservation.SlotCode, caller.Username);

            return ServiceResult<ReservationDto>.Created(
                Project(reservation), "Reservation request created successfully. It is pending approval.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to create a reservation for {User}.", caller.Username);
            return ServiceResult<ReservationDto>.Error("The reservation could not be created at this time.");
        }
    }

    /// <summary>Changes a booking - enforces the 12 hour notice rule R2 and re-validates the new slot.</summary>
    /// <param name="id">Mongo identifier of the booking.</param>
    /// <param name="request">Optional new slot and/or new energy amount.</param>
    /// <param name="caller">The authenticated caller - owner or staff (rule R11).</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The updated booking, or the rule violation that stopped the change.</returns>
    public async Task<ServiceResult<ReservationDto>> UpdateAsync(
        string id, UpdateReservationRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        try
        {
            var reservation = await LoadByIdAsync(id, ct);
            if (reservation is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Reservation not found.");
            }

            // ***RULE R11*** - only the owning prosumer or a staff member may change a booking.
            if (!CanTouch(reservation, caller))
            {
                return ServiceResult<ReservationDto>.Forbidden(
                    "You may only change your own reservations.", ErrorCodes.Forbidden);
            }

            // A completed, cancelled or rejected booking is history and must stay exactly as it is.
            if (reservation.Status is not (ReservationStatus.Pending or ReservationStatus.Approved))
            {
                return ServiceResult<ReservationDto>.Invalid(
                    "Only pending or approved reservations can be updated.", ErrorCodes.ReservationBadState);
            }

            var now = _clock.UtcNow;

            // ***RULE R2*** - a booking may only be changed while at least the configured notice period
            // (12 hours) still separates now from the scheduled start instant.
            var hoursUntilStart = (reservation.ReservationStartUtc - now).TotalHours;
            if (hoursUntilStart < NoticeHours)
            {
                _logger.LogInformation(
                    "R2 blocked an update to {ReservationNo}: {Hours} hour(s) of notice remained.",
                    reservation.ReservationNo, Math.Round(hoursUntilStart, 2));

                return ServiceResult<ReservationDto>.Invalid(
                    $"Reservations can only be updated at least {NoticeHours} hours before the scheduled start time.",
                    ErrorCodes.ReservationUpdateNotice);
            }

            var updateBuilder = Builders<EnergyReservation>.Update;
            var updates = new List<UpdateDefinition<EnergyReservation>>
            {
                updateBuilder.Set(r => r.UpdatedAt, now),
                updateBuilder.Set(r => r.UpdatedBy, caller.Username)
            };

            var previousSlotId = reservation.SlotId;
            var requestedSlotId = (request.SlotId ?? string.Empty).Trim();
            var slotChanged = requestedSlotId.Length > 0
                              && !string.Equals(requestedSlotId, previousSlotId, StringComparison.Ordinal);

            EnergyBookingSlot? effectiveSlot = null;

            if (slotChanged)
            {
                if (!ObjectId.TryParse(requestedSlotId, out _))
                {
                    return ServiceResult<ReservationDto>.NotFound("Booking slot not found.");
                }

                var newSlot = await _db.Slots
                    .Find(Builders<EnergyBookingSlot>.Filter.Eq(s => s.Id, requestedSlotId))
                    .FirstOrDefaultAsync(ct);

                if (newSlot is null)
                {
                    return ServiceResult<ReservationDto>.NotFound("Booking slot not found.");
                }

                if (!ObjectId.TryParse(newSlot.StationId, out _))
                {
                    return ServiceResult<ReservationDto>.NotFound("Microgrid node not found.");
                }

                var newStation = await _db.Stations
                    .Find(Builders<SolarStationInfo>.Filter.Eq(s => s.Id, newSlot.StationId))
                    .FirstOrDefaultAsync(ct);

                if (newStation is null)
                {
                    return ServiceResult<ReservationDto>.NotFound("Microgrid node not found.");
                }

                // ***RULE R13*** - moving onto a slot the prosumer already holds would create the duplicate
                // the rule forbids, so this booking itself is excluded and everything else is rejected.
                //
                // Checked BEFORE R8 for the same reason as in CreateAsync: on a single booking slot the
                // duplicate would otherwise surface as the misleading "slot is full" error.
                var duplicate = await HasActiveReservationOnSlotAsync(
                    reservation.ProsumerNic, newSlot.Id, reservation.Id, ct);

                if (duplicate)
                {
                    return ServiceResult<ReservationDto>.Conflict(
                        "You already hold an active reservation for this slot.",
                        ErrorCodes.DuplicateReservation);
                }

                // The new slot faces exactly the same gate a brand new booking faces: node active, R1, R12,
                // R8 and the energy cap - checked through the one shared helper.
                var energyForCheck = request.EnergyKwh ?? reservation.EnergyKwh;
                var slotCheck = ValidateSlotForBooking(newSlot, newStation, energyForCheck);
                if (!slotCheck.IsSuccess)
                {
                    return AsReservationFailure(slotCheck);
                }

                // Capacity is handed back to the old slot and claimed on the new one only now that every
                // rule has passed, so a rejected update never disturbs either count.
                await AdjustSlotCountAsync(previousSlotId, -1, ct);
                await AdjustSlotCountAsync(newSlot.Id, 1, ct);

                // The in memory entity is moved onto the new slot as well, so the QR payload rebuilt below
                // describes the new booking rather than the old one.
                reservation.SlotId = newSlot.Id;
                reservation.SlotCode = newSlot.SlotCode;
                reservation.StationId = newSlot.StationId;
                reservation.StationCode = newSlot.StationCode;
                reservation.StationName = newStation.Name;
                reservation.ReservationStartUtc = newSlot.StartUtc;
                reservation.ReservationEndUtc = newSlot.EndUtc;
                reservation.StartTime = newSlot.StartTime;
                reservation.EndTime = newSlot.EndTime;
                reservation.SlotType = newSlot.SlotType;

                updates.Add(updateBuilder.Set(r => r.SlotId, newSlot.Id));
                updates.Add(updateBuilder.Set(r => r.SlotCode, newSlot.SlotCode));
                updates.Add(updateBuilder.Set(r => r.StationId, newSlot.StationId));
                updates.Add(updateBuilder.Set(r => r.StationCode, newSlot.StationCode));
                updates.Add(updateBuilder.Set(r => r.StationName, newStation.Name));
                updates.Add(updateBuilder.Set(r => r.ReservationStartUtc, newSlot.StartUtc));
                updates.Add(updateBuilder.Set(r => r.ReservationEndUtc, newSlot.EndUtc));
                updates.Add(updateBuilder.Set(
                    r => r.TimeSemanticsVersion, BookingTime.CurrentSemanticsVersion));
                updates.Add(updateBuilder.Set(r => r.StartTime, newSlot.StartTime));
                updates.Add(updateBuilder.Set(r => r.EndTime, newSlot.EndTime));
                updates.Add(updateBuilder.Set(r => r.SlotType, newSlot.SlotType));

                effectiveSlot = newSlot;
            }

            if (request.EnergyKwh.HasValue)
            {
                // The cap belongs to whichever slot the booking will actually sit on once this call finishes.
                if (effectiveSlot is null && ObjectId.TryParse(reservation.SlotId, out _))
                {
                    effectiveSlot = await _db.Slots
                        .Find(Builders<EnergyBookingSlot>.Filter.Eq(s => s.Id, reservation.SlotId))
                        .FirstOrDefaultAsync(ct);
                }

                if (effectiveSlot is not null && request.EnergyKwh.Value > effectiveSlot.EnergyCapacityKwh)
                {
                    return ServiceResult<ReservationDto>.Invalid(
                        $"The selected slot can trade at most {FormatKwh(effectiveSlot.EnergyCapacityKwh)} kWh.");
                }

                reservation.EnergyKwh = request.EnergyKwh.Value;
                updates.Add(updateBuilder.Set(r => r.EnergyKwh, request.EnergyKwh.Value));
            }

            if (reservation.Status == ReservationStatus.Approved)
            {
                // The booking has already been approved, so a QR code is out in the world on the prosumer's
                // phone. Issuing a fresh token here retires that old image: a code screenshotted before the
                // change can never be replayed against the new slot, because the token it carries no longer
                // matches any reservation in the database.
                reservation.QrToken = _qr.CreateToken();
                reservation.QrPayload = BuildQrPayload(reservation);
                reservation.QrIssuedAt = now;

                updates.Add(updateBuilder.Set(r => r.QrToken, reservation.QrToken));
                updates.Add(updateBuilder.Set(r => r.QrPayload, reservation.QrPayload));
                updates.Add(updateBuilder.Set(r => r.QrIssuedAt, reservation.QrIssuedAt));

                _logger.LogInformation(
                    "QR token re-issued for {ReservationNo} after an update by {User}.",
                    reservation.ReservationNo, caller.Username);
            }

            var updated = await _db.Reservations.FindOneAndUpdateAsync(
                Builders<EnergyReservation>.Filter.Eq(r => r.Id, reservation.Id),
                updateBuilder.Combine(updates),
                new FindOneAndUpdateOptions<EnergyReservation> { ReturnDocument = ReturnDocument.After },
                ct);

            if (updated is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Reservation not found.");
            }

            _logger.LogInformation(
                "Reservation {ReservationNo} updated by {User}.", updated.ReservationNo, caller.Username);

            return ServiceResult<ReservationDto>.Ok(Project(updated), "Reservation updated successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to update reservation {ReservationId}.", id);
            return ServiceResult<ReservationDto>.Error("The reservation could not be updated at this time.");
        }
    }

    /// <summary>Cancels a booking - enforces the 12 hour notice rule R3.</summary>
    /// <param name="id">Mongo identifier of the booking.</param>
    /// <param name="request">Optional free text cancellation reason.</param>
    /// <param name="caller">The authenticated caller - owner or staff (rule R11).</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The cancelled booking, or the rule violation that stopped it.</returns>
    public async Task<ServiceResult<ReservationDto>> CancelAsync(
        string id, CancelReservationRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        try
        {
            var reservation = await LoadByIdAsync(id, ct);
            if (reservation is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Reservation not found.");
            }

            // ***RULE R11*** - only the owning prosumer or a staff member may cancel a booking.
            if (!CanTouch(reservation, caller))
            {
                return ServiceResult<ReservationDto>.Forbidden(
                    "You may only cancel your own reservations.", ErrorCodes.Forbidden);
            }

            if (reservation.Status is not (ReservationStatus.Pending or ReservationStatus.Approved))
            {
                return ServiceResult<ReservationDto>.Invalid(
                    "Only pending or approved reservations can be cancelled.", ErrorCodes.ReservationBadState);
            }

            var now = _clock.UtcNow;

            // ***RULE R3*** - the same 12 hour notice the update rule uses, so an operator is never left with
            // a slot that empties minutes before the prosumer was due to arrive.
            var hoursUntilStart = (reservation.ReservationStartUtc - now).TotalHours;
            if (hoursUntilStart < NoticeHours)
            {
                _logger.LogInformation(
                    "R3 blocked a cancellation of {ReservationNo}: {Hours} hour(s) of notice remained.",
                    reservation.ReservationNo, Math.Round(hoursUntilStart, 2));

                return ServiceResult<ReservationDto>.Invalid(
                    $"Reservations can only be cancelled at least {NoticeHours} hours before the scheduled start time.",
                    ErrorCodes.ReservationCancelNotice);
            }

            var update = Builders<EnergyReservation>.Update
                .Set(r => r.Status, ReservationStatus.Cancelled)
                .Set(r => r.CancelledBy, caller.Username)
                .Set(r => r.CancelledAt, now)
                .Set(r => r.CancelReason, string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim())
                .Set(r => r.UpdatedAt, now)
                .Set(r => r.UpdatedBy, caller.Username)

                // The QR material is removed so a cancelled booking can never be scanned into an energy
                // transfer. Unset rather than Set(null) is used deliberately: qrToken carries a unique sparse
                // index, and a sparse index skips a MISSING field but still indexes an explicit null - so
                // storing null would make the second cancelled booking collide with the first.
                .Unset(r => r.QrToken)
                .Unset(r => r.QrPayload);

            var updated = await _db.Reservations.FindOneAndUpdateAsync(
                Builders<EnergyReservation>.Filter.Eq(r => r.Id, reservation.Id),
                update,
                new FindOneAndUpdateOptions<EnergyReservation> { ReturnDocument = ReturnDocument.After },
                ct);

            if (updated is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Reservation not found.");
            }

            // The slot capacity goes back on sale now that nobody is coming to use it.
            await AdjustSlotCountAsync(updated.SlotId, -1, ct);

            _logger.LogInformation(
                "Reservation {ReservationNo} cancelled by {User}.", updated.ReservationNo, caller.Username);

            return ServiceResult<ReservationDto>.Ok(Project(updated), "Reservation cancelled successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to cancel reservation {ReservationId}.", id);
            return ServiceResult<ReservationDto>.Error("The reservation could not be cancelled at this time.");
        }
    }

    /// <summary>Approves a pending booking and issues its QR token - staff only.</summary>
    /// <param name="id">Mongo identifier of the booking.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The approved booking carrying its freshly issued token.</returns>
    public async Task<ServiceResult<ReservationDto>> ApproveAsync(
        string id, CurrentUser caller, CancellationToken ct = default)
    {
        // Approval is the moment a booking becomes a commitment for the node, so it is a staff decision.
        if (!caller.IsStaff)
        {
            return ServiceResult<ReservationDto>.Forbidden(
                "Only a Backoffice officer or a Grid Operator may approve a reservation.", ErrorCodes.Forbidden);
        }

        try
        {
            var reservation = await LoadByIdAsync(id, ct);
            if (reservation is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Reservation not found.");
            }

            if (reservation.Status != ReservationStatus.Pending)
            {
                return ServiceResult<ReservationDto>.Invalid(
                    $"Only a pending reservation can be approved; this one is {reservation.Status}.",
                    ErrorCodes.ReservationBadState);
            }

            var now = _clock.UtcNow;

            // The token is the only thing the field scanner is trusted with, so it is generated here and the
            // payload is built around it once the booking's own details are final.
            reservation.QrToken = _qr.CreateToken();
            reservation.QrPayload = BuildQrPayload(reservation);
            reservation.QrIssuedAt = now;

            var update = Builders<EnergyReservation>.Update
                .Set(r => r.Status, ReservationStatus.Approved)
                .Set(r => r.ApprovedBy, caller.Username)
                .Set(r => r.ApprovedAt, now)
                .Set(r => r.QrToken, reservation.QrToken)
                .Set(r => r.QrPayload, reservation.QrPayload)
                .Set(r => r.QrIssuedAt, reservation.QrIssuedAt)
                .Set(r => r.UpdatedAt, now)
                .Set(r => r.UpdatedBy, caller.Username);

            var updated = await _db.Reservations.FindOneAndUpdateAsync(
                Builders<EnergyReservation>.Filter.Eq(r => r.Id, reservation.Id),
                update,
                new FindOneAndUpdateOptions<EnergyReservation> { ReturnDocument = ReturnDocument.After },
                ct);

            if (updated is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Reservation not found.");
            }

            _logger.LogInformation(
                "Reservation {ReservationNo} approved by {User}; QR token issued.",
                updated.ReservationNo, caller.Username);

            return ServiceResult<ReservationDto>.Ok(
                Project(updated), "Reservation approved. A QR code has been issued to the prosumer.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to approve reservation {ReservationId}.", id);
            return ServiceResult<ReservationDto>.Error("The reservation could not be approved at this time.");
        }
    }

    /// <summary>Rejects a pending booking with a reason - staff only.</summary>
    /// <param name="id">Mongo identifier of the booking.</param>
    /// <param name="request">The mandatory rejection reason shown to the prosumer.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The rejected booking.</returns>
    public async Task<ServiceResult<ReservationDto>> RejectAsync(
        string id, RejectReservationRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // Refusing a booking is a staff decision for exactly the same reason approving one is.
        if (!caller.IsStaff)
        {
            return ServiceResult<ReservationDto>.Forbidden(
                "Only a Backoffice officer or a Grid Operator may reject a reservation.", ErrorCodes.Forbidden);
        }

        try
        {
            var reservation = await LoadByIdAsync(id, ct);
            if (reservation is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Reservation not found.");
            }

            if (reservation.Status != ReservationStatus.Pending)
            {
                return ServiceResult<ReservationDto>.Invalid(
                    $"Only a pending reservation can be rejected; this one is {reservation.Status}.",
                    ErrorCodes.ReservationBadState);
            }

            var now = _clock.UtcNow;

            var update = Builders<EnergyReservation>.Update
                .Set(r => r.Status, ReservationStatus.Rejected)
                .Set(r => r.RejectedReason, (request.Reason ?? string.Empty).Trim())
                .Set(r => r.UpdatedAt, now)
                .Set(r => r.UpdatedBy, caller.Username);

            var updated = await _db.Reservations.FindOneAndUpdateAsync(
                Builders<EnergyReservation>.Filter.Eq(r => r.Id, reservation.Id),
                update,
                new FindOneAndUpdateOptions<EnergyReservation> { ReturnDocument = ReturnDocument.After },
                ct);

            if (updated is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Reservation not found.");
            }

            // The capacity this booking was holding is handed straight back so somebody else can take it.
            await AdjustSlotCountAsync(updated.SlotId, -1, ct);

            _logger.LogInformation(
                "Reservation {ReservationNo} rejected by {User}.", updated.ReservationNo, caller.Username);

            return ServiceResult<ReservationDto>.Ok(Project(updated), "Reservation rejected successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to reject reservation {ReservationId}.", id);
            return ServiceResult<ReservationDto>.Error("The reservation could not be rejected at this time.");
        }
    }

    /// <summary>Returns the QR payload and a rendered PNG for an approved booking.</summary>
    /// <param name="id">Mongo identifier of the booking.</param>
    /// <param name="caller">The authenticated caller - owner or staff (rule R11).</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The QR material, or the reason the code is not available.</returns>
    public async Task<ServiceResult<QrCodeDto>> GetQrCodeAsync(
        string id, CurrentUser caller, CancellationToken ct = default)
    {
        try
        {
            var reservation = await LoadByIdAsync(id, ct);
            if (reservation is null)
            {
                return ServiceResult<QrCodeDto>.NotFound("Reservation not found.");
            }

            // ***RULE R11*** - a QR code is a bearer credential, so only its owner or staff may fetch it.
            if (!CanTouch(reservation, caller))
            {
                return ServiceResult<QrCodeDto>.Forbidden(
                    "You may only view the QR code of your own reservations.", ErrorCodes.Forbidden);
            }

            if (reservation.Status != ReservationStatus.Approved)
            {
                return ServiceResult<QrCodeDto>.Invalid(
                    "A QR code is only available once the reservation has been approved.",
                    ErrorCodes.QrNotApproved);
            }

            if (string.IsNullOrWhiteSpace(reservation.QrToken))
            {
                // Defensive repair. An approved booking should always carry a token, but if a document was
                // ever written by an older build or edited by hand, the prosumer would be stranded with an
                // approved booking and no way to prove it at the node. Issuing and persisting one here costs
                // a single write and keeps the field workflow moving.
                var issuedAt = _clock.UtcNow;
                reservation.QrToken = _qr.CreateToken();
                reservation.QrPayload = BuildQrPayload(reservation);
                reservation.QrIssuedAt = issuedAt;

                await _db.Reservations.UpdateOneAsync(
                    Builders<EnergyReservation>.Filter.Eq(r => r.Id, reservation.Id),
                    Builders<EnergyReservation>.Update
                        .Set(r => r.QrToken, reservation.QrToken)
                        .Set(r => r.QrPayload, reservation.QrPayload)
                        .Set(r => r.QrIssuedAt, reservation.QrIssuedAt)
                        .Set(r => r.UpdatedAt, issuedAt),
                    cancellationToken: ct);

                _logger.LogWarning(
                    "Approved reservation {ReservationNo} had no QR token; a replacement was issued.",
                    reservation.ReservationNo);
            }

            // The payload is rebuilt when it is missing so the image and the stored string always agree.
            var payload = string.IsNullOrWhiteSpace(reservation.QrPayload)
                ? BuildQrPayload(reservation)
                : reservation.QrPayload;

            var dto = new QrCodeDto
            {
                ReservationNo = reservation.ReservationNo,
                QrToken = reservation.QrToken!,
                QrPayload = payload,
                QrImageBase64 = _qr.GeneratePngBase64(payload, 10),
                IssuedAt = reservation.QrIssuedAt
            };

            return ServiceResult<QrCodeDto>.Ok(dto, "QR code retrieved successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to read the QR code of reservation {ReservationId}.", id);
            return ServiceResult<QrCodeDto>.Error("The QR code could not be read at this time.");
        }
    }

    /// <summary>Validates a scanned token against the server - rule R9, staff only.</summary>
    /// <param name="request">The token, or the whole payload string, read out of the scanned image.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The booking behind the token, or the R9 failure that rejected it.</returns>
    public async Task<ServiceResult<ReservationDto>> VerifyQrAsync(
        VerifyQrRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // Scanning happens at the node, so only a grid operator or the back office may verify a code.
        if (!caller.IsStaff)
        {
            return ServiceResult<ReservationDto>.Forbidden(
                "Only a Backoffice officer or a Grid Operator may verify a QR code.", ErrorCodes.Forbidden);
        }

        try
        {
            // ***RULE R9*** lives once, in the shared resolver, so verify and complete can never disagree.
            var resolved = await ResolveApprovedByTokenAsync(request.QrToken, ct);
            if (!resolved.IsSuccess)
            {
                return AsReservationFailure(resolved);
            }

            var reservation = resolved.Value!;

            _logger.LogInformation(
                "QR code for {ReservationNo} verified by {User}.", reservation.ReservationNo, caller.Username);

            return ServiceResult<ReservationDto>.Ok(Project(reservation), "QR code verified successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to verify a QR code for {User}.", caller.Username);
            return ServiceResult<ReservationDto>.Error("The QR code could not be verified at this time.");
        }
    }

    /// <summary>Finalises the energy transfer after a successful scan - staff only.</summary>
    /// <param name="request">The scanned token, the delivered kWh and optional operator notes.</param>
    /// <param name="caller">The authenticated caller, whose role gates the operation.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The completed booking, or the R9 failure that rejected the scan.</returns>
    public async Task<ServiceResult<ReservationDto>> CompleteAsync(
        CompleteReservationRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // Completing a transfer records what physically happened at the node, so it is staff only.
        if (!caller.IsStaff)
        {
            return ServiceResult<ReservationDto>.Forbidden(
                "Only a Backoffice officer or a Grid Operator may finalise an energy transfer.",
                ErrorCodes.Forbidden);
        }

        try
        {
            // ***RULE R9*** - the very same resolver the verify endpoint uses, so a token that would fail
            // verification can never be completed either. The rule is written once and reused.
            var resolved = await ResolveApprovedByTokenAsync(request.QrToken, ct);
            if (!resolved.IsSuccess)
            {
                return AsReservationFailure(resolved);
            }

            var reservation = resolved.Value!;
            var now = _clock.UtcNow;

            var update = Builders<EnergyReservation>.Update
                .Set(r => r.Status, ReservationStatus.Completed)
                .Set(r => r.DeliveredKwh, request.DeliveredKwh)
                .Set(r => r.OperatorNotes, string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim())
                .Set(r => r.CompletedBy, caller.Username)
                .Set(r => r.CompletedAt, now)
                .Set(r => r.UpdatedAt, now)
                .Set(r => r.UpdatedBy, caller.Username)

                // The token is retired the instant the transfer is recorded, which prevents replay: a
                // photograph of the same QR code scanned a second time now resolves to nothing at all.
                // Unset is used rather than Set(null) because the unique sparse index on qrToken skips a
                // missing field but would still index an explicit null, colliding on the next completion.
                .Unset(r => r.QrToken)
                .Unset(r => r.QrPayload);

            var updated = await _db.Reservations.FindOneAndUpdateAsync(
                Builders<EnergyReservation>.Filter.Eq(r => r.Id, reservation.Id),
                update,
                new FindOneAndUpdateOptions<EnergyReservation> { ReturnDocument = ReturnDocument.After },
                ct);

            if (updated is null)
            {
                return ServiceResult<ReservationDto>.NotFound("Reservation not found.");
            }

            _logger.LogInformation(
                "Reservation {ReservationNo} completed by {User} with {DeliveredKwh} kWh delivered.",
                updated.ReservationNo, caller.Username, request.DeliveredKwh);

            return ServiceResult<ReservationDto>.Ok(Project(updated), "Energy transfer finalised successfully.");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Failed to complete an energy transfer for {User}.", caller.Username);
            return ServiceResult<ReservationDto>.Error("The energy transfer could not be finalised at this time.");
        }
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>Projects an entity to its wire DTO, evaluating rules R2 and R3 on the way out.</summary>
    /// <param name="r">The reservation to project.</param>
    /// <returns>The DTO, with CanModify and CanCancel already decided by the server.</returns>
    private ReservationDto Project(EnergyReservation r)
    {
        // The clock and the notice period are supplied here so no client ever re-implements the 12 hour rule.
        return r.ToDto(_clock.UtcNow, NoticeHours);
    }

    /// <summary>Loads one reservation by id, answering null for a malformed or unknown identifier.</summary>
    /// <param name="id">Mongo ObjectId of the reservation, as a string.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The stored document, or null when it does not exist.</returns>
    private async Task<EnergyReservation?> LoadByIdAsync(string id, CancellationToken ct)
    {
        // A malformed id is answered as "not found" rather than being handed to the driver, which would throw.
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        return await _db.Reservations
            .Find(Builders<EnergyReservation>.Filter.Eq(r => r.Id, id))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Allocates the next reservation number of the day, e.g. "RSV-20260904-0001".</summary>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>A reservation number that no stored document is currently using.</returns>
    private async Task<string> NextReservationNoAsync(CancellationToken ct)
    {
        // Numbering restarts every day, so the count of documents created since UTC midnight is the seed.
        var now = _clock.UtcNow;
        var todayUtc = now.Date;
        var datePart = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var todayCount = await _db.Reservations.CountDocumentsAsync(
            Builders<EnergyReservation>.Filter.Gte(r => r.CreatedAt, todayUtc), cancellationToken: ct);

        var sequence = (int)todayCount + 1;

        // The probe covers a race between two concurrent creates: both can read the same count and derive the
        // same number, so each candidate is tested against the collection and the loser simply steps up to the
        // next free sequence rather than failing on the unique index.
        for (var attempt = 0; attempt < MaxReservationNoAttempts; attempt++)
        {
            var candidate = $"RSV-{datePart}-{sequence:0000}";

            var taken = await _db.Reservations.CountDocumentsAsync(
                Builders<EnergyReservation>.Filter.Eq(r => r.ReservationNo, candidate), cancellationToken: ct);

            if (taken == 0)
            {
                return candidate;
            }

            sequence++;
        }

        // Twenty collisions in a row means something very unusual is happening; a random suffix guarantees the
        // booking can still be saved instead of failing the prosumer outright.
        var suffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        _logger.LogWarning("Reservation number probe exhausted for {DatePart}; falling back to a random suffix.", datePart);
        return $"RSV-{datePart}-{sequence:0000}-{suffix}";
    }

    /// <summary>Rule R11 - decides whether a caller is allowed to see or change a booking.</summary>
    /// <param name="r">The booking being accessed.</param>
    /// <param name="caller">The authenticated caller.</param>
    /// <returns>True for any staff member, or for the prosumer whose NIC owns the booking.</returns>
    private static bool CanTouch(EnergyReservation r, CurrentUser caller)
    {
        // ***RULE R11*** - staff see everything; a prosumer is matched on NIC, case insensitively so that a
        // token carrying "199845600789v" still owns a booking stored as "199845600789V".
        return caller.IsStaff
               || (caller.IsProsumer
                   && string.Equals(caller.Nic, r.ProsumerNic, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Moves a slot's booked count and re-derives its availability status.</summary>
    /// <param name="slotId">Mongo identifier of the slot to adjust.</param>
    /// <param name="delta">+1 when capacity is claimed, -1 when it is handed back.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    private async Task AdjustSlotCountAsync(string slotId, int delta, CancellationToken ct)
    {
        // A slot that cannot be addressed cannot be adjusted; answering quietly keeps the caller's flow intact.
        if (!ObjectId.TryParse(slotId, out _))
        {
            return;
        }

        var idFilter = Builders<EnergyBookingSlot>.Filter.Eq(s => s.Id, slotId);
        var now = _clock.UtcNow;

        // $inc is applied atomically by the MongoDB server itself, so two concurrent bookings can never both
        // read a stale count and write the same new value - one increment cannot overwrite the other. Reading
        // the count into C#, adding one and writing it back would lose a booking under exactly that race.
        await _db.Slots.UpdateOneAsync(
            idFilter,
            Builders<EnergyBookingSlot>.Update
                .Inc(s => s.BookedCount, delta)
                .Set(s => s.UpdatedAt, now),
            cancellationToken: ct);

        // The status is derived from the count that the server actually stored, not from the one this process
        // predicted, so a concurrent adjustment is reflected correctly.
        var slot = await _db.Slots.Find(idFilter).FirstOrDefaultAsync(ct);
        if (slot is null)
        {
            return;
        }

        var bookedCount = slot.BookedCount;
        var update = Builders<EnergyBookingSlot>.Update.Set(s => s.UpdatedAt, now);

        if (bookedCount < 0)
        {
            // A negative count would be nonsense on a screen, so it is clamped back to zero if it ever occurs.
            bookedCount = 0;
            update = update.Set(s => s.BookedCount, 0);
        }

        // A Blocked slot was taken out of service by an operator and must stay blocked whatever the count says.
        if (slot.Status != SlotStatus.Blocked)
        {
            var status = bookedCount >= slot.MaxBookings ? SlotStatus.Full : SlotStatus.Available;
            update = update.Set(s => s.Status, status);
        }

        await _db.Slots.UpdateOneAsync(idFilter, update, cancellationToken: ct);
    }

    /// <summary>Builds the exact JSON string that is encoded into the QR image.</summary>
    /// <param name="r">The approved reservation the code represents.</param>
    /// <returns>The payload string, matching section 9 of the API contract.</returns>
    private static string BuildQrPayload(EnergyReservation r)
    {
        // System.Text.Json serialises an anonymous object here rather than the string being concatenated by
        // hand, so quoting and escaping are always correct even if a prosumer name ever reaches this payload.
        // The member names v, t, r, n, s and e are written verbatim because the serialiser options above set
        // no naming policy - the contract fixes those single letter keys.
        var payload = new
        {
            v = 1,
            t = r.QrToken,
            r = r.ReservationNo,
            n = r.ProsumerNic,
            s = r.StationCode,
            e = r.ReservationStartUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        return JsonSerializer.Serialize(payload, QrPayloadJsonOptions);
    }

    /// <summary>Rule R9 - resolves a scanned token to an approved booking inside its scan window.</summary>
    /// <param name="rawToken">Either the bare token or the whole JSON payload read from the image.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>The booking behind the token, or the specific R9 failure that rejected it.</returns>
    private async Task<ServiceResult<EnergyReservation>> ResolveApprovedByTokenAsync(
        string rawToken, CancellationToken ct)
    {
        var token = (rawToken ?? string.Empty).Trim();

        // A scanner may hand over the entire payload string instead of just the token, so a JSON looking
        // input is parsed and its "t" member is taken.
        //
        // IMPORTANT: the server trusts ONLY the token. Every other fact about this booking - the prosumer,
        // the node, the status, the times - is re-read from the database below. Nothing that was printed in
        // the scanned payload is believed, because anybody can generate a QR image containing any text they
        // like. The token is the sole credential; the database is the sole authority.
        if (token.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(token);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("t", out var tokenElement)
                    && tokenElement.ValueKind == JsonValueKind.String)
                {
                    token = (tokenElement.GetString() ?? string.Empty).Trim();
                }
            }
            catch (JsonException)
            {
                // Not valid JSON after all - fall back to treating the whole string as the token itself.
                token = (rawToken ?? string.Empty).Trim();
            }
        }

        if (token.Length == 0)
        {
            return ServiceResult<EnergyReservation>.Invalid(
                "This QR code is not recognised.", ErrorCodes.QrInvalid);
        }

        var reservation = await _db.Reservations
            .Find(Builders<EnergyReservation>.Filter.Eq(r => r.QrToken, token))
            .FirstOrDefaultAsync(ct);

        // No document carries this token: either it was never issued, or it has already been retired by a
        // completion or a cancellation.
        if (reservation is null)
        {
            return ServiceResult<EnergyReservation>.Invalid(
                "This QR code is not recognised.", ErrorCodes.QrInvalid);
        }

        // R9 part two - the booking must currently be Approved. The message names the ACTUAL status so the
        // operator on site understands what they are looking at instead of retrying the scan.
        if (reservation.Status != ReservationStatus.Approved)
        {
            var message = reservation.Status switch
            {
                ReservationStatus.Completed => "This reservation has already been completed.",
                ReservationStatus.Cancelled => "This reservation has been cancelled.",
                ReservationStatus.Rejected => "This reservation was rejected.",
                ReservationStatus.Pending => "This reservation has not been approved yet.",
                _ => $"This reservation is not approved; its current status is {reservation.Status}."
            };

            return ServiceResult<EnergyReservation>.Invalid(message, ErrorCodes.QrNotApproved);
        }

        // R9 part three - the scan must fall inside the booked window plus the configured grace either side,
        // so a code cannot be used days early or long after the slot has passed.
        var now = _clock.UtcNow;
        var windowOpensUtc = reservation.ReservationStartUtc.AddHours(-_rules.QrGraceHoursBefore);
        var windowClosesUtc = reservation.ReservationEndUtc.AddHours(_rules.QrGraceHoursAfter);

        if (now < windowOpensUtc || now > windowClosesUtc)
        {
            return ServiceResult<EnergyReservation>.Invalid(
                "This QR code is outside its valid scan window. It can only be used between " +
                $"{BookingTime.FormatLocal(windowOpensUtc)} and {BookingTime.FormatLocal(windowClosesUtc)}.",
                ErrorCodes.QrOutOfWindow);
        }

        return ServiceResult<EnergyReservation>.Ok(reservation, "QR code resolved successfully.");
    }

    /// <summary>
    /// The single gate a slot must pass before a booking may sit on it. Both CreateAsync and
    /// UpdateAsync call this, so a slot change is held to exactly the same standard as a brand
    /// new booking and the two paths can never drift apart.
    /// </summary>
    /// <param name="slot">The slot being booked.</param>
    /// <param name="station">The node that owns the slot.</param>
    /// <param name="energyKwh">The energy the caller wants to trade in that window.</param>
    /// <returns>Ok when the slot may be booked, otherwise the rule violation that stops it.</returns>
    private ServiceResult<bool> ValidateSlotForBooking(
        EnergyBookingSlot slot, SolarStationInfo station, double energyKwh)
    {
        // A node that has been taken out of service cannot trade energy, whatever its slots still say.
        if (station.Status != StationStatus.Active)
        {
            return ServiceResult<bool>.Invalid(
                "The selected microgrid node is not currently in service.", ErrorCodes.StationInactive);
        }

        var now = _clock.UtcNow;

        // ***RULE R1*** - the booking must start in the future and no more than the configured window
        // (7 days) from now. Both halves are checked together so the message can explain the whole rule.
        if (slot.StartUtc <= now || slot.StartUtc > now.AddDays(WindowDays))
        {
            return ServiceResult<bool>.Invalid(
                $"Reservations must be scheduled within the next {WindowDays} days and cannot be in the past.",
                ErrorCodes.ReservationWindow);
        }

        // ***RULE R12*** - the node publishes a seven day schedule, and a day marked closed cannot be booked.
        // DayOfWeek is 0 = Sunday, which is exactly how the schedule rows are numbered.
        var dayIndex = (int)slot.StartUtc.DayOfWeek;
        var daySchedule = station.WeeklySchedule.FirstOrDefault(d => d.DayOfWeek == dayIndex);

        if (daySchedule is not null && !daySchedule.IsOpen)
        {
            return ServiceResult<bool>.Invalid(
                $"The microgrid node {station.StationCode} is closed on {DomainMapper.DayName(dayIndex)}. " +
                "Please choose a slot on a day the node is open.",
                ErrorCodes.StationClosed);
        }

        // ***RULE R8*** - the slot must still be on sale and must still have room. Blocked and Full slots and
        // any slot already at its maximum are all refused as a conflict rather than a validation error,
        // because the request was well formed - somebody simply got there first.
        if (slot.Status != SlotStatus.Available || slot.BookedCount >= slot.MaxBookings)
        {
            var reason = slot.Status switch
            {
                SlotStatus.Blocked => "has been blocked by the node operator",
                SlotStatus.Full => "is already fully booked",
                _ => "is no longer available"
            };

            return ServiceResult<bool>.Conflict(
                $"Slot {slot.SlotCode} {reason}. Please choose another slot.", ErrorCodes.SlotUnavailable);
        }

        // The prosumer cannot trade more energy than the window was published to carry.
        if (energyKwh > slot.EnergyCapacityKwh)
        {
            return ServiceResult<bool>.Invalid(
                $"The selected slot can trade at most {FormatKwh(slot.EnergyCapacityKwh)} kWh.");
        }

        return ServiceResult<bool>.Ok(true, "The slot may be booked.");
    }

    /// <summary>Rule R13 - asks whether a NIC already holds a live booking on a slot.</summary>
    /// <param name="nic">NIC of the prosumer being checked.</param>
    /// <param name="slotId">The slot in question.</param>
    /// <param name="excludeReservationId">Booking to ignore, so an update does not clash with itself.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>True when a Pending or Approved booking already exists for that pair.</returns>
    private async Task<bool> HasActiveReservationOnSlotAsync(
        string nic, string slotId, string? excludeReservationId, CancellationToken ct)
    {
        // SlotId is stored as an ObjectId, so an unparsable value could never match a stored booking anyway.
        if (!ObjectId.TryParse(slotId, out _))
        {
            return false;
        }

        var builder = Builders<EnergyReservation>.Filter;

        // ***RULE R13*** - only Pending and Approved bookings count as "held"; a cancelled or rejected one
        // has released the slot and must not block the prosumer from trying again.
        var filter = builder.Regex(r => r.ProsumerNic, AnchoredIgnoreCase(nic))
                     & builder.Eq(r => r.SlotId, slotId)
                     & builder.In(r => r.Status, new[] { ReservationStatus.Pending, ReservationStatus.Approved });

        if (!string.IsNullOrWhiteSpace(excludeReservationId) && ObjectId.TryParse(excludeReservationId, out _))
        {
            filter &= builder.Ne(r => r.Id, excludeReservationId);
        }

        var count = await _db.Reservations.CountDocumentsAsync(filter, cancellationToken: ct);
        return count > 0;
    }

    /// <summary>Builds an anchored, case insensitive regex for matching a whole stored value.</summary>
    /// <param name="value">The raw user supplied text to match.</param>
    /// <returns>A BSON regex that matches the trimmed value exactly, ignoring case.</returns>
    private static BsonRegularExpression AnchoredIgnoreCase(string? value)
    {
        // The value is escaped before it becomes a regex, so a NIC containing a regex metacharacter is
        // matched literally instead of changing the meaning of the query.
        return new BsonRegularExpression($"^{Regex.Escape((value ?? string.Empty).Trim())}$", "i");
    }

    /// <summary>Re-shapes a failed result of any payload type into a reservation shaped failure.</summary>
    /// <typeparam name="TSource">Payload type of the failing result.</typeparam>
    /// <param name="failure">The failed result whose status, message and code must be preserved.</param>
    /// <returns>The same failure, typed for a reservation endpoint.</returns>
    private static ServiceResult<ReservationDto> AsReservationFailure<TSource>(ServiceResult<TSource> failure)
    {
        // The helpers above report rule violations against bool or entity payloads; this keeps the exact
        // status and error code so the controller still emits 400, 403, 404 or 409 as the contract requires.
        return failure.Status switch
        {
            ResultStatus.NotFound => ServiceResult<ReservationDto>.NotFound(failure.Message, failure.ErrorCode),
            ResultStatus.Conflict => ServiceResult<ReservationDto>.Conflict(failure.Message, failure.ErrorCode),
            ResultStatus.Forbidden => ServiceResult<ReservationDto>.Forbidden(failure.Message, failure.ErrorCode),
            ResultStatus.Unauthorized => ServiceResult<ReservationDto>.Unauthorized(failure.Message, failure.ErrorCode),
            ResultStatus.Error => ServiceResult<ReservationDto>.Error(failure.Message, failure.ErrorCode),
            _ => ServiceResult<ReservationDto>.Invalid(failure.Message, failure.ErrorCode)
        };
    }

    /// <summary>Formats a kWh figure for a user facing message.</summary>
    /// <param name="value">The energy value in kilowatt hours.</param>
    /// <returns>The value with at most two decimals, using invariant formatting.</returns>
    private static string FormatKwh(double value)
    {
        // Invariant formatting keeps the message identical whatever culture the server happens to run under.
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

}
