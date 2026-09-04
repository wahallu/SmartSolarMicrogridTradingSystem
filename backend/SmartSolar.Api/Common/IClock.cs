/* =============================================================================
 * File        : IClock.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Common
 * Description : A one line abstraction over "what time is it". Every business
 *               rule in this system is a comparison against now - the 7 day
 *               booking window, the 12 hour notice period, the QR scan window -
 *               so reading the time through an injected service instead of
 *               calling DateTime.UtcNow directly makes all of those rules
 *               unit testable with a frozen clock.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

namespace SmartSolar.Api.Common;

/// <summary>Supplies the current instant to the service layer.</summary>
public interface IClock
{
    /// <summary>Current UTC instant. All persisted times are UTC.</summary>
    DateTime UtcNow { get; }
}

/// <summary>Production clock backed by the operating system time.</summary>
public class SystemClock : IClock
{
    /// <summary>Reads the machine clock in UTC.</summary>
    public DateTime UtcNow => DateTime.UtcNow;
}
