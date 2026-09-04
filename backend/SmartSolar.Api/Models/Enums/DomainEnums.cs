/* =============================================================================
 * File        : DomainEnums.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Models / Enums
 * Description : Every closed set of values used by the domain. They are stored
 *               in MongoDB as strings (see BsonRepresentation on the entities)
 *               so a document remains readable in Compass and so the JSON that
 *               the web and Android clients receive is self describing.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

namespace SmartSolar.Api.Models.Enums;

/// <summary>The three principals recognised by the system.</summary>
public enum UserRole
{
    /// <summary>Back office administrator - full system administration rights.</summary>
    Backoffice = 0,

    /// <summary>Microgrid site operator - operational tools on web and mobile.</summary>
    GridOperator = 1,

    /// <summary>Solar prosumer - property owner with a solar array (mobile app).</summary>
    Prosumer = 2
}

/// <summary>Life cycle state of any account in the Users collection.</summary>
public enum AccountStatus
{
    /// <summary>Self registered from the mobile app, awaiting back office activation.</summary>
    Pending = 0,

    /// <summary>Able to log in and transact.</summary>
    Active = 1,

    /// <summary>Blocked. Only a Backoffice officer may return it to Active (rule R5).</summary>
    Deactivated = 2
}

/// <summary>Physical classification of a microgrid node.</summary>
public enum StationType
{
    Solar = 0,
    Hybrid = 1,
    Battery = 2
}

/// <summary>Whether a microgrid node is currently in service.</summary>
public enum StationStatus
{
    Active = 0,
    Inactive = 1
}

/// <summary>Direction of the energy transfer booked for a slot.</summary>
public enum SlotType
{
    /// <summary>The prosumer draws energy from the grid node.</summary>
    Charging = 0,

    /// <summary>The prosumer delivers surplus solar energy into the node.</summary>
    DropOff = 1
}

/// <summary>Availability state of an energy booking slot.</summary>
public enum SlotStatus
{
    Available = 0,
    Full = 1,
    Blocked = 2
}

/// <summary>Life cycle of an energy reservation.</summary>
public enum ReservationStatus
{
    /// <summary>Created by a prosumer, waiting for staff approval.</summary>
    Pending = 0,

    /// <summary>Approved by staff - a QR token has been issued.</summary>
    Approved = 1,

    /// <summary>Energy transfer finalised by a grid operator after a QR scan.</summary>
    Completed = 2,

    /// <summary>Withdrawn by the prosumer or by staff (rule R3).</summary>
    Cancelled = 3,

    /// <summary>Refused by staff.</summary>
    Rejected = 4
}
