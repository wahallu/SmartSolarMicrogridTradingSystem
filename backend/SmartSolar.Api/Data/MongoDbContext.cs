/* =============================================================================
 * File        : MongoDbContext.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Data
 * Description : The single point at which this application talks to MongoDB. It
 *               resolves the four collections named in the specification and
 *               creates their indexes once at start-up. Registered as a
 *               singleton because MongoClient maintains its own connection pool
 *               and is designed to be shared for the lifetime of the process.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * References  : Index creation follows the MongoDB .NET driver guidance at
 *               https://www.mongodb.com/docs/drivers/csharp/current/fundamentals/indexes/
 * =============================================================================
 */

using Microsoft.Extensions.Options;
using MongoDB.Driver;
using SmartSolar.Api.Configuration;
using SmartSolar.Api.Models;

namespace SmartSolar.Api.Data;

/// <summary>Exposes the four collections and owns index management.</summary>
public class MongoDbContext
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<MongoDbContext> _logger;

    /// <summary>Opens the client, resolves the database and caches the collections.</summary>
    public MongoDbContext(IOptions<MongoDbSettings> settings, ILogger<MongoDbContext> logger)
    {
        // MongoClient is thread safe and pools connections, so one instance serves the whole app.
        _logger = logger;
        var config = settings.Value;

        var client = new MongoClient(config.ConnectionString);
        _database = client.GetDatabase(config.DatabaseName);

        Users = _database.GetCollection<User>(config.UsersCollection);
        Stations = _database.GetCollection<SolarStationInfo>(config.StationsCollection);
        Slots = _database.GetCollection<EnergyBookingSlot>(config.SlotsCollection);
        Reservations = _database.GetCollection<EnergyReservation>(config.ReservationsCollection);

        Client = client;
        DatabaseName = config.DatabaseName;
    }

    /// <summary>Underlying client, used by the health endpoint to ping the server.</summary>
    public IMongoClient Client { get; }

    public string DatabaseName { get; }

    /// <summary>Collection 1 of 4 - every principal in the system.</summary>
    public IMongoCollection<User> Users { get; }

    /// <summary>Collection 2 of 4 - microgrid nodes.</summary>
    public IMongoCollection<SolarStationInfo> Stations { get; }

    /// <summary>Collection 3 of 4 - bookable energy windows.</summary>
    public IMongoCollection<EnergyBookingSlot> Slots { get; }

    /// <summary>Collection 4 of 4 - power trading reservations.</summary>
    public IMongoCollection<EnergyReservation> Reservations { get; }

    /// <summary>Creates every index the query patterns rely on. Safe to run repeatedly.</summary>
    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        // CreateMany is idempotent - an index that already exists with the same shape is left alone.
        try
        {
            await Users.Indexes.CreateManyAsync(new[]
            {
                // NIC is the prosumer primary key: unique, but sparse so web users may omit it.
                new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.Nic),
                    new CreateIndexOptions { Unique = true, Sparse = true, Name = "ux_users_nic" }),
                new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.Username),
                    new CreateIndexOptions { Unique = true, Name = "ux_users_username" }),
                new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.Email),
                    new CreateIndexOptions { Unique = true, Sparse = true, Name = "ux_users_email" }),
                new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.Role).Ascending(u => u.Status),
                    new CreateIndexOptions { Name = "ix_users_role_status" })
            }, ct);

            await Stations.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<SolarStationInfo>(
                    Builders<SolarStationInfo>.IndexKeys.Ascending(s => s.StationCode),
                    new CreateIndexOptions { Unique = true, Name = "ux_stations_code" }),
                new CreateIndexModel<SolarStationInfo>(
                    Builders<SolarStationInfo>.IndexKeys.Ascending(s => s.Status),
                    new CreateIndexOptions { Name = "ix_stations_status" })
            }, ct);

            await Slots.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<EnergyBookingSlot>(
                    Builders<EnergyBookingSlot>.IndexKeys
                        .Ascending(s => s.StationId)
                        .Ascending(s => s.SlotDate)
                        .Ascending(s => s.SlotCode),
                    new CreateIndexOptions { Unique = true, Name = "ux_slots_station_date_code" }),
                new CreateIndexModel<EnergyBookingSlot>(
                    Builders<EnergyBookingSlot>.IndexKeys.Ascending(s => s.StartUtc),
                    new CreateIndexOptions { Name = "ix_slots_start" }),
                new CreateIndexModel<EnergyBookingSlot>(
                    Builders<EnergyBookingSlot>.IndexKeys
                        .Ascending(s => s.StationId)
                        .Ascending(s => s.Status),
                    new CreateIndexOptions { Name = "ix_slots_station_status" })
            }, ct);

            await Reservations.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<EnergyReservation>(
                    Builders<EnergyReservation>.IndexKeys.Ascending(r => r.ReservationNo),
                    new CreateIndexOptions { Unique = true, Name = "ux_res_no" }),
                // Sparse: only approved reservations carry a QR token.
                new CreateIndexModel<EnergyReservation>(
                    Builders<EnergyReservation>.IndexKeys.Ascending(r => r.QrToken),
                    new CreateIndexOptions { Unique = true, Sparse = true, Name = "ux_res_qr" }),
                new CreateIndexModel<EnergyReservation>(
                    Builders<EnergyReservation>.IndexKeys
                        .Ascending(r => r.ProsumerNic)
                        .Descending(r => r.ReservationStartUtc),
                    new CreateIndexOptions { Name = "ix_res_nic_start" }),
                new CreateIndexModel<EnergyReservation>(
                    Builders<EnergyReservation>.IndexKeys
                        .Ascending(r => r.StationId)
                        .Ascending(r => r.Status)
                        .Ascending(r => r.ReservationStartUtc),
                    new CreateIndexOptions { Name = "ix_res_station_status_start" })
            }, ct);

            _logger.LogInformation("MongoDB indexes verified on database {Database}.", DatabaseName);
        }
        catch (MongoException ex)
        {
            // Index creation must never stop the API from starting - log and continue.
            _logger.LogError(ex, "Failed to create one or more MongoDB indexes.");
        }
    }

    /// <summary>Pings the server so the health endpoint can report connectivity.</summary>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        // A failed ping is reported as "down" rather than thrown, so /health always answers.
        try
        {
            await _database.RunCommandAsync<MongoDB.Bson.BsonDocument>(
                new MongoDB.Bson.BsonDocument("ping", 1), cancellationToken: ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB ping failed.");
            return false;
        }
    }
}
