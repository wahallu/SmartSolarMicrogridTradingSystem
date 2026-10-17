/* =============================================================================
 * File        : BookingTime.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Common / Time
 * Description : Converts Sri Lankan booking wall-clock values to UTC instants.
 * Author      : IT22207272 - Nisal
 * Created     : 2026-10-17
 * =============================================================================
 */

using System.Globalization;

namespace SmartSolar.Api.Common;

/// <summary>
/// Defines the one business time zone used by every microgrid node. Operators enter
/// Sri Lankan wall-clock values, while MongoDB and all rule arithmetic store UTC.
/// </summary>
public static class BookingTime
{
    public const string TimeZoneId = "Asia/Colombo";
    public const int CurrentSemanticsVersion = 1;

    private static readonly TimeZoneInfo BusinessTimeZone = ResolveBusinessTimeZone();

    /// <summary>Converts a booking calendar day and HH:mm wall time to its UTC instant.</summary>
    public static DateTime ToUtc(DateTime calendarDate, string hhmm)
    {
        var timeOfDay = TimeSpan.ParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture);
        var localWallClock = DateTime.SpecifyKind(
            calendarDate.Date.Add(timeOfDay), DateTimeKind.Unspecified);

        return TimeZoneInfo.ConvertTimeToUtc(localWallClock, BusinessTimeZone);
    }

    /// <summary>Returns the Sri Lankan calendar day containing the supplied UTC instant.</summary>
    public static DateTime LocalCalendarDate(DateTime utcInstant)
    {
        var utc = DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, BusinessTimeZone);

        // SlotDate is a calendar key, represented in MongoDB as UTC midnight. It is
        // not itself the instant at which the slot starts.
        return DateTime.SpecifyKind(local.Date, DateTimeKind.Utc);
    }

    /// <summary>Formats a UTC instant in the wall-clock zone used at every node.</summary>
    public static string FormatLocal(DateTime utcInstant)
    {
        var utc = DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, BusinessTimeZone);
        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " Asia/Colombo";
    }

    private static TimeZoneInfo ResolveBusinessTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback for older Windows/IIS hosts that do not map IANA ids.
            return TimeZoneInfo.FindSystemTimeZoneById("Sri Lanka Standard Time");
        }
    }
}
