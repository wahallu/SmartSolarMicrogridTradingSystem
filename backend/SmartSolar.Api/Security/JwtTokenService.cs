/* =============================================================================
 * File        : JwtTokenService.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Security
 * Description : Issues the signed JSON Web Tokens that the React web client and
 *               the native Android client attach to every authenticated request.
 *               The token carries the caller's identity, role, NIC and assigned
 *               station so that authorisation decisions never need a database
 *               round trip on each call.
 * Author      : Chathushka - Identity, Access Control & Local Session (SE4040 EAD)
 * Created     : 2026-09-04
 * =============================================================================
 */

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartSolar.Api.Configuration;
using SmartSolar.Api.Models;

namespace SmartSolar.Api.Security;

/// <summary>Custom claim type names shared with the authorisation policies.</summary>
public static class AppClaimTypes
{
    public const string Nic = "nic";
    public const string StationId = "stationId";
    public const string StationCode = "stationCode";
    public const string FullName = "fullName";
    public const string Status = "accountStatus";
}

/// <summary>Creates bearer tokens for authenticated accounts.</summary>
public interface IJwtTokenService
{
    /// <summary>Builds a signed token for the supplied account.</summary>
    (string Token, DateTime ExpiresAtUtc) CreateToken(User user);
}

/// <summary>HMAC-SHA256 implementation of <see cref="IJwtTokenService"/>.</summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;

    /// <summary>Captures the signing settings bound from appsettings.json.</summary>
    public JwtTokenService(IOptions<JwtSettings> settings)
    {
        // Options are validated at start-up, so a missing secret fails fast rather than at first login.
        _settings = settings.Value;
    }

    /// <summary>Assembles the claim set, signs it and returns the token with its expiry.</summary>
    public (string Token, DateTime ExpiresAtUtc) CreateToken(User user)
    {
        // Claims are the whole identity the clients and the [Authorize] filters see.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(AppClaimTypes.FullName, user.FullName),
            new(AppClaimTypes.Status, user.Status.ToString())
        };

        // Only prosumers carry a NIC, and only operators carry an assigned station.
        if (!string.IsNullOrWhiteSpace(user.Nic))
        {
            claims.Add(new Claim(AppClaimTypes.Nic, user.Nic));
        }

        if (!string.IsNullOrWhiteSpace(user.AssignedStationId))
        {
            claims.Add(new Claim(AppClaimTypes.StationId, user.AssignedStationId));
        }

        if (!string.IsNullOrWhiteSpace(user.AssignedStationCode))
        {
            claims.Add(new Claim(AppClaimTypes.StationCode, user.AssignedStationCode));
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(_settings.ExpiryHours);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
