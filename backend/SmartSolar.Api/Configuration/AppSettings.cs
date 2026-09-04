/* =============================================================================
 * File        : AppSettings.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Configuration
 * Description : Strongly typed configuration option classes that are bound from
 *               appsettings.json at start-up. Keeping configuration in POCOs
 *               (rather than reading raw IConfiguration strings all over the
 *               code base) means a mis-spelled key fails once, at boot, instead
 *               of silently returning null deep inside a service.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

namespace SmartSolar.Api.Configuration;

/// <summary>
/// MongoDB connection details and the names of the four collections that make
/// up the server side data store.
/// </summary>
public class MongoDbSettings
{
    public const string SectionName = "MongoDbSettings";

    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "SmartSolarMicrogridDb";

    // The four collections required by the assignment specification.
    public string UsersCollection { get; set; } = "Users";
    public string StationsCollection { get; set; } = "SolarStationInfo";
    public string SlotsCollection { get; set; } = "EnergyBookingSlots";
    public string ReservationsCollection { get; set; } = "EnergyReservation";
}

/// <summary>
/// Signing material and lifetime settings for the JSON Web Tokens that both the
/// web client and the Android client present on every authenticated call.
/// </summary>
public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "SmartSolarMicrogridApi";
    public string Audience { get; set; } = "SmartSolarMicrogridClients";
    public int ExpiryHours { get; set; } = 8;
}

/// <summary>
/// Numeric constants behind the domain rules. They live in configuration so the
/// marking rules (7 day booking window, 12 hour modification notice) can be
/// demonstrated and tuned without a recompile - but note the rules themselves
/// are still enforced exclusively inside the API service layer (FAT service).
/// </summary>
public class BusinessRuleSettings
{
    public const string SectionName = "BusinessRules";

    /// <summary>R1 - a reservation must start within this many days from now.</summary>
    public int ReservationWindowDays { get; set; } = 7;

    /// <summary>R2/R3 - updates and cancellations need at least this much notice.</summary>
    public int ModificationNoticeHours { get; set; } = 12;

    /// <summary>R9 - how early an operator may scan a prosumer's QR code.</summary>
    public int QrGraceHoursBefore { get; set; } = 2;

    /// <summary>R9 - how late an operator may still scan a prosumer's QR code.</summary>
    public int QrGraceHoursAfter { get; set; } = 2;

    /// <summary>Default search radius used by the "nearby stations" map feature.</summary>
    public double DefaultNearbyRadiusKm { get; set; } = 25;
}

/// <summary>
/// Controls whether the demonstration data set is written on first start-up.
/// </summary>
public class SeedDataSettings
{
    public const string SectionName = "SeedData";

    public bool Enabled { get; set; } = true;
}
