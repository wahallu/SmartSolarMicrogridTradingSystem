/* =============================================================================
 * File        : DataSeeder.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Data
 * Description : Writes the demonstration data set described in section 10 of the
 *               master contract: the four fixed staff logins, six solar prosumers
 *               (one Pending and one Deactivated so the activation and the
 *               reactivation screens both have a subject), six microgrid nodes
 *               with real Sri Lankan coordinates, three weeks of booking slots
 *               and roughly twenty five reservations spread over every status.
 *               The seeder runs on EVERY start-up, so each collection is written
 *               only when it is empty - see SeedAsync for the idempotence rule.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using System.Globalization;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartSolar.Api.Common;
using SmartSolar.Api.Models;
using SmartSolar.Api.Models.Enums;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Implementations;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Data;

/// <summary>
/// Creates the first run demonstration data set. It is deliberately a plain
/// class rather than a hosted service so the composition root decides when it
/// runs, and so a unit test can drive it against a throw away database.
/// </summary>
public class DataSeeder
{
    /// <summary>Marker written into CreatedBy so seeded rows are recognisable in Compass.</summary>
    private const string SeedAuthor = "system-seed";

    /// <summary>Number of days of FUTURE slots published per node (covers the 7 day window of rule R1 twice over).</summary>
    private const int FutureSlotDays = 14;

    /// <summary>Number of days of PAST slots published per node, so completed reservations have something to point at.</summary>
    private const int PastSlotDays = 7;

    /// <summary>Energy tradable in one seeded window, in kWh.</summary>
    private const double SlotEnergyCapacityKwh = 20;

    /// <summary>Battery bays published per time window at every node.</summary>
    private const int BaysPerWindow = 2;

    private readonly MongoDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IQrCodeGenerator _qr;
    private readonly IClock _clock;
    private readonly ILogger<DataSeeder> _logger;

    /// <summary>Captures the data context, the hasher, the QR generator, the clock and the logger.</summary>
    /// <param name="db">Access to the four MongoDB collections.</param>
    /// <param name="hasher">Produces the PBKDF2 hash and salt for every seeded password.</param>
    /// <param name="qr">Issues the URL safe QR tokens carried by the approved reservations.</param>
    /// <param name="clock">Injected clock - "today" is read from here so a frozen clock moves the whole data set.</param>
    /// <param name="logger">Diagnostics sink; the seeder reports exactly what it created or skipped.</param>
    public DataSeeder(
        MongoDbContext db,
        IPasswordHasher hasher,
        IQrCodeGenerator qr,
        IClock clock,
        ILogger<DataSeeder> logger)
    {
        // Dependencies are captured once; nothing is resolved lazily so a mis-registration fails at boot.
        _db = db;
        _hasher = hasher;
        _qr = qr;
        _clock = clock;
        _logger = logger;
    }

    // =========================================================================
    // Entry point
    // =========================================================================

    /// <summary>
    /// Seeds the demonstration data set. IDEMPOTENCE: this method runs on every
    /// single application start-up, so each of the four collections is counted
    /// first and is written only when that count is zero. A collection that
    /// already holds documents is left completely untouched - the seeder never
    /// updates, patches or deletes existing data, which means restarting the API
    /// can never disturb work done during a demonstration.
    /// </summary>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        // ORDERING CHOICE: stations are seeded FIRST and users second. The two grid operators carry an
        // AssignedStationId / AssignedStationCode pointing at STN-001 and STN-002, so seeding the nodes
        // up front lets SeedUsersAsync resolve those ids by a simple lookup. The alternative - seeding
        // users then patching the operators afterwards - would need a second write pass for no benefit.
        var stationsCreated = await SeedStationsAsync(ct);
        var usersCreated = await SeedUsersAsync(ct);
        var slotsCreated = await SeedSlotsAsync(ct);
        var reservationsCreated = await SeedReservationsAsync(ct);

        // The closing summary states both what this run created and what the database now holds in total,
        // so a marker reading the console can confirm the demonstration data set at a glance.
        var userTotal = await _db.Users.CountDocumentsAsync(Builders<User>.Filter.Empty, cancellationToken: ct);
        var stationTotal = await _db.Stations.CountDocumentsAsync(Builders<SolarStationInfo>.Filter.Empty, cancellationToken: ct);
        var slotTotal = await _db.Slots.CountDocumentsAsync(Builders<EnergyBookingSlot>.Filter.Empty, cancellationToken: ct);
        var reservationTotal = await _db.Reservations.CountDocumentsAsync(Builders<EnergyReservation>.Filter.Empty, cancellationToken: ct);

        _logger.LogInformation(
            "Seed summary - created this run: users {UsersCreated}, stations {StationsCreated}, slots {SlotsCreated}, reservations {ReservationsCreated}. " +
            "Database totals: users {UserTotal}, stations {StationTotal}, slots {SlotTotal}, reservations {ReservationTotal}.",
            usersCreated, stationsCreated, slotsCreated, reservationsCreated,
            userTotal, stationTotal, slotTotal, reservationTotal);
    }

    // =========================================================================
    // Stage 1 - microgrid nodes
    // =========================================================================

    /// <summary>
    /// Publishes the six microgrid nodes. Every node is Active, carries a genuine
    /// Sri Lankan GPS position so the Google Maps "nearby" screen has real pins to
    /// draw, and receives the full seven day schedule produced by
    /// <see cref="DomainMapper.DefaultWeeklySchedule"/> so rule R12 never rejects a
    /// demonstration booking.
    /// </summary>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Number of node documents inserted by this run; zero when the collection was already populated.</returns>
    private async Task<int> SeedStationsAsync(CancellationToken ct)
    {
        // IDEMPOTENCE - a non empty collection is skipped outright and nothing is rewritten.
        var existing = await _db.Stations.CountDocumentsAsync(Builders<SolarStationInfo>.Filter.Empty, cancellationToken: ct);
        if (existing > 0)
        {
            _logger.LogInformation("Seeder skipped SolarStationInfo: {Count} microgrid node(s) already present.", existing);
            return 0;
        }

        var now = _clock.UtcNow;

        // AvailableBatterySlots is seeded at roughly 60-80 per cent of the installed bays so the availability
        // widgets on both clients show a believable mid range figure rather than a full or an empty node.
        var seeds = new[]
        {
            new StationSeed("STN-001", "Malabe Solar Hub", StationType.Solar, "No. 45, New Kandy Road, Malabe",
                "Malabe", 6.9061, 79.9696, 250, 12, 8, "0112413001"),
            new StationSeed("STN-002", "Colombo Fort Grid Node", StationType.Hybrid, "No. 12, Church Street, Colombo 01",
                "Colombo", 6.9344, 79.8428, 400, 20, 14, "0112413002"),
            new StationSeed("STN-003", "Kaduwela Community Array", StationType.Solar, "No. 88, Avissawella Road, Kaduwela",
                "Kaduwela", 6.9331, 79.9850, 180, 10, 7, "0112413003"),
            new StationSeed("STN-004", "Nugegoda Battery Bank", StationType.Battery, "No. 210, High Level Road, Nugegoda",
                "Nugegoda", 6.8649, 79.8997, 320, 16, 11, "0112413004"),
            new StationSeed("STN-005", "Kandy Hill Microgrid", StationType.Hybrid, "No. 33, Peradeniya Road, Kandy",
                "Kandy", 7.2906, 80.6337, 210, 12, 9, "0812413005"),
            new StationSeed("STN-006", "Galle Coastal Node", StationType.Solar, "No. 7, Wakwella Road, Galle",
                "Galle", 6.0535, 80.2210, 150, 8, 5, "0912413006")
        };

        var documents = seeds.Select(seed => new SolarStationInfo
        {
            StationCode = seed.StationCode,
            Name = seed.Name,
            Type = seed.Type,
            Address = seed.Address,
            City = seed.City,
            Location = new GeoLocation { Latitude = seed.Latitude, Longitude = seed.Longitude },
            CapacityKwh = seed.CapacityKwh,
            TotalBatterySlots = seed.TotalBatterySlots,
            AvailableBatterySlots = seed.AvailableBatterySlots,
            OperatingHours = new OperatingHours { OpenTime = "06:00", CloseTime = "22:00" },
            // The shared default builder is reused so a seeded node is shaped exactly like one created
            // through POST /stations, and the seven row schedule table renders identically on both clients.
            WeeklySchedule = DomainMapper.DefaultWeeklySchedule("06:00", "22:00"),
            ContactNumber = seed.ContactNumber,
            Status = StationStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = SeedAuthor,
            UpdatedBy = SeedAuthor
        }).ToList();

        await _db.Stations.InsertManyAsync(documents, cancellationToken: ct);
        _logger.LogInformation("Seeder created {Count} microgrid node(s): {Codes}.",
            documents.Count, string.Join(", ", documents.Select(s => s.StationCode)));

        return documents.Count;
    }

    // =========================================================================
    // Stage 2 - accounts
    // =========================================================================

    /// <summary>
    /// Creates the four fixed staff logins and the six solar prosumers. The exact
    /// user names and passwords are quoted in the project report and used during
    /// the live demonstration, so they must not be altered. Every password is put
    /// through the injected <see cref="IPasswordHasher"/> - no clear text and no
    /// pre-computed hash is ever stored.
    /// </summary>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Number of account documents inserted by this run; zero when the collection was already populated.</returns>
    private async Task<int> SeedUsersAsync(CancellationToken ct)
    {
        // IDEMPOTENCE - a non empty Users collection is skipped so a real administrator is never overwritten.
        var existing = await _db.Users.CountDocumentsAsync(Builders<User>.Filter.Empty, cancellationToken: ct);
        if (existing > 0)
        {
            _logger.LogInformation("Seeder skipped Users: {Count} account(s) already present.", existing);
            return 0;
        }

        var now = _clock.UtcNow;

        // Stations were seeded first (see the ordering note in SeedAsync), so the operators' home nodes can be
        // resolved here by station code instead of needing a second corrective write pass.
        var stations = await _db.Stations
            .Find(Builders<SolarStationInfo>.Filter.Empty)
            .ToListAsync(ct);
        var stationsByCode = stations.ToDictionary(s => s.StationCode, StringComparer.OrdinalIgnoreCase);

        var seeds = new List<UserSeed>
        {
            // ---- staff: the login name is the username --------------------------------------------------
            new(null, "admin", "Admin@123", "System Administrator", "admin@smartsolar.lk", "0112345000",
                "No. 1, Sir Baron Jayatilaka Mawatha, Colombo 01", UserRole.Backoffice, AccountStatus.Active, null),
            new(null, "backoffice2", "Back@123", "Nimali Fernando", "nimali@smartsolar.lk", "0112345001",
                "No. 24, Ward Place, Colombo 07", UserRole.Backoffice, AccountStatus.Active, null),
            new(null, "operator1", "Operator@123", "Kasun Perera", "kasun@smartsolar.lk", "0112345002",
                "No. 102, Kaduwela Road, Malabe", UserRole.GridOperator, AccountStatus.Active, "STN-001"),
            new(null, "operator2", "Operator@123", "Dilani Silva", "dilani@smartsolar.lk", "0112345003",
                "No. 56, Galle Face Terrace, Colombo 03", UserRole.GridOperator, AccountStatus.Active, "STN-002"),

            // ---- prosumers: the NIC IS the login, so Username is a copy of Nic ---------------------------
            new("200112301234", "200112301234", "Prosumer@123", "Sanduni Jayawardena", "sanduni@gmail.com", "0771234567",
                "No. 78, Temple Road, Maharagama", UserRole.Prosumer, AccountStatus.Active, null),
            new("199845600789", "199845600789", "Prosumer@123", "Ruwan Bandara", "ruwan@gmail.com", "0719876543",
                "No. 15, Station Road, Dehiwala", UserRole.Prosumer, AccountStatus.Active, null),
            // Left Pending on purpose - this account is the subject of the pending-activation demonstration (R6).
            new("200234501122", "200234501122", "Prosumer@123", "Chamodi Weerasinghe", "chamodi@gmail.com", "0763334455",
                "No. 91, Hokandara Road, Thalawathugoda", UserRole.Prosumer, AccountStatus.Pending, null),
            // Left Deactivated on purpose - this account is the subject of the reactivation demonstration (R5).
            new("199756403311", "199756403311", "Prosumer@123", "Tharindu Rajapaksa", "tharindu@gmail.com", "0755556677",
                "No. 3, Lake Drive, Rajagiriya", UserRole.Prosumer, AccountStatus.Deactivated, null),
            new("200356701199", "200356701199", "Prosumer@123", "Ishara Gunawardena", "ishara@gmail.com", "0741112233",
                "No. 140, Horana Road, Piliyandala", UserRole.Prosumer, AccountStatus.Active, null),
            new("199912305566", "199912305566", "Prosumer@123", "Malith Senanayake", "malith@gmail.com", "0728889900",
                "No. 62, Kandy Road, Kiribathgoda", UserRole.Prosumer, AccountStatus.Active, null)
        };

        var documents = new List<User>(seeds.Count);
        foreach (var seed in seeds)
        {
            // Each account receives its own random salt, exactly as a real registration would.
            var (hash, salt) = _hasher.Hash(seed.Password);

            var user = new User
            {
                Nic = seed.Nic,
                Username = seed.Username,
                Email = seed.Email,
                PasswordHash = hash,
                PasswordSalt = salt,
                FullName = seed.FullName,
                Phone = seed.Phone,
                Address = seed.Address,
                Role = seed.Role,
                Status = seed.Status,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = SeedAuthor,
                UpdatedBy = SeedAuthor
            };

            // The two grid operators are pinned to their home node; both the id and the denormalised code are
            // stored because the JWT carries the id while the operator screens display the code.
            if (seed.AssignedStationCode is not null &&
                stationsByCode.TryGetValue(seed.AssignedStationCode, out var station))
            {
                user.AssignedStationId = station.Id;
                user.AssignedStationCode = station.StationCode;
            }

            // The deactivated prosumer carries the reason and the request flags a real withdrawal would have
            // left behind, so the back office reactivation screen has something meaningful to display.
            if (seed.Status == AccountStatus.Deactivated)
            {
                user.DeactivationRequested = true;
                user.DeactivationRequestedAt = now.AddDays(-20);
                user.DeactivationReason = "Prosumer relocated overseas and asked for the account to be suspended.";
            }

            documents.Add(user);
        }

        await _db.Users.InsertManyAsync(documents, cancellationToken: ct);
        _logger.LogInformation(
            "Seeder created {Count} account(s): {Staff} staff and {Prosumers} prosumer(s).",
            documents.Count,
            documents.Count(u => u.Role != UserRole.Prosumer),
            documents.Count(u => u.Role == UserRole.Prosumer));

        return documents.Count;
    }

    // =========================================================================
    // Stage 3 - booking slots
    // =========================================================================

    /// <summary>
    /// Publishes the booking slots for every node: fourteen consecutive days
    /// starting at today's UTC date, so the seven day window of rule R1 always has
    /// bookable data, PLUS seven days of slots dated BEFORE today. The past days
    /// are essential - the eight Completed reservations seeded in the next stage
    /// must point at a slot whose window has already closed, and a completed
    /// booking hanging off a future slot would make the history screens nonsense.
    /// </summary>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Number of slot documents inserted by this run; zero when the collection was already populated.</returns>
    private async Task<int> SeedSlotsAsync(CancellationToken ct)
    {
        // IDEMPOTENCE - published slots are never regenerated, because reservations already reference them.
        var existing = await _db.Slots.CountDocumentsAsync(Builders<EnergyBookingSlot>.Filter.Empty, cancellationToken: ct);
        if (existing > 0)
        {
            _logger.LogInformation("Seeder skipped EnergyBookingSlots: {Count} slot(s) already present.", existing);
            return 0;
        }

        var stations = await _db.Stations
            .Find(Builders<SolarStationInfo>.Filter.Empty)
            .SortBy(s => s.StationCode)
            .ToListAsync(ct);

        if (stations.Count == 0)
        {
            _logger.LogWarning("Seeder produced no slots: there are no microgrid nodes to publish them against.");
            return 0;
        }

        var now = _clock.UtcNow;

        // Every date is anchored to UTC midnight of today, read from the injected clock rather than
        // DateTime.UtcNow, so the whole seeded calendar shifts together under a frozen test clock.
        var today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);

        // The five two hour windows published at every node, in wall clock order.
        var windows = new[]
        {
            new SlotWindow(8, 10, "08:00", "10:00"),
            new SlotWindow(10, 12, "10:00", "12:00"),
            new SlotWindow(12, 14, "12:00", "14:00"),
            new SlotWindow(14, 16, "14:00", "16:00"),
            new SlotWindow(16, 18, "16:00", "18:00")
        };

        var documents = new List<EnergyBookingSlot>();

        foreach (var station in stations)
        {
            // Defensive guard - a station whose id is not a valid ObjectId could never be referenced by a
            // slot document, because EnergyBookingSlot.StationId is stored with a BsonType.ObjectId
            // representation. Skipping is safer than letting the driver throw during start-up.
            if (!ObjectId.TryParse(station.Id, out _))
            {
                _logger.LogWarning("Seeder skipped slots for node {StationCode}: its identifier is malformed.", station.StationCode);
                continue;
            }

            // Day index runs from -7 (a week ago) to +13 (a fortnight ahead) so both the historical and the
            // bookable halves of the calendar are produced by one loop and shipped in one InsertManyAsync.
            for (var dayOffset = -PastSlotDays; dayOffset < FutureSlotDays; dayOffset++)
            {
                var slotDate = today.AddDays(dayOffset);
                var slotIndex = 0;

                foreach (var window in windows)
                {
                    for (var bay = 1; bay <= BaysPerWindow; bay++)
                    {
                        // SlotCode is sequential inside one station and one date, which is exactly the shape
                        // of the unique index ux_slots_station_date_code created by MongoDbContext.
                        var slotCode = $"S{slotIndex + 1}";

                        documents.Add(new EnergyBookingSlot
                        {
                            StationId = station.Id,
                            StationCode = station.StationCode,
                            StationName = station.Name,
                            SlotCode = slotCode,
                            SlotDate = slotDate,
                            StartTime = window.StartTime,
                            EndTime = window.EndTime,
                            // SpecifyKind marks the composed instant as UTC so the driver stores it without
                            // applying a local time zone conversion on the way into MongoDB.
                            StartUtc = DateTime.SpecifyKind(slotDate.Date.AddHours(window.StartHour), DateTimeKind.Utc),
                            EndUtc = DateTime.SpecifyKind(slotDate.Date.AddHours(window.EndHour), DateTimeKind.Utc),
                            // Types alternate slot by slot so every day offers both directions of energy trade.
                            SlotType = slotIndex % 2 == 0 ? SlotType.Charging : SlotType.DropOff,
                            EnergyCapacityKwh = SlotEnergyCapacityKwh,
                            BatterySlotNumber = bay,
                            MaxBookings = 1,
                            BookedCount = 0,
                            Status = SlotStatus.Available,
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = SeedAuthor,
                            UpdatedBy = SeedAuthor
                        });

                        slotIndex++;
                    }
                }
            }
        }

        // A single bulk insert keeps start-up fast: one round trip instead of well over a thousand.
        await _db.Slots.InsertManyAsync(documents, cancellationToken: ct);

        _logger.LogInformation(
            "Seeder created {Count} booking slot(s): {Days} day(s) per node ({Past} past, {Future} future) across {Stations} node(s).",
            documents.Count, PastSlotDays + FutureSlotDays, PastSlotDays, FutureSlotDays, stations.Count);

        return documents.Count;
    }

    // =========================================================================
    // Stage 4 - reservations
    // =========================================================================

    /// <summary>
    /// Creates roughly twenty five reservations spread deliberately so that every
    /// screen in both clients opens with data: five Pending fill the approval
    /// queue, six Approved carry a real QR token, eight Completed populate the
    /// history and the delivered energy totals, four Cancelled and two Rejected
    /// round out the status filter. Reservations are never placed on the Pending
    /// or the Deactivated prosumer, because neither of those accounts could have
    /// logged in to make one.
    /// </summary>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    /// <returns>Number of reservation documents inserted by this run; zero when the collection was already populated.</returns>
    private async Task<int> SeedReservationsAsync(CancellationToken ct)
    {
        // IDEMPOTENCE - reservations are transactional history, so an existing set is never touched.
        var existing = await _db.Reservations.CountDocumentsAsync(Builders<EnergyReservation>.Filter.Empty, cancellationToken: ct);
        if (existing > 0)
        {
            _logger.LogInformation("Seeder skipped EnergyReservation: {Count} reservation(s) already present.", existing);
            return 0;
        }

        var now = _clock.UtcNow;
        var today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);

        // Only ACTIVE prosumers may hold bookings. The Pending account has never been able to log in and the
        // Deactivated one is blocked by rule R5, so giving either of them history would contradict the rules.
        var prosumerFilter = Builders<User>.Filter.Eq(u => u.Role, UserRole.Prosumer)
                             & Builders<User>.Filter.Eq(u => u.Status, AccountStatus.Active);
        // Sorted by Username rather than Nic because for a prosumer the two are the same value, and Username
        // is non nullable, which keeps the sort expression free of a nullable projection.
        var prosumers = await _db.Users.Find(prosumerFilter).SortBy(u => u.Username).ToListAsync(ct);

        var stations = await _db.Stations.Find(Builders<SolarStationInfo>.Filter.Empty).ToListAsync(ct);
        var stationsById = stations.ToDictionary(s => s.Id, StringComparer.Ordinal);

        if (prosumers.Count == 0 || stations.Count == 0)
        {
            _logger.LogWarning("Seeder produced no reservations: active prosumers or microgrid nodes are missing.");
            return 0;
        }

        // Only the slice of the calendar the seeded bookings can use is read back, rather than every slot.
        var slotFilter = Builders<EnergyBookingSlot>.Filter.Gte(s => s.SlotDate, today.AddDays(-PastSlotDays))
                         & Builders<EnergyBookingSlot>.Filter.Lte(s => s.SlotDate, today.AddDays(6));
        var slots = await _db.Slots.Find(slotFilter).ToListAsync(ct);

        // FUTURE pool: two to six days out. This sits comfortably inside the seven day window of rule R1 and
        // well beyond the twelve hour notice of rules R2 and R3, so every seeded booking shows CanModify and
        // CanCancel as true and the modify / cancel buttons on both clients are live from the first click.
        var futurePool = slots
            .Where(s => s.SlotDate >= today.AddDays(2) && s.SlotDate <= today.AddDays(6))
            .OrderBy(s => s.SlotDate)
            .ThenBy(s => s.SlotCode, StringComparer.Ordinal)
            .ThenBy(s => s.StationCode, StringComparer.Ordinal)
            .ToList();

        // PAST pool: windows that have already closed, which is what a Completed booking has to point at.
        var pastPool = slots
            .Where(s => s.EndUtc < now)
            .OrderByDescending(s => s.SlotDate)
            .ThenBy(s => s.SlotCode, StringComparer.Ordinal)
            .ThenBy(s => s.StationCode, StringComparer.Ordinal)
            .ToList();

        // Fifteen future slots (5 Pending + 6 Approved + 2 Cancelled + 2 Rejected) and ten past ones
        // (8 Completed + 2 Cancelled). Every reservation gets a slot of its own, so no seeded slot is
        // ever over booked against its MaxBookings of one.
        var futurePicks = TakeSpread(futurePool, 15, 11);
        var pastPicks = TakeSpread(pastPool, 10, 7);

        if (futurePicks.Count < 15 || pastPicks.Count < 10)
        {
            _logger.LogWarning(
                "Seeder found only {Future} future and {Past} past slot(s); the reservation set will be smaller than planned.",
                futurePicks.Count, pastPicks.Count);
        }

        var documents = new List<EnergyReservation>();
        var sequence = 0;
        var futureCursor = 0;
        var pastCursor = 0;

        // ---- 5 Pending: the back office approval queue opens with work waiting ---------------------------
        for (var i = 0; i < 5 && futureCursor < futurePicks.Count; i++, futureCursor++)
        {
            var slot = futurePicks[futureCursor];
            var reservation = NewReservation(++sequence, prosumers[sequence % prosumers.Count], slot, stationsById, now);
            reservation.Status = ReservationStatus.Pending;
            documents.Add(reservation);
        }

        // ---- 6 Approved: each carries a genuine QR token and the payload the scanner will read ------------
        for (var i = 0; i < 6 && futureCursor < futurePicks.Count; i++, futureCursor++)
        {
            var slot = futurePicks[futureCursor];
            var reservation = NewReservation(++sequence, prosumers[sequence % prosumers.Count], slot, stationsById, now);
            reservation.Status = ReservationStatus.Approved;

            // The token comes from the same generator the approval endpoint uses, so a seeded QR image
            // verifies through POST /reservations/verify-qr exactly like a freshly approved one.
            var token = _qr.CreateToken();
            var approvedAt = NotAfter(reservation.CreatedAt.AddHours(4), now);

            reservation.QrToken = token;
            reservation.QrPayload = BuildQrPayload(token, reservation);
            reservation.ApprovedBy = "admin";
            reservation.ApprovedAt = approvedAt;
            reservation.QrIssuedAt = approvedAt;
            reservation.UpdatedAt = approvedAt;
            reservation.UpdatedBy = "admin";

            documents.Add(reservation);
        }

        // ---- 2 Cancelled on future slots: withdrawn while notice remained (rule R3 was satisfied) ---------
        for (var i = 0; i < 2 && futureCursor < futurePicks.Count; i++, futureCursor++)
        {
            var slot = futurePicks[futureCursor];
            var reservation = NewReservation(++sequence, prosumers[sequence % prosumers.Count], slot, stationsById, now);
            ApplyCancellation(reservation, "Prosumer no longer needs this energy window.", reservation.ProsumerNic, now);
            documents.Add(reservation);
        }

        // ---- 2 Rejected: refused by staff, so the rejection reason column has content ---------------------
        var rejectionReasons = new[]
        {
            "Requested energy exceeds the capacity available at this node for the window.",
            "Node is scheduled for inverter maintenance during the requested window."
        };
        for (var i = 0; i < 2 && futureCursor < futurePicks.Count; i++, futureCursor++)
        {
            var slot = futurePicks[futureCursor];
            var reservation = NewReservation(++sequence, prosumers[sequence % prosumers.Count], slot, stationsById, now);
            reservation.Status = ReservationStatus.Rejected;
            reservation.RejectedReason = rejectionReasons[i % rejectionReasons.Length];
            reservation.UpdatedAt = NotAfter(reservation.CreatedAt.AddHours(5), now);
            reservation.UpdatedBy = "admin";
            documents.Add(reservation);
        }

        // ---- 8 Completed on past slots: the energy actually delivered feeds the dashboard totals ----------
        for (var i = 0; i < 8 && pastCursor < pastPicks.Count; i++, pastCursor++)
        {
            var slot = pastPicks[pastCursor];
            var reservation = NewReservation(++sequence, prosumers[sequence % prosumers.Count], slot, stationsById, now);
            reservation.Status = ReservationStatus.Completed;
            reservation.ApprovedBy = "admin";
            reservation.ApprovedAt = reservation.CreatedAt.AddHours(4);

            // Delivered energy lands near, but not exactly on, the requested figure - real transfers never
            // match to the decimal, and a slightly noisy number makes the reports look genuine.
            reservation.DeliveredKwh = Math.Round(reservation.EnergyKwh - 0.4 - (i % 4) * 0.35, 2);
            // Completion is stamped just after the window closed, which is when an operator files the reading.
            var completedAt = slot.EndUtc.AddMinutes(8 + i);
            reservation.CompletedBy = "operator1";
            reservation.CompletedAt = completedAt;
            reservation.OperatorNotes = $"Transfer finalised at {slot.StationCode} after QR verification; meter reading accepted.";
            reservation.UpdatedAt = completedAt;
            reservation.UpdatedBy = "operator1";

            documents.Add(reservation);
        }

        // ---- 2 Cancelled on past slots: historical withdrawals, so the history filter has both kinds ------
        for (var i = 0; i < 2 && pastCursor < pastPicks.Count; i++, pastCursor++)
        {
            var slot = pastPicks[pastCursor];
            var reservation = NewReservation(++sequence, prosumers[sequence % prosumers.Count], slot, stationsById, now);
            ApplyCancellation(reservation, "Cancelled by the back office after the prosumer reported an inverter fault.", "admin", now);
            documents.Add(reservation);
        }

        if (documents.Count == 0)
        {
            _logger.LogWarning("Seeder produced no reservations: no usable booking slots were found.");
            return 0;
        }

        await _db.Reservations.InsertManyAsync(documents, cancellationToken: ct);

        // Keep the slot counters truthful (see the method for why this matters).
        await ApplySlotConsumptionAsync(documents, slots, ct);

        _logger.LogInformation(
            "Seeder created {Count} reservation(s): {Pending} Pending, {Approved} Approved, {Completed} Completed, {Cancelled} Cancelled, {Rejected} Rejected.",
            documents.Count,
            documents.Count(r => r.Status == ReservationStatus.Pending),
            documents.Count(r => r.Status == ReservationStatus.Approved),
            documents.Count(r => r.Status == ReservationStatus.Completed),
            documents.Count(r => r.Status == ReservationStatus.Cancelled),
            documents.Count(r => r.Status == ReservationStatus.Rejected));

        return documents.Count;
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Increments BookedCount on every slot actually consumed by a seeded Pending
    /// or Approved reservation and flips the slot to Full once the count reaches
    /// MaxBookings, in one bulk write.
    ///
    /// This step is what makes the seeded database HONEST. ReservationService
    /// performs exactly this counter maintenance whenever a booking is created,
    /// so applying it here leaves the collections in precisely the state the
    /// service itself would have produced. Without it a slot could show as
    /// Available on the booking screen while a reservation already held it, and
    /// the very first demonstration booking would appear to succeed against a slot
    /// that was never really free. Cancelled and Rejected bookings are deliberately
    /// excluded - they release their slot, so they consume no capacity.
    /// </summary>
    /// <param name="reservations">The reservations just inserted by the seeder.</param>
    /// <param name="slots">The slots read earlier, used to read MaxBookings without a second query.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    private async Task ApplySlotConsumptionAsync(
        List<EnergyReservation> reservations, List<EnergyBookingSlot> slots, CancellationToken ct)
    {
        // Only the two live states hold a slot; everything else has already given it back.
        var consumedSlotIds = reservations
            .Where(r => r.Status is ReservationStatus.Pending or ReservationStatus.Approved)
            .Select(r => r.SlotId)
            .ToList();

        if (consumedSlotIds.Count == 0)
        {
            return;
        }

        var slotsById = slots.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var now = _clock.UtcNow;
        var writes = new List<WriteModel<EnergyBookingSlot>>();

        foreach (var group in consumedSlotIds.GroupBy(id => id, StringComparer.Ordinal))
        {
            // A slot id that does not resolve cannot be updated, so it is skipped rather than guessed at.
            if (!slotsById.TryGetValue(group.Key, out var slot) || !ObjectId.TryParse(group.Key, out _))
            {
                continue;
            }

            var bookedCount = slot.BookedCount + group.Count();
            var status = bookedCount >= slot.MaxBookings ? SlotStatus.Full : SlotStatus.Available;

            var filter = Builders<EnergyBookingSlot>.Filter.Eq(s => s.Id, slot.Id);
            var update = Builders<EnergyBookingSlot>.Update
                .Set(s => s.BookedCount, bookedCount)
                .Set(s => s.Status, status)
                .Set(s => s.UpdatedAt, now)
                .Set(s => s.UpdatedBy, SeedAuthor);

            writes.Add(new UpdateOneModel<EnergyBookingSlot>(filter, update));
        }

        if (writes.Count == 0)
        {
            return;
        }

        // One bulk write rather than one round trip per slot keeps start-up quick.
        await _db.Slots.BulkWriteAsync(writes, cancellationToken: ct);

        _logger.LogInformation(
            "Seeder reconciled {Count} booking slot(s) against the reservations it created, so slot availability is truthful.",
            writes.Count);
    }

    /// <summary>
    /// Builds one reservation with every denormalised field copied from the
    /// prosumer, the node and the slot it points at, exactly as
    /// ReservationService.CreateAsync would have done. Caller specialises the
    /// status and the workflow stamps afterwards.
    /// </summary>
    /// <param name="sequence">Global one based counter that keeps the reservation number unique.</param>
    /// <param name="prosumer">The owning solar prosumer.</param>
    /// <param name="slot">The booking slot being reserved.</param>
    /// <param name="stationsById">Node lookup used to denormalise the node name onto the booking.</param>
    /// <param name="now">Current instant from the injected clock.</param>
    /// <returns>A Pending reservation ready for status specific stamps.</returns>
    private EnergyReservation NewReservation(
        int sequence,
        User prosumer,
        EnergyBookingSlot slot,
        IReadOnlyDictionary<string, SolarStationInfo> stationsById,
        DateTime now)
    {
        // The node name is denormalised onto the booking so a reservation list never needs a second query.
        var stationName = stationsById.TryGetValue(slot.StationId, out var station)
            ? station.Name
            : slot.StationName;

        // Requested energy cycles through five believable figures, none exceeding the slot capacity.
        var energyKwh = 8 + (sequence % 5) * 3;

        // A booking is always created before its window opens; two days of lead time is typical, but the
        // stamp is clamped so a seeded row can never claim to have been created in the future.
        var createdAt = NotAfter(slot.StartUtc.AddDays(-2), now.AddHours(-1));

        return new EnergyReservation
        {
            // The date part is taken from the slot day so the seeded numbers read naturally on the screens,
            // while the global sequence guarantees the ux_res_no unique index is never violated.
            ReservationNo = string.Format(
                CultureInfo.InvariantCulture,
                "RSV-{0:yyyyMMdd}-{1:0000}",
                slot.StartUtc,
                sequence),
            ProsumerNic = prosumer.Nic ?? prosumer.Username,
            ProsumerName = prosumer.FullName,
            ProsumerPhone = prosumer.Phone,
            StationId = slot.StationId,
            StationCode = slot.StationCode,
            StationName = stationName,
            SlotId = slot.Id,
            SlotCode = slot.SlotCode,
            // Start, end, the display strings and the trade direction are all copied from the slot, so a
            // later edit to the slot can never silently change what the prosumer believes they booked.
            ReservationStartUtc = slot.StartUtc,
            ReservationEndUtc = slot.EndUtc,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            SlotType = slot.SlotType,
            EnergyKwh = energyKwh,
            Status = ReservationStatus.Pending,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            CreatedBy = SeedAuthor,
            UpdatedBy = SeedAuthor
        };
    }

    /// <summary>
    /// Serialises the QR payload in the EXACT shape the approval endpoint emits,
    /// so a seeded code and a freshly approved one are byte for byte comparable:
    /// {"v":1,"t":token,"r":reservationNo,"n":nic,"s":stationCode,"e":startUtc}.
    /// </summary>
    /// <param name="token">The URL safe token issued for this reservation.</param>
    /// <param name="reservation">The reservation the payload describes.</param>
    /// <returns>The exact JSON string that is encoded into the QR image.</returns>
    private static string BuildQrPayload(string token, EnergyReservation reservation)
    {
        // System.Text.Json is used over an anonymous object rather than string concatenation so that any
        // quote or backslash inside a value is escaped correctly and the scanner always gets valid JSON.
        return JsonSerializer.Serialize(new
        {
            v = 1,
            t = token,
            r = reservation.ReservationNo,
            n = reservation.ProsumerNic,
            s = reservation.StationCode,
            e = reservation.ReservationStartUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        });
    }

    /// <summary>Stamps a reservation as Cancelled with its reason and audit trail.</summary>
    /// <param name="reservation">The reservation being withdrawn.</param>
    /// <param name="reason">Why the booking was given up.</param>
    /// <param name="cancelledBy">Who withdrew it - the prosumer's NIC or a staff user name.</param>
    /// <param name="now">Current instant from the injected clock.</param>
    private static void ApplyCancellation(EnergyReservation reservation, string reason, string cancelledBy, DateTime now)
    {
        // The cancellation stamp is clamped to "now" so no seeded row is dated in the future.
        var cancelledAt = NotAfter(reservation.CreatedAt.AddHours(6), now);

        reservation.Status = ReservationStatus.Cancelled;
        reservation.CancelReason = reason;
        reservation.CancelledBy = cancelledBy;
        reservation.CancelledAt = cancelledAt;
        reservation.UpdatedAt = cancelledAt;
        reservation.UpdatedBy = cancelledBy;
    }

    /// <summary>Returns the candidate instant, or the limit when the candidate is later than it.</summary>
    /// <param name="candidate">The instant the seeder would like to stamp.</param>
    /// <param name="limit">The latest instant that is allowed.</param>
    /// <returns>Whichever of the two is earlier.</returns>
    private static DateTime NotAfter(DateTime candidate, DateTime limit)
    {
        // Keeps every audit stamp on seeded history in the past, which is what a real workflow would produce.
        return candidate > limit ? limit : candidate;
    }

    /// <summary>
    /// Picks distinct slots out of a pool by striding through it, so the seeded
    /// reservations land on different nodes, days and windows instead of filling
    /// one node's calendar from the top down.
    /// </summary>
    /// <param name="pool">Candidate slots, already ordered.</param>
    /// <param name="count">How many distinct slots are wanted.</param>
    /// <param name="stride">Step between picks; a prime keeps the walk well spread.</param>
    /// <returns>Up to <paramref name="count"/> distinct slots.</returns>
    private static List<EnergyBookingSlot> TakeSpread(List<EnergyBookingSlot> pool, int count, int stride)
    {
        // An empty pool yields an empty selection rather than throwing during start-up.
        var picked = new List<EnergyBookingSlot>();
        if (pool.Count == 0 || count <= 0)
        {
            return picked;
        }

        var used = new HashSet<string>(StringComparer.Ordinal);

        // The stride can revisit indices when it shares a factor with the pool size, so the walk is bounded.
        for (var attempt = 0; attempt < pool.Count && picked.Count < count; attempt++)
        {
            var slot = pool[attempt * stride % pool.Count];
            if (used.Add(slot.Id))
            {
                picked.Add(slot);
            }
        }

        // Fallback - if the stride cycled early, top the selection up in plain order so the count is met.
        foreach (var slot in pool)
        {
            if (picked.Count >= count)
            {
                break;
            }

            if (used.Add(slot.Id))
            {
                picked.Add(slot);
            }
        }

        return picked;
    }

    // =========================================================================
    // Seed value records
    // =========================================================================

    /// <summary>One row of the microgrid node seed table.</summary>
    private sealed record StationSeed(
        string StationCode,
        string Name,
        StationType Type,
        string Address,
        string City,
        double Latitude,
        double Longitude,
        double CapacityKwh,
        int TotalBatterySlots,
        int AvailableBatterySlots,
        string ContactNumber);

    /// <summary>One row of the account seed table; Nic is null for staff logins.</summary>
    private sealed record UserSeed(
        string? Nic,
        string Username,
        string Password,
        string FullName,
        string Email,
        string Phone,
        string Address,
        UserRole Role,
        AccountStatus Status,
        string? AssignedStationCode);

    /// <summary>One publishable two hour energy window.</summary>
    private sealed record SlotWindow(int StartHour, int EndHour, string StartTime, string EndTime);
}
