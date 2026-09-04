/* =============================================================================
 * File        : User.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Models  (MongoDB collection: "Users")
 * Description : A single Users collection stores every principal in the system:
 *               Backoffice officers, Grid Operators and Solar Prosumers. Using
 *               one collection keeps authentication uniform, while the Nic
 *               field carries the National Identity Card number that the
 *               specification names as the primary key for prosumers.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using SmartSolar.Api.Models.Enums;

namespace SmartSolar.Api.Models;

/// <summary>
/// Persisted account document. Passwords are never stored in clear text - only
/// a PBKDF2 hash together with the per user salt that produced it.
/// </summary>
public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// National Identity Card number. This is the business primary key for a
    /// prosumer and is null for Backoffice / GridOperator accounts.
    /// </summary>
    [BsonElement("nic")]
    [BsonIgnoreIfNull]
    public string? Nic { get; set; }

    /// <summary>Login name. For prosumers the NIC is copied here as well.</summary>
    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Base64 PBKDF2-SHA256 hash (100,000 iterations).</summary>
    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Base64 of the 16 random bytes used to salt the hash above.</summary>
    [BsonElement("passwordSalt")]
    public string PasswordSalt { get; set; } = string.Empty;

    [BsonElement("fullName")]
    public string FullName { get; set; } = string.Empty;

    [BsonElement("phone")]
    public string Phone { get; set; } = string.Empty;

    [BsonElement("address")]
    public string Address { get; set; } = string.Empty;

    [BsonElement("role")]
    [BsonRepresentation(BsonType.String)]
    public UserRole Role { get; set; } = UserRole.Prosumer;

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public AccountStatus Status { get; set; } = AccountStatus.Pending;

    /// <summary>Raised by a prosumer from the mobile app; cleared by the back office.</summary>
    [BsonElement("deactivationRequested")]
    public bool DeactivationRequested { get; set; }

    [BsonElement("deactivationRequestedAt")]
    [BsonIgnoreIfNull]
    public DateTime? DeactivationRequestedAt { get; set; }

    [BsonElement("deactivationReason")]
    [BsonIgnoreIfNull]
    public string? DeactivationReason { get; set; }

    /// <summary>Home microgrid node of a grid operator (null for other roles).</summary>
    [BsonElement("assignedStationId")]
    [BsonIgnoreIfNull]
    public string? AssignedStationId { get; set; }

    [BsonElement("assignedStationCode")]
    [BsonIgnoreIfNull]
    public string? AssignedStationCode { get; set; }

    [BsonElement("lastLoginAt")]
    [BsonIgnoreIfNull]
    public DateTime? LastLoginAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("createdBy")]
    [BsonIgnoreIfNull]
    public string? CreatedBy { get; set; }

    [BsonElement("updatedBy")]
    [BsonIgnoreIfNull]
    public string? UpdatedBy { get; set; }
}
