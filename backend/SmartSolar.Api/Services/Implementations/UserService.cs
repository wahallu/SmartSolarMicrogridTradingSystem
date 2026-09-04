/* =============================================================================
 * File        : UserService.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Services / Implementations
 * Description : Administration of the two web facing account types - Backoffice
 *               officers and Grid Operators. Rule R10 says only a Backoffice
 *               officer may create, edit, activate, deactivate or delete these
 *               accounts, and rule R5 says only a Backoffice officer may return a
 *               deactivated account to Active; both rules are enforced here in
 *               the service (FAT service) so the controller stays a thin adapter.
 *               Prosumers are deliberately out of scope - they are keyed by NIC
 *               and are managed by ProsumerService instead.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartSolar.Api.Common;
using SmartSolar.Api.Data;
using SmartSolar.Api.DTOs.Users;
using SmartSolar.Api.Models;
using SmartSolar.Api.Models.Enums;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Services.Implementations;

/// <summary>
/// MongoDB backed implementation of <see cref="IUserService"/>. Every method
/// re-checks the caller's role rather than trusting the controller's
/// [Authorize] attribute, because the business rule - not the transport - is the
/// authority on who may administer an account.
/// </summary>
public class UserService : IUserService
{
    private readonly MongoDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly ILogger<UserService> _logger;

    /// <summary>Captures the collaborators this service needs for its whole lifetime.</summary>
    public UserService(
        MongoDbContext db,
        IPasswordHasher hasher,
        IClock clock,
        ILogger<UserService> logger)
    {
        // Constructor injection keeps the service testable: a frozen IClock and an
        // in-memory Mongo instance are enough to exercise every rule below.
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _logger = logger;
    }

    // =========================================================================
    // Queries
    // =========================================================================

    /// <summary>Returns a paged, filtered page of web user accounts.</summary>
    /// <param name="query">Role, status, free text search and paging options.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    public async Task<ServiceResult<PagedResult<UserDto>>> GetUsersAsync(UserQuery query, CancellationToken ct = default)
    {
        // This endpoint administers WEB users. When no explicit role filter is sent
        // the result is restricted to Backoffice + GridOperator so that the tens of
        // thousands of prosumer records never leak into the administration screen.
        var builder = Builders<User>.Filter;
        var filters = new List<FilterDefinition<User>>();

        if (query.Role.HasValue)
        {
            filters.Add(builder.Eq(u => u.Role, query.Role.Value));
        }
        else
        {
            filters.Add(builder.In(u => u.Role, new[] { UserRole.Backoffice, UserRole.GridOperator }));
        }

        // Optional life cycle filter - Pending / Active / Deactivated.
        if (query.Status.HasValue)
        {
            filters.Add(builder.Eq(u => u.Status, query.Status.Value));
        }

        // Free text search. Regex.Escape is essential: without it a user typing "a.*"
        // (or a malicious "(((") would inject an expression into the server side query.
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = new BsonRegularExpression(Regex.Escape(query.Search.Trim()), "i");
            filters.Add(builder.Or(
                builder.Regex(u => u.Nic, pattern),
                builder.Regex(u => u.Username, pattern),
                builder.Regex(u => u.FullName, pattern),
                builder.Regex(u => u.Email, pattern)));
        }

        var filter = builder.And(filters);

        // The total is counted with the same filter so the pager stays consistent
        // with the page that is actually returned.
        var total = await _db.Users.CountDocumentsAsync(filter, cancellationToken: ct);

        var users = await _db.Users
            .Find(filter)
            .SortByDescending(u => u.CreatedAt)
            .Skip(query.Skip)
            .Limit(query.PageSize)
            .ToListAsync(ct);

        var items = users.Select(u => u.ToDto()).ToList();
        return ServiceResult<PagedResult<UserDto>>.Ok(
            PagedResult<UserDto>.Create(items, query.Page, query.PageSize, total),
            "Users retrieved successfully.");
    }

    /// <summary>Reads a single account by its MongoDB identifier.</summary>
    /// <param name="id">Mongo ObjectId of the account, as a string.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    public async Task<ServiceResult<UserDto>> GetByIdAsync(string id, CancellationToken ct = default)
    {
        // A malformed id is a "not found", never an exception: the driver throws when
        // it tries to convert a non ObjectId string for an ObjectId represented field.
        var user = await FindUserByIdAsync(id, ct);
        if (user is null)
        {
            return ServiceResult<UserDto>.NotFound("The requested user account was not found.");
        }

        return ServiceResult<UserDto>.Ok(user.ToDto(), "User retrieved successfully.");
    }

    // =========================================================================
    // Commands
    // =========================================================================

    /// <summary>Creates a Backoffice or Grid Operator account (rule R10).</summary>
    /// <param name="request">The account details posted by the web client.</param>
    /// <param name="caller">Authenticated caller - must be a Backoffice officer.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    public async Task<ServiceResult<UserDto>> CreateUserAsync(CreateUserRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - only a Backoffice officer may create a web user account.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<UserDto>.Forbidden(
                "Only a Backoffice user may create system accounts.", ErrorCodes.Forbidden);
        }

        // A prosumer is keyed by NIC, which this payload cannot carry, so creating one
        // here would produce an account without its business primary key (see R7).
        if (request.Role == UserRole.Prosumer)
        {
            return ServiceResult<UserDto>.Invalid(
                "Prosumer accounts must be created through the prosumer endpoints so that the NIC is captured as the primary key.");
        }

        var username = request.Username.Trim();
        var email = request.Email.Trim();

        // Username uniqueness is checked here so the client gets a readable message
        // instead of a raw duplicate key error from the unique index.
        if (await ExistsAsync(Builders<User>.Filter.Regex(u => u.Username, ExactMatch(username)), ct))
        {
            return ServiceResult<UserDto>.Conflict(
                $"The username '{username}' is already taken.", ErrorCodes.UsernameAlreadyExists);
        }

        if (await ExistsAsync(Builders<User>.Filter.Regex(u => u.Email, ExactMatch(email)), ct))
        {
            return ServiceResult<UserDto>.Conflict(
                $"The e-mail address '{email}' is already registered.", ErrorCodes.EmailAlreadyExists);
        }

        var now = _clock.UtcNow;
        var (hash, salt) = _hasher.Hash(request.Password);

        var user = new User
        {
            Nic = null,
            Username = username,
            Email = email,
            PasswordHash = hash,
            PasswordSalt = salt,
            FullName = request.FullName.Trim(),
            Phone = request.Phone.Trim(),
            Address = request.Address.Trim(),
            Role = request.Role,

            // Staff accounts are created by a human administrator who has already
            // vetted the person, so they are usable immediately. Only self registered
            // prosumers start Pending (rule R6).
            Status = AccountStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = caller.Username,
            UpdatedBy = caller.Username
        };

        // A grid operator may be given a home microgrid node. The station code is
        // denormalised onto the account so operator lists need no second query.
        if (request.Role == UserRole.GridOperator && !string.IsNullOrWhiteSpace(request.AssignedStationId))
        {
            var station = await FindStationByIdAsync(request.AssignedStationId, ct);
            if (station is null)
            {
                return ServiceResult<UserDto>.NotFound("The assigned microgrid station was not found.");
            }

            user.AssignedStationId = station.Id;
            user.AssignedStationCode = station.StationCode;
        }

        try
        {
            await _db.Users.InsertOneAsync(user, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Two administrators creating the same username at the same instant land
            // here; the unique index is the final authority, so report it as a conflict.
            _logger.LogWarning(ex, "Duplicate key while creating user {Username}.", username);
            return ServiceResult<UserDto>.Conflict(
                "That username or e-mail address is already registered.", ErrorCodes.UsernameAlreadyExists);
        }

        _logger.LogInformation("User {Username} ({Role}) created by {Caller}.", user.Username, user.Role, caller.Username);
        return ServiceResult<UserDto>.Created(user.ToDto(), "User created successfully.");
    }

    /// <summary>Edits an existing web user account (rule R10).</summary>
    /// <param name="id">Mongo identifier of the account being edited.</param>
    /// <param name="request">The new profile, role, status and station assignment.</param>
    /// <param name="caller">Authenticated caller - must be a Backoffice officer.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    public async Task<ServiceResult<UserDto>> UpdateUserAsync(string id, UpdateUserRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - account administration is a Backoffice only capability.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<UserDto>.Forbidden(
                "Only a Backoffice user may modify system accounts.", ErrorCodes.Forbidden);
        }

        var user = await FindUserByIdAsync(id, ct);
        if (user is null)
        {
            return ServiceResult<UserDto>.NotFound("The requested user account was not found.");
        }

        var email = request.Email.Trim();

        // Only re-check uniqueness when the address actually changed, and always
        // exclude this document so an unchanged e-mail never collides with itself.
        if (!string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var duplicate = Builders<User>.Filter.And(
                Builders<User>.Filter.Regex(u => u.Email, ExactMatch(email)),
                Builders<User>.Filter.Ne(u => u.Id, user.Id));

            if (await ExistsAsync(duplicate, ct))
            {
                return ServiceResult<UserDto>.Conflict(
                    $"The e-mail address '{email}' is already registered.", ErrorCodes.EmailAlreadyExists);
            }
        }

        user.Email = email;
        user.FullName = request.FullName.Trim();
        user.Phone = request.Phone.Trim();
        user.Address = request.Address.Trim();

        // Turning a web account into a prosumer would leave it without a NIC, which
        // is the prosumer primary key - so that transition is refused (see R7).
        if (request.Role.HasValue)
        {
            if (request.Role.Value == UserRole.Prosumer)
            {
                return ServiceResult<UserDto>.Invalid(
                    "A system account cannot be converted into a prosumer because a prosumer requires a NIC as its primary key.");
            }

            user.Role = request.Role.Value;
        }

        if (request.Status.HasValue)
        {
            user.Status = request.Status.Value;
        }

        if (user.Role != UserRole.GridOperator)
        {
            // Only a grid operator has a home node, so a role change away from
            // GridOperator must clear the assignment and its denormalised code.
            user.AssignedStationId = null;
            user.AssignedStationCode = null;
        }
        else if (request.AssignedStationId is not null)
        {
            if (string.IsNullOrWhiteSpace(request.AssignedStationId))
            {
                // An explicit empty value means "unassign this operator".
                user.AssignedStationId = null;
                user.AssignedStationCode = null;
            }
            else
            {
                var station = await FindStationByIdAsync(request.AssignedStationId, ct);
                if (station is null)
                {
                    return ServiceResult<UserDto>.NotFound("The assigned microgrid station was not found.");
                }

                user.AssignedStationId = station.Id;
                user.AssignedStationCode = station.StationCode;
            }
        }

        user.UpdatedAt = _clock.UtcNow;
        user.UpdatedBy = caller.Username;

        await _db.Users.ReplaceOneAsync(
            Builders<User>.Filter.Eq(u => u.Id, user.Id), user, cancellationToken: ct);

        _logger.LogInformation("User {Username} updated by {Caller}.", user.Username, caller.Username);
        return ServiceResult<UserDto>.Ok(user.ToDto(), "User updated successfully.");
    }

    /// <summary>Returns a blocked account to Active - Backoffice only (rules R5 and R10).</summary>
    /// <param name="id">Mongo identifier of the account to reactivate.</param>
    /// <param name="caller">Authenticated caller - must be a Backoffice officer.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    public async Task<ServiceResult<UserDto>> ActivateAsync(string id, CurrentUser caller, CancellationToken ct = default)
    {
        // R5 - reactivation is reserved for Backoffice officers; an operator who could
        // re-enable their own colleague's blocked account would defeat the control.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<UserDto>.Forbidden(
                "Only a Backoffice user may activate an account.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<UserDto>.NotFound("The requested user account was not found.");
        }

        // Activation also withdraws any pending deactivation request, otherwise the
        // account would come back Active while still flagged for removal.
        var update = Builders<User>.Update
            .Set(u => u.Status, AccountStatus.Active)
            .Set(u => u.DeactivationRequested, false)
            .Unset(u => u.DeactivationRequestedAt)
            .Unset(u => u.DeactivationReason)
            .Set(u => u.UpdatedAt, _clock.UtcNow)
            .Set(u => u.UpdatedBy, caller.Username);

        var updated = await _db.Users.FindOneAndUpdateAsync(
            Builders<User>.Filter.Eq(u => u.Id, id),
            update,
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is null)
        {
            return ServiceResult<UserDto>.NotFound("The requested user account was not found.");
        }

        _logger.LogInformation("Account {Username} activated by {Caller}.", updated.Username, caller.Username);
        return ServiceResult<UserDto>.Ok(updated.ToDto(), "Account activated successfully.");
    }

    /// <summary>Blocks an account from logging in - Backoffice only (rule R10).</summary>
    /// <param name="id">Mongo identifier of the account to block.</param>
    /// <param name="request">Optional free text reason recorded against the account.</param>
    /// <param name="caller">Authenticated caller - must be a Backoffice officer.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    public async Task<ServiceResult<UserDto>> DeactivateAsync(string id, DeactivateRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - only a Backoffice officer may block an account.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<UserDto>.Forbidden(
                "Only a Backoffice user may deactivate an account.", ErrorCodes.Forbidden);
        }

        // Locking yourself out would leave the system with one fewer administrator and
        // no way back in, so the caller may never deactivate their own account.
        if (string.Equals(id, caller.UserId, StringComparison.Ordinal))
        {
            return ServiceResult<UserDto>.Invalid(
                "You cannot deactivate your own account.", ErrorCodes.CannotDeleteSelf);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<UserDto>.NotFound("The requested user account was not found.");
        }

        var reason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request!.Reason!.Trim();

        var update = Builders<User>.Update
            .Set(u => u.Status, AccountStatus.Deactivated)
            .Set(u => u.DeactivationReason, reason)
            .Set(u => u.UpdatedAt, _clock.UtcNow)
            .Set(u => u.UpdatedBy, caller.Username);

        var updated = await _db.Users.FindOneAndUpdateAsync(
            Builders<User>.Filter.Eq(u => u.Id, id),
            update,
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is null)
        {
            return ServiceResult<UserDto>.NotFound("The requested user account was not found.");
        }

        _logger.LogInformation("Account {Username} deactivated by {Caller}.", updated.Username, caller.Username);
        return ServiceResult<UserDto>.Ok(updated.ToDto(), "Account deactivated successfully.");
    }

    /// <summary>Sets a new password on a user's behalf - Backoffice only (rule R10).</summary>
    /// <param name="id">Mongo identifier of the account whose password is replaced.</param>
    /// <param name="request">The new clear text password, hashed before it is stored.</param>
    /// <param name="caller">Authenticated caller - must be a Backoffice officer.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    public async Task<ServiceResult<bool>> ResetPasswordAsync(string id, ResetPasswordRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - resetting somebody else's password is an administrative act.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<bool>.Forbidden(
                "Only a Backoffice user may reset a password.", ErrorCodes.Forbidden);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<bool>.NotFound("The requested user account was not found.");
        }

        // A fresh salt is produced with every reset, so the new hash is unrelated to
        // the old one even if the officer types the same password twice.
        var (hash, salt) = _hasher.Hash(request.NewPassword);

        var update = Builders<User>.Update
            .Set(u => u.PasswordHash, hash)
            .Set(u => u.PasswordSalt, salt)
            .Set(u => u.UpdatedAt, _clock.UtcNow)
            .Set(u => u.UpdatedBy, caller.Username);

        var result = await _db.Users.UpdateOneAsync(
            Builders<User>.Filter.Eq(u => u.Id, id), update, cancellationToken: ct);

        if (result.MatchedCount == 0)
        {
            return ServiceResult<bool>.NotFound("The requested user account was not found.");
        }

        _logger.LogInformation("Password reset for user {UserId} by {Caller}.", id, caller.Username);
        return ServiceResult<bool>.Ok(true, "Password reset successfully.");
    }

    /// <summary>Permanently removes an account - Backoffice only, never the caller (rule R10).</summary>
    /// <param name="id">Mongo identifier of the account to delete.</param>
    /// <param name="caller">Authenticated caller - must be a Backoffice officer.</param>
    /// <param name="ct">Cancellation token flowed through to MongoDB.</param>
    public async Task<ServiceResult<bool>> DeleteAsync(string id, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - hard deletion of an account is a Backoffice only capability.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<bool>.Forbidden(
                "Only a Backoffice user may delete an account.", ErrorCodes.Forbidden);
        }

        // Deleting your own record would destroy the session you are working in and
        // could remove the last administrator, so it is refused outright.
        if (string.Equals(id, caller.UserId, StringComparison.Ordinal))
        {
            return ServiceResult<bool>.Invalid(
                "You cannot delete your own account.", ErrorCodes.CannotDeleteSelf);
        }

        if (!ObjectId.TryParse(id, out _))
        {
            return ServiceResult<bool>.NotFound("The requested user account was not found.");
        }

        var result = await _db.Users.DeleteOneAsync(
            Builders<User>.Filter.Eq(u => u.Id, id), ct);

        if (result.DeletedCount == 0)
        {
            return ServiceResult<bool>.NotFound("The requested user account was not found.");
        }

        _logger.LogInformation("User {UserId} deleted by {Caller}.", id, caller.Username);
        return ServiceResult<bool>.Ok(true, "User deleted successfully.");
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>Loads an account by identifier, treating a malformed id as "not found".</summary>
    private async Task<User?> FindUserByIdAsync(string id, CancellationToken ct)
    {
        // Id is stored as an ObjectId, so the driver would throw a FormatException when
        // it tried to serialise a string such as "abc" into the filter. Guarding first
        // turns a would-be HTTP 500 into a clean HTTP 404.
        if (string.IsNullOrWhiteSpace(id) || !ObjectId.TryParse(id, out _))
        {
            return null;
        }

        return await _db.Users
            .Find(Builders<User>.Filter.Eq(u => u.Id, id))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Loads a microgrid node by identifier, treating a malformed id as "not found".</summary>
    private async Task<SolarStationInfo?> FindStationByIdAsync(string? stationId, CancellationToken ct)
    {
        // Same ObjectId guard as above - a bad station id must not crash a user create.
        if (string.IsNullOrWhiteSpace(stationId) || !ObjectId.TryParse(stationId, out _))
        {
            return null;
        }

        return await _db.Stations
            .Find(Builders<SolarStationInfo>.Filter.Eq(s => s.Id, stationId))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Answers whether at least one account matches the supplied filter.</summary>
    private async Task<bool> ExistsAsync(FilterDefinition<User> filter, CancellationToken ct)
    {
        // A bounded count is cheaper than materialising the document just to test it.
        var count = await _db.Users.CountDocumentsAsync(
            filter, new CountOptions { Limit = 1 }, ct);
        return count > 0;
    }

    /// <summary>Builds an anchored, case insensitive regular expression for an exact value match.</summary>
    private static BsonRegularExpression ExactMatch(string value)
    {
        // Uniqueness of a username or e-mail must ignore letter case, otherwise
        // "Admin" and "admin" would become two different logins. Regex.Escape keeps
        // characters such as '.' and '+' (common in e-mail addresses) literal.
        return new BsonRegularExpression("^" + Regex.Escape(value) + "$", "i");
    }
}
