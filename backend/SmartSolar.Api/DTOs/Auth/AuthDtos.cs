/* =============================================================================
 * File        : AuthDtos.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : DTOs / Auth
 * Description : Request and response contracts for logging in, self registering
 *               as a solar prosumer and changing a password. Entities are never
 *               exposed directly - a DTO keeps the password hash and salt off
 *               the wire and lets the JSON shape stay stable.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using System.ComponentModel.DataAnnotations;

namespace SmartSolar.Api.DTOs.Auth;

/// <summary>Credentials posted to POST /api/v1/auth/login.</summary>
public class LoginRequest
{
    /// <summary>NIC (prosumer), username or e-mail address - any of the three works.</summary>
    [Required(ErrorMessage = "NIC, username or e-mail is required.")]
    public string Identifier { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Self registration payload used by the Android application.</summary>
public class RegisterProsumerRequest
{
    /// <summary>National Identity Card number - the prosumer primary key.</summary>
    [Required(ErrorMessage = "NIC is required.")]
    [RegularExpression(@"^(\d{9}[VvXx]|\d{12})$",
        ErrorMessage = "NIC must be 9 digits followed by V/X, or 12 digits.")]
    public string Nic { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(120, MinimumLength = 3)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-mail is required.")]
    [EmailAddress(ErrorMessage = "A valid e-mail address is required.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [RegularExpression(@"^[0-9+\-\s()]{9,15}$", ErrorMessage = "Enter a valid phone number.")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Address is required.")]
    [StringLength(250, MinimumLength = 5)]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(64, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Password change payload for an already authenticated caller.</summary>
public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Current password is required.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "New password is required.")]
    [StringLength(64, MinimumLength = 6, ErrorMessage = "New password must be at least 6 characters.")]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Result of a successful login.</summary>
public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public Users.UserDto User { get; set; } = new();
}
