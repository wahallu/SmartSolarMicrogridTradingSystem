/* =============================================================================
 * File        : IClock.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Common
 * Description : Provides UTC time to services that enforce time-based rules.
 * Author      : IT22228062 - Akash
 * Created     : 2026-09-14
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
