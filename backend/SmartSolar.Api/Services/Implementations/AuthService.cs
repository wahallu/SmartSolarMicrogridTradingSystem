/* =============================================================================
 * File        : AuthService.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Services / Implementations
 * Description : The authentication half of the FAT service layer. It owns every
 *               decision taken while a principal signs in or joins the system:
 *               resolving a login identifier (NIC, username or e-mail), checking
 *               the PBKDF2 password, refusing accounts that are still Pending
 *               (rule R6) or already Deactivated (rule R5), rejecting duplicate
 *               NIC self registrations (rule R7) and changing a caller's own
 *               password. Controllers only translate the returned ServiceResult
 *               into an HTTP status code - no rule is ever repeated in a client.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartSolar.Api.Common;
using SmartSolar.Api.Data;
using SmartSolar.Api.DTOs.Auth;
using SmartSolar.Api.DTOs.Users;
using SmartSolar.Api.Models;
using SmartSolar.Api.Models.Enums;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Services.Implementations;

/// <summary>
/// MongoDB backed implementation of <see cref="IAuthService"/>. Every branch in
/// this class is a business rule, which is precisely why none of them appear in
/// the React or Android clients.
/// </summary>
public class AuthService : IAuthService
{
    private readonly MongoDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly IClock _clock;
    private readonly ILogger<AuthService> _logger;

    /// <summary>
    /// Message deliberately shared by "no such account" and "wrong password".
    /// Returning one wording for both keeps the endpoint from confirming which
    /// NICs, usernames or e-mail addresses actually exist (account enumeration).
    /// </summary>
    private const string InvalidCredentialsMessage =
        "Invalid credentials. Please check your NIC / username and password.";

    /// <summary>Captures the collections, the hasher, the token issuer and the clock.</summary>
    public AuthService(
        MongoDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        IClock clock,
        ILogger<AuthService> logger)
    {
        // Everything is injected so the rules below can be unit tested against a frozen clock.
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Validates a set of credentials and issues a bearer token. Enforces rule R6
    /// (a self registered prosumer may not sign in until the back office activates
    /// the account) and rule R5 (a deactivated account may only be restored by a
    /// Backoffice officer).
    /// </summary>
    /// <param name="request">Identifier (NIC, username or e-mail) and password.</param>
    /// <param name="ct">Cancellation token for the request in flight.</param>
    /// <returns>A token, its expiry and the caller profile, or a rule violation.</returns>
    public async Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        // A login identifier is one of three different fields, so it is resolved with a single OR query.
        var identifier = (request.Identifier ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrEmpty(request.Password))
        {
            // An empty identifier or password can never match; fail with the shared wording.
            return ServiceResult<AuthResponse>.Unauthorized(
                InvalidCredentialsMessage, ErrorCodes.InvalidCredentials);
        }

        try
        {
            var filter = BuildIdentifierFilter(identifier);

            var user = await _db.Users
                .Find(filter)
                .FirstOrDefaultAsync(ct);

            // Account enumeration guard: a missing account and a wrong password answer identically.
            if (user is null || !_hasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
            {
                _logger.LogWarning("Failed login attempt for identifier {Identifier}.", identifier);
                return ServiceResult<AuthResponse>.Unauthorized(
                    InvalidCredentialsMessage, ErrorCodes.InvalidCredentials);
            }

            // Rule R6 - self registered prosumers stay Pending until a Backoffice officer approves them.
            if (user.Status == AccountStatus.Pending)
            {
                return ServiceResult<AuthResponse>.Forbidden(
                    "Your account is awaiting activation by a Backoffice officer. Please try again once it has been approved.",
                    ErrorCodes.AccountPending);
            }

            // Rule R5 - a deactivated account is blocked and only the back office may reactivate it.
            if (user.Status == AccountStatus.Deactivated)
            {
                return ServiceResult<AuthResponse>.Forbidden(
                    "This account has been deactivated. Only a Backoffice officer can reactivate it.",
                    ErrorCodes.AccountDeactivated);
            }

            // The sign in instant comes from IClock so a frozen clock makes the behaviour testable.
            var now = _clock.UtcNow;
            user.LastLoginAt = now;

            // Only the audit field is written back - a full replace could clobber a concurrent edit.
            var idFilter = Builders<User>.Filter.Eq(u => u.Id, user.Id);
            var update = Builders<User>.Update.Set(u => u.LastLoginAt, now);
            await _db.Users.UpdateOneAsync(idFilter, update, cancellationToken: ct);

            var (token, expiresAtUtc) = _jwt.CreateToken(user);

            _logger.LogInformation("User {Username} ({Role}) signed in.", user.Username, user.Role);

            return ServiceResult<AuthResponse>.Ok(
                new AuthResponse
                {
                    Token = token,
                    ExpiresAtUtc = expiresAtUtc,
                    User = user.ToDto()
                },
                "Login successful.");
        }
        catch (MongoException ex)
        {
            // A database outage is an infrastructure fault, never a credential problem.
            _logger.LogError(ex, "Database failure while authenticating identifier {Identifier}.", identifier);
            return ServiceResult<AuthResponse>.Error(
                "The sign in service is temporarily unavailable. Please try again.");
        }
    }

    /// <summary>
    /// Registers a solar prosumer from the Android application. The NIC is the
    /// business primary key (rule R7) and the new account is always created in the
    /// Pending state so a Backoffice officer must approve it first (rule R6).
    /// </summary>
    /// <param name="request">Self registration payload posted by the mobile client.</param>
    /// <param name="ct">Cancellation token for the request in flight.</param>
    /// <returns>The created profile, or a conflict when the NIC or e-mail is taken.</returns>
    public async Task<ServiceResult<UserDto>> RegisterProsumerAsync(RegisterProsumerRequest request, CancellationToken ct = default)
    {
        // NIC is compared in upper case and e-mail in lower case so duplicates cannot slip through on casing.
        var nic = (request.Nic ?? string.Empty).Trim().ToUpperInvariant();
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        try
        {
            // Rule R7 - the NIC is unique across the Users collection, and it doubles as the username.
            var nicTaken = Builders<User>.Filter.Or(
                Builders<User>.Filter.Eq(u => u.Nic, nic),
                Builders<User>.Filter.Eq(u => u.Username, nic));

            if (await _db.Users.Find(nicTaken).AnyAsync(ct))
            {
                return ServiceResult<UserDto>.Conflict(
                    "An account already exists for this NIC. Please sign in instead, or contact the back office.",
                    ErrorCodes.NicAlreadyExists);
            }

            // E-mail is a secondary login identifier, so it must be unique as well.
            var emailTaken = Builders<User>.Filter.Regex(nameof(User.Email), CaseInsensitiveExact(email));

            if (await _db.Users.Find(emailTaken).AnyAsync(ct))
            {
                return ServiceResult<UserDto>.Conflict(
                    "This e-mail address is already registered to another account.",
                    ErrorCodes.EmailAlreadyExists);
            }

            var now = _clock.UtcNow;
            var (hash, salt) = _hasher.Hash(request.Password);

            var user = new User
            {
                Nic = nic,
                Username = nic,
                Email = email,
                PasswordHash = hash,
                PasswordSalt = salt,
                FullName = (request.FullName ?? string.Empty).Trim(),
                Phone = (request.Phone ?? string.Empty).Trim(),
                Address = (request.Address ?? string.Empty).Trim(),
                Role = UserRole.Prosumer,

                // Rule R6 - a self registered prosumer cannot log in until the back office activates them.
                Status = AccountStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = "self-registration"
            };

            await _db.Users.InsertOneAsync(user, cancellationToken: ct);

            _logger.LogInformation("Prosumer {Nic} self registered and is awaiting activation.", nic);

            return ServiceResult<UserDto>.Created(
                user.ToDto(),
                "Registration successful. Your account is pending activation by a Backoffice officer.");
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Two devices can register the same NIC at once; the unique index is the final arbiter of R7.
            _logger.LogWarning(ex, "Duplicate key rejected the registration of NIC {Nic}.", nic);
            return ServiceResult<UserDto>.Conflict(
                "An account already exists for this NIC or e-mail address.",
                ErrorCodes.NicAlreadyExists);
        }
        catch (MongoException ex)
        {
            // Any other database fault is reported as an infrastructure error, not a validation failure.
            _logger.LogError(ex, "Database failure while registering NIC {Nic}.", nic);
            return ServiceResult<UserDto>.Error(
                "The registration service is temporarily unavailable. Please try again.");
        }
    }

    /// <summary>
    /// Returns the profile of whoever presented the bearer token, so a client can
    /// refresh a cached profile without holding stale role or status information.
    /// </summary>
    /// <param name="caller">Identity projected from the validated JWT.</param>
    /// <param name="ct">Cancellation token for the request in flight.</param>
    /// <returns>The caller profile, or NotFound when the account has been removed.</returns>
    public async Task<ServiceResult<UserDto>> GetCurrentUserAsync(CurrentUser caller, CancellationToken ct = default)
    {
        // The token may outlive the account it describes, so the document is re-read every time.
        try
        {
            var user = await FindByIdAsync(caller.UserId, ct);

            if (user is null)
            {
                return ServiceResult<UserDto>.NotFound(
                    "Your account could no longer be found. Please sign in again.");
            }

            return ServiceResult<UserDto>.Ok(user.ToDto(), "Profile loaded successfully.");
        }
        catch (MongoException ex)
        {
            // Reading a profile must never surface a raw driver exception to a client.
            _logger.LogError(ex, "Database failure while loading the profile of user {UserId}.", caller.UserId);
            return ServiceResult<UserDto>.Error("The profile service is temporarily unavailable. Please try again.");
        }
    }

    /// <summary>
    /// Changes the caller's own password. The current password must be proved
    /// first, and the replacement must actually differ from it.
    /// </summary>
    /// <param name="caller">Identity projected from the validated JWT.</param>
    /// <param name="request">Current and new password supplied by the caller.</param>
    /// <param name="ct">Cancellation token for the request in flight.</param>
    /// <returns>True on success, or the rule that refused the change.</returns>
    public async Task<ServiceResult<bool>> ChangePasswordAsync(CurrentUser caller, ChangePasswordRequest request, CancellationToken ct = default)
    {
        // A password change is always self service - the account edited is the one in the token.
        try
        {
            var user = await FindByIdAsync(caller.UserId, ct);

            if (user is null)
            {
                return ServiceResult<bool>.NotFound(
                    "Your account could no longer be found. Please sign in again.");
            }

            // Proving knowledge of the current password stops a stolen token from locking the owner out.
            if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash, user.PasswordSalt))
            {
                _logger.LogWarning("Rejected password change for {Username}: wrong current password.", user.Username);
                return ServiceResult<bool>.Invalid(
                    "The current password is incorrect.",
                    ErrorCodes.WrongCurrentPassword);
            }

            // Re-using the same secret would leave the account exactly as exposed as before.
            if (string.Equals(request.NewPassword, request.CurrentPassword, StringComparison.Ordinal))
            {
                return ServiceResult<bool>.Invalid(
                    "The new password must be different from the current password.");
            }

            var now = _clock.UtcNow;
            var (hash, salt) = _hasher.Hash(request.NewPassword);

            // A fresh salt is stored alongside the new hash - the pair is only ever valid together.
            var idFilter = Builders<User>.Filter.Eq(u => u.Id, user.Id);
            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, hash)
                .Set(u => u.PasswordSalt, salt)
                .Set(u => u.UpdatedAt, now);

            await _db.Users.UpdateOneAsync(idFilter, update, cancellationToken: ct);

            _logger.LogInformation("User {Username} changed their password.", user.Username);

            return ServiceResult<bool>.Ok(true, "Password changed successfully.");
        }
        catch (MongoException ex)
        {
            // The caller is told the operation failed rather than being shown driver internals.
            _logger.LogError(ex, "Database failure while changing the password of user {UserId}.", caller.UserId);
            return ServiceResult<bool>.Error("The password service is temporarily unavailable. Please try again.");
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the "NIC or username or e-mail" filter used by the login endpoint,
    /// matching case insensitively without letting the caller inject a pattern.
    /// </summary>
    private static FilterDefinition<User> BuildIdentifierFilter(string identifier)
    {
        // One anchored, escaped, case insensitive pattern is reused across the three login fields.
        var pattern = CaseInsensitiveExact(identifier);
        var builder = Builders<User>.Filter;

        // Field names are given as member names so the class map resolves them to the BSON elements.
        return builder.Or(
            builder.Regex(nameof(User.Nic), pattern),
            builder.Regex(nameof(User.Username), pattern),
            builder.Regex(nameof(User.Email), pattern));
    }

    /// <summary>
    /// Turns user supplied text into an anchored, case insensitive regular
    /// expression that can only ever match that exact literal value.
    /// </summary>
    private static BsonRegularExpression CaseInsensitiveExact(string value)
    {
        // Regex.Escape neutralises metacharacters such as . * + ( ) so no pattern can be injected,
        // and the ^...$ anchors keep the match to the whole field rather than a substring.
        return new BsonRegularExpression("^" + Regex.Escape(value) + "$", "i");
    }

    /// <summary>Loads an account by its Mongo identifier, tolerating a malformed id.</summary>
    private async Task<User?> FindByIdAsync(string id, CancellationToken ct)
    {
        // A token carrying a non ObjectId subject must read as "no such account", never throw.
        if (string.IsNullOrWhiteSpace(id) || !ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var filter = Builders<User>.Filter.Eq(u => u.Id, id);
        return await _db.Users.Find(filter).FirstOrDefaultAsync(ct);
    }
}
