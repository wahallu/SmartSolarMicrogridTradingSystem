/* =============================================================================
 * File        : CurrentUser.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Security
 * Description : Reads the signed-in user's identity and role from JWT claims.
 * Author      : IT22049872 - Chathushka
 * Created     : 2026-09-04
 * =============================================================================
 */

using System.Security.Claims;
using SmartSolar.Api.Models.Enums;

namespace SmartSolar.Api.Security;

/// <summary>Identity facts about the caller of the current request.</summary>
public class CurrentUser
{
    public string UserId { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public UserRole Role { get; init; } = UserRole.Prosumer;

    /// <summary>Account lifecycle status projected from the JWT claim.</summary>
    public string? Status { get; init; }

    /// <summary>NIC of the caller when the caller is a prosumer, otherwise null.</summary>
    public string? Nic { get; init; }

    public string? AssignedStationId { get; init; }
    public string? AssignedStationCode { get; init; }

    /// <summary>True for Backoffice officers - the only role allowed to reactivate accounts.</summary>
    public bool IsBackoffice => Role == UserRole.Backoffice;

    /// <summary>True for microgrid site operators.</summary>
    public bool IsGridOperator => Role == UserRole.GridOperator;

    /// <summary>True for solar prosumers.</summary>
    public bool IsProsumer => Role == UserRole.Prosumer;

    /// <summary>Backoffice and GridOperator together are treated as staff.</summary>
    public bool IsStaff => IsBackoffice || IsGridOperator;

    /// <summary>True when the account status claim is Active.</summary>
    public bool IsActive => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);

    /// <summary>Represents an unauthenticated caller for anonymous endpoints.</summary>
    public static CurrentUser Anonymous { get; } = new();

    /// <summary>Projects the claims on a validated JWT into a CurrentUser.</summary>
    public static CurrentUser FromPrincipal(ClaimsPrincipal? principal)
    {
        // An unauthenticated request yields the anonymous instance rather than null.
        if (principal?.Identity is not { IsAuthenticated: true })
        {
            return Anonymous;
        }

        // Role is stored as its enum name; an unparsable value falls back to the least privileged role.
        var roleText = principal.FindFirstValue(ClaimTypes.Role) ?? nameof(UserRole.Prosumer);
        if (!Enum.TryParse<UserRole>(roleText, ignoreCase: true, out var role))
        {
            role = UserRole.Prosumer;
        }

        return new CurrentUser
        {
            UserId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            Username = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            FullName = principal.FindFirstValue(AppClaimTypes.FullName) ?? string.Empty,
            Role = role,
            Status = principal.FindFirstValue(AppClaimTypes.Status),
            Nic = principal.FindFirstValue(AppClaimTypes.Nic),
            AssignedStationId = principal.FindFirstValue(AppClaimTypes.StationId),
            AssignedStationCode = principal.FindFirstValue(AppClaimTypes.StationCode)
        };
    }
}

/// <summary>Resolves the <see cref="CurrentUser"/> for the request in flight.</summary>
public interface ICurrentUserAccessor
{
    /// <summary>The caller of the current HTTP request.</summary>
    CurrentUser User { get; }
}

/// <summary>Reads the caller from IHttpContextAccessor.</summary>
public class CurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Stores the accessor that exposes the ambient HttpContext.</summary>
    public CurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        // Registered as scoped so the resolved user matches the request being served.
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>Projects the current request principal into a CurrentUser.</summary>
    public CurrentUser User => CurrentUser.FromPrincipal(_httpContextAccessor.HttpContext?.User);
}
