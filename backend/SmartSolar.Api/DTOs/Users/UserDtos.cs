/* =============================================================================
 * File        : UserDtos.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : DTOs / Users
 * Description : Contains the API models used for staff and prosumer accounts.
 * Author      : IT22049872 - Chathushka
 * Created     : 2026-09-06
 * =============================================================================
 */

using System.ComponentModel.DataAnnotations;
using SmartSolar.Api.Common;
using SmartSolar.Api.Models.Enums;

namespace SmartSolar.Api.DTOs.Users;

/// <summary>Account as returned to the clients - never carries password material.</summary>
public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string? Nic { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool DeactivationRequested { get; set; }
    public DateTime? DeactivationRequestedAt { get; set; }
    public string? DeactivationReason { get; set; }
    public string? AssignedStationId { get; set; }
    public string? AssignedStationCode { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Creates a Backoffice or Grid Operator account from the web application.</summary>
public class CreateUserRequest
{
    [Required(ErrorMessage = "Username is required.")]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-mail is required.")]
    [EmailAddress(ErrorMessage = "A valid e-mail address is required.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(120, MinimumLength = 3)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    public string Phone { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(64, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Backoffice or GridOperator. Prosumers are created through /prosumers.</summary>
    [Required(ErrorMessage = "Role is required.")]
    public UserRole Role { get; set; } = UserRole.GridOperator;

    /// <summary>Home microgrid node, meaningful only for a GridOperator.</summary>
    public string? AssignedStationId { get; set; }
}

/// <summary>Updates a web user account.</summary>
public class UpdateUserRequest
{
    [Required(ErrorMessage = "E-mail is required.")]
    [EmailAddress(ErrorMessage = "A valid e-mail address is required.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full name is required.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    public string Phone { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public UserRole? Role { get; set; }
    public string? AssignedStationId { get; set; }
    public AccountStatus? Status { get; set; }
}

/// <summary>Creates a prosumer directly from the back office.</summary>
public class CreateProsumerRequest
{
    [Required(ErrorMessage = "NIC is required.")]
    [RegularExpression(@"^(\d{9}[VvXx]|\d{12})$",
        ErrorMessage = "NIC must be 9 digits followed by V/X, or 12 digits.")]
    public string Nic { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full name is required.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-mail is required.")]
    [EmailAddress(ErrorMessage = "A valid e-mail address is required.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    public string Phone { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(64, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Back office may create an account already Active; defaults to Pending.</summary>
    public AccountStatus? Status { get; set; }
}

/// <summary>Profile fields a prosumer may edit about themselves.</summary>
public class UpdateProsumerRequest
{
    [Required(ErrorMessage = "Full name is required.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-mail is required.")]
    [EmailAddress(ErrorMessage = "A valid e-mail address is required.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    public string Phone { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;
}

/// <summary>Optional free text reason attached to a deactivation.</summary>
public class DeactivateRequest
{
    [StringLength(250)]
    public string? Reason { get; set; }
}

/// <summary>New password chosen by a Backoffice officer on a user's behalf.</summary>
public class ResetPasswordRequest
{
    [Required(ErrorMessage = "New password is required.")]
    [StringLength(64, MinimumLength = 6)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Filter and paging options accepted by the user and prosumer lists.</summary>
public class UserQuery : PagingQuery
{
    public UserRole? Role { get; set; }
    public AccountStatus? Status { get; set; }

    /// <summary>Free text match over NIC, username, full name and e-mail.</summary>
    public string? Search { get; set; }
}
