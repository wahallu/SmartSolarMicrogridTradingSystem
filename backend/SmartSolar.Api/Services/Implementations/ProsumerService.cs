/* =============================================================================
 * File        : ProsumerService.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Services / Implementations
 * Description : Fat service for solar prosumer accounts. Prosumers are addressed
 *               by their National Identity Card number rather than by a Mongo id,
 *               so every lookup here goes through one anchored, case insensitive
 *               NIC filter. The file also owns the account life cycle rules the
 *               back office depends on: R5 (only a Backoffice officer may return
 *               an account to Active), R7 (NIC is unique), R10 (only Backoffice
 *               may create, activate, deactivate or delete) and R11 (a prosumer
 *               may only read or edit their own profile).
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
/// Implements <see cref="IProsumerService"/>. Controllers call these methods and
/// do nothing but translate the returned <see cref="ServiceResult{T}"/> into an
/// HTTP status code - every rule decision is taken here.
/// </summary>
public class ProsumerService : IProsumerService
{
    private readonly MongoDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly ILogger<ProsumerService> _logger;

    /// <summary>Captures the collaborators this service needs for its whole lifetime.</summary>
    /// <param name="db">MongoDB collections gateway.</param>
    /// <param name="hasher">PBKDF2 hasher used when the back office creates an account.</param>
    /// <param name="clock">Injected clock - every rule reads "now" from here, never DateTime.UtcNow.</param>
    /// <param name="logger">Audit trail for the account life cycle transitions.</param>
    public ProsumerService(
        MongoDbContext db,
        IPasswordHasher hasher,
        IClock clock,
        ILogger<ProsumerService> logger)
    {
        // Plain constructor injection - the service holds no mutable state of its own.
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _logger = logger;
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Builds the anchored, case insensitive filter that matches exactly one NIC.
    /// NIC is the prosumer primary key, so every read and every write in this
    /// service resolves its document through this single helper.
    /// </summary>
    /// <param name="nic">Raw NIC supplied by the caller or taken from the route.</param>
    /// <returns>Filter matching the "nic" field, ignoring case and surrounding blanks.</returns>
    private static FilterDefinition<User> BuildNicFilter(string nic)
    {
        // Regex.Escape keeps a NIC that contains regex metacharacters from being
        // interpreted as a pattern; ^...$ anchors it so "20011" cannot match "200112301234".
        var pattern = "^" + Regex.Escape((nic ?? string.Empty).Trim()) + "$";
        return Builders<User>.Filter.Regex(u => u.Nic, new BsonRegularExpression(pattern, "i"));
    }

    /// <summary>Builds the anchored, case insensitive filter that matches one e-mail address.</summary>
    /// <param name="email">Raw e-mail address to look for.</param>
    /// <returns>Filter matching the "email" field, ignoring case.</returns>
    private static FilterDefinition<User> BuildEmailFilter(string email)
    {
        // E-mail uniqueness must not depend on the casing a client happened to send.
        var pattern = "^" + Regex.Escape((email ?? string.Empty).Trim()) + "$";
        return Builders<User>.Filter.Regex(u => u.Email, new BsonRegularExpression(pattern, "i"));
    }

    /// <summary>Normalises a NIC to the canonical stored form: trimmed and upper case.</summary>
    /// <param name="nic">Raw NIC.</param>
    /// <returns>Trimmed upper case NIC, never null.</returns>
    private static string NormaliseNic(string? nic)
    {
        // Sri Lankan NICs end in V or X; storing them upper case keeps the collection tidy.
        return (nic ?? string.Empty).Trim().ToUpperInvariant();
    }

    /// <summary>Normalises an e-mail address to the canonical stored form: trimmed and lower case.</summary>
    /// <param name="email">Raw e-mail address.</param>
    /// <returns>Trimmed lower case e-mail, never null.</returns>
    private static string NormaliseEmail(string? email)
    {
        // Lower casing on the way in means the unique index behaves the way users expect.
        return (email ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <summary>Collapses an optional free text reason to null when it carries no content.</summary>
    /// <param name="reason">Reason supplied by the caller, possibly blank.</param>
    /// <returns>Trimmed reason, or null when nothing meaningful was supplied.</returns>
    private static string? NormaliseReason(string? reason)
    {
        // A blank string would show up in the UI as an empty "reason" box, so store null instead.
        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>Decides whether a prosumer caller is acting on somebody else's NIC (rule R11).</summary>
    /// <param name="caller">Authenticated caller.</param>
    /// <param name="nic">NIC the request is aimed at.</param>
    /// <returns>True when the caller is a prosumer and the NIC is not their own.</returns>
    private static bool IsForeignProsumerAccess(CurrentUser caller, string nic)
    {
        // Staff are unaffected; a prosumer is confined to the NIC carried in their own token.
        return caller.IsProsumer
               && !string.Equals(caller.Nic?.Trim(), nic.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // Queries
    // =========================================================================

    /// <summary>Returns a paged, filtered list of solar prosumers, newest first.</summary>
    /// <param name="query">Status, free text search and paging options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One page of <see cref="UserDto"/> projections.</returns>
    public async Task<ServiceResult<PagedResult<UserDto>>> GetProsumersAsync(UserQuery query, CancellationToken ct = default)
    {
        // The role filter is fixed to Prosumer here - this endpoint never exposes
        // Backoffice or GridOperator accounts, whatever query.Role happens to hold.
        var builder = Builders<User>.Filter;
        var filter = builder.Eq(u => u.Role, UserRole.Prosumer);

        if (query.Status.HasValue)
        {
            filter &= builder.Eq(u => u.Status, query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // The term is escaped before it becomes a pattern so a user typing "(" cannot break the query.
            var term = new BsonRegularExpression(Regex.Escape(query.Search.Trim()), "i");
            filter &= builder.Or(
                builder.Regex(u => u.Nic, term),
                builder.Regex(u => u.FullName, term),
                builder.Regex(u => u.Email, term),
                builder.Regex(u => u.Phone, term));
        }

        var total = await _db.Users.CountDocumentsAsync(filter, cancellationToken: ct);

        var documents = await _db.Users
            .Find(filter)
            .Sort(Builders<User>.Sort.Descending(u => u.CreatedAt))
            .Skip(query.Skip)
            .Limit(query.PageSize)
            .ToListAsync(ct);

        var page = PagedResult<UserDto>.Create(
            documents.Select(d => d.ToDto()).ToList(),
            query.Page,
            query.PageSize,
            total);

        return ServiceResult<PagedResult<UserDto>>.Ok(page, "Prosumers retrieved successfully.");
    }

    /// <summary>Returns the back office "pending activation" work queue, oldest request first.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Prosumers that need a Backoffice decision.</returns>
    public async Task<ServiceResult<List<UserDto>>> GetPendingActivationsAsync(CancellationToken ct = default)
    {
        // This is a work queue rather than a plain status list, so it deliberately
        // contains BOTH kinds of item a Backoffice officer has to act on:
        //   (a) Status = Pending          - self registered sign ups awaiting approval (R6), and
        //   (b) DeactivationRequested     - existing prosumers who asked from the mobile app
        //                                   to be deactivated and are still waiting for an
        //                                   officer to action it (see RequestDeactivationAsync).
        var builder = Builders<User>.Filter;
        var filter = builder.Eq(u => u.Role, UserRole.Prosumer)
                     & builder.Or(
                         builder.Eq(u => u.Status, AccountStatus.Pending),
                         builder.Eq(u => u.DeactivationRequested, true));

        // Ascending CreatedAt so the longest waiting applicant is dealt with first.
        var documents = await _db.Users
            .Find(filter)
            .Sort(Builders<User>.Sort.Ascending(u => u.CreatedAt))
            .ToListAsync(ct);

        var items = documents.Select(d => d.ToDto()).ToList();
        return ServiceResult<List<UserDto>>.Ok(items, "Pending prosumer activations retrieved successfully.");
    }

    /// <summary>Reads one prosumer by NIC; a prosumer caller may only read their own record (rule R11).</summary>
    /// <param name="nic">NIC of the prosumer to read.</param>
    /// <param name="caller">Authenticated caller.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The prosumer projection, or Forbidden / NotFound.</returns>
    public async Task<ServiceResult<UserDto>> GetByNicAsync(string nic, CurrentUser caller, CancellationToken ct = default)
    {
        // R11 - a prosumer is confined to their own profile; staff may read anybody.
        if (IsForeignProsumerAccess(caller, nic))
        {
            return ServiceResult<UserDto>.Forbidden("You may only view your own profile.");
        }

        var prosumer = await _db.Users.Find(BuildNicFilter(nic)).FirstOrDefaultAsync(ct);
        if (prosumer is null)
        {
            return ServiceResult<UserDto>.NotFound($"No prosumer was found with NIC '{nic}'.");
        }

        return ServiceResult<UserDto>.Ok(prosumer.ToDto(), "Prosumer retrieved successfully.");
    }

    // =========================================================================
    // Commands
    // =========================================================================

    /// <summary>Creates a prosumer account from the back office (rules R7 and R10).</summary>
    /// <param name="request">NIC, profile fields, password and an optional initial status.</param>
    /// <param name="caller">Authenticated caller - must be a Backoffice officer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created projection, or Forbidden / Conflict.</returns>
    public async Task<ServiceResult<UserDto>> CreateAsync(CreateProsumerRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - only a Backoffice officer may create an account from the web application.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<UserDto>.Forbidden("Only a Backoffice officer may create prosumer accounts.");
        }

        var nic = NormaliseNic(request.Nic);
        var email = NormaliseEmail(request.Email);

        // R7 - the National Identity Card number is the prosumer primary key and must be unique.
        var nicTaken = await _db.Users.Find(BuildNicFilter(nic)).AnyAsync(ct);
        if (nicTaken)
        {
            return ServiceResult<UserDto>.Conflict(
                $"NIC '{nic}' is already registered.", ErrorCodes.NicAlreadyExists);
        }

        // E-mail carries a unique sparse index, so a duplicate has to be refused before the insert.
        var emailTaken = await _db.Users.Find(BuildEmailFilter(email)).AnyAsync(ct);
        if (emailTaken)
        {
            return ServiceResult<UserDto>.Conflict(
                $"E-mail '{email}' is already registered.", ErrorCodes.EmailAlreadyExists);
        }

        var now = _clock.UtcNow;
        var (hash, salt) = _hasher.Hash(request.Password);

        var prosumer = new User
        {
            Nic = nic,

            // The NIC doubles as the login name so a prosumer only ever remembers one identifier.
            Username = nic,
            Email = email,
            PasswordHash = hash,
            PasswordSalt = salt,
            FullName = request.FullName.Trim(),
            Phone = request.Phone.Trim(),
            Address = (request.Address ?? string.Empty).Trim(),
            Role = UserRole.Prosumer,

            // Created by staff, therefore Active by default. This differs on purpose from
            // self registration through /auth/register, which must start Pending under R6:
            // there the applicant is unverified, whereas here a Backoffice officer has already
            // checked the NIC in person, so a second activation step would be pure friction.
            Status = request.Status ?? AccountStatus.Active,
            DeactivationRequested = false,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = caller.Username,
            UpdatedBy = caller.Username
        };

        await _db.Users.InsertOneAsync(prosumer, cancellationToken: ct);
        _logger.LogInformation("Prosumer {Nic} created by {Caller} with status {Status}.",
            nic, caller.Username, prosumer.Status);

        return ServiceResult<UserDto>.Created(prosumer.ToDto(), "Prosumer created successfully.");
    }

    /// <summary>Edits a prosumer profile - back office, grid operator or the prosumer themselves (rule R11).</summary>
    /// <param name="nic">NIC of the prosumer being edited.</param>
    /// <param name="request">New full name, e-mail, phone and address.</param>
    /// <param name="caller">Authenticated caller.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated projection, or Forbidden / NotFound / Conflict.</returns>
    public async Task<ServiceResult<UserDto>> UpdateAsync(string nic, UpdateProsumerRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // R11 - a prosumer editing somebody else's NIC is refused; staff may edit anybody.
        if (IsForeignProsumerAccess(caller, nic))
        {
            return ServiceResult<UserDto>.Forbidden("You may only update your own profile.");
        }

        var existing = await _db.Users.Find(BuildNicFilter(nic)).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            return ServiceResult<UserDto>.NotFound($"No prosumer was found with NIC '{nic}'.");
        }

        var email = NormaliseEmail(request.Email);

        // Uniqueness is re-checked excluding this very document, otherwise saving an
        // unchanged e-mail address would collide with the record being edited.
        var duplicateEmail = await _db.Users
            .Find(BuildEmailFilter(email) & Builders<User>.Filter.Ne(u => u.Id, existing.Id))
            .AnyAsync(ct);
        if (duplicateEmail)
        {
            return ServiceResult<UserDto>.Conflict(
                $"E-mail '{email}' is already registered.", ErrorCodes.EmailAlreadyExists);
        }

        var update = Builders<User>.Update
            .Set(u => u.FullName, request.FullName.Trim())
            .Set(u => u.Email, email)
            .Set(u => u.Phone, request.Phone.Trim())
            .Set(u => u.Address, (request.Address ?? string.Empty).Trim())
            .Set(u => u.UpdatedAt, _clock.UtcNow)
            .Set(u => u.UpdatedBy, caller.Username);

        var updated = await _db.Users.FindOneAndUpdateAsync(
            BuildNicFilter(nic),
            update,
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is null)
        {
            return ServiceResult<UserDto>.NotFound($"No prosumer was found with NIC '{nic}'.");
        }

        _logger.LogInformation("Prosumer {Nic} profile updated by {Caller}.", nic, caller.Username);
        return ServiceResult<UserDto>.Ok(updated.ToDto(), "Profile updated successfully.");
    }

    /// <summary>Returns a prosumer account to Active - Backoffice only (rule R5).</summary>
    /// <param name="nic">NIC of the prosumer to activate.</param>
    /// <param name="caller">Authenticated caller - must be a Backoffice officer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated projection, or Forbidden / NotFound.</returns>
    public async Task<ServiceResult<UserDto>> ActivateAsync(string nic, CurrentUser caller, CancellationToken ct = default)
    {
        // R5 - BACKOFFICE ONLY. A deactivated or pending prosumer can be returned to
        // Active by nobody else: not a grid operator, and certainly not the prosumer,
        // which is the whole point of the rule. This is the single activation gate.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<UserDto>.Forbidden("Only a Backoffice officer may activate a prosumer account.");
        }

        var exists = await _db.Users.Find(BuildNicFilter(nic)).AnyAsync(ct);
        if (!exists)
        {
            return ServiceResult<UserDto>.NotFound($"No prosumer was found with NIC '{nic}'.");
        }

        // Activating also clears any outstanding deactivation request, since the
        // officer has now decided the account should stay in service.
        var update = Builders<User>.Update
            .Set(u => u.Status, AccountStatus.Active)
            .Set(u => u.DeactivationRequested, false)
            .Unset(u => u.DeactivationRequestedAt)
            .Unset(u => u.DeactivationReason)
            .Set(u => u.UpdatedAt, _clock.UtcNow)
            .Set(u => u.UpdatedBy, caller.Username);

        var updated = await _db.Users.FindOneAndUpdateAsync(
            BuildNicFilter(nic),
            update,
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is null)
        {
            return ServiceResult<UserDto>.NotFound($"No prosumer was found with NIC '{nic}'.");
        }

        _logger.LogInformation("Prosumer {Nic} activated by Backoffice officer {Caller}.", nic, caller.Username);
        return ServiceResult<UserDto>.Ok(updated.ToDto(), "Prosumer account activated successfully.");
    }

    /// <summary>Blocks a prosumer account from logging in - Backoffice only (rule R10).</summary>
    /// <param name="nic">NIC of the prosumer to deactivate.</param>
    /// <param name="request">Optional free text reason recorded against the account.</param>
    /// <param name="caller">Authenticated caller - must be a Backoffice officer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated projection, or Forbidden / NotFound.</returns>
    public async Task<ServiceResult<UserDto>> DeactivateAsync(string nic, DeactivateRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - deactivating an account is an administrative act reserved for the back office.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<UserDto>.Forbidden("Only a Backoffice officer may deactivate a prosumer account.");
        }

        var exists = await _db.Users.Find(BuildNicFilter(nic)).AnyAsync(ct);
        if (!exists)
        {
            return ServiceResult<UserDto>.NotFound($"No prosumer was found with NIC '{nic}'.");
        }

        var now = _clock.UtcNow;

        // DeactivationRequested is cleared because any self service request has now been
        // actioned - the account leaves the "pending activation" work queue at this point.
        var update = Builders<User>.Update
            .Set(u => u.Status, AccountStatus.Deactivated)
            .Set(u => u.DeactivationReason, NormaliseReason(request?.Reason))
            .Set(u => u.DeactivationRequested, false)
            .Set(u => u.UpdatedAt, now)
            .Set(u => u.UpdatedBy, caller.Username);

        var updated = await _db.Users.FindOneAndUpdateAsync(
            BuildNicFilter(nic),
            update,
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is null)
        {
            return ServiceResult<UserDto>.NotFound($"No prosumer was found with NIC '{nic}'.");
        }

        _logger.LogInformation("Prosumer {Nic} deactivated by Backoffice officer {Caller}.", nic, caller.Username);
        return ServiceResult<UserDto>.Ok(updated.ToDto(), "Prosumer account deactivated successfully.");
    }

    /// <summary>Raises a deactivation request from the mobile self service screen (rule R11).</summary>
    /// <param name="nic">NIC the request is raised for.</param>
    /// <param name="request">Optional reason the prosumer typed on the phone.</param>
    /// <param name="caller">Authenticated caller - the prosumer themselves, or staff acting for them.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated projection, or Forbidden / NotFound / Invalid.</returns>
    public async Task<ServiceResult<UserDto>> RequestDeactivationAsync(string nic, DeactivateRequest request, CurrentUser caller, CancellationToken ct = default)
    {
        // R11 - a prosumer may raise this only against their own NIC. Staff are allowed
        // to raise it on a prosumer's behalf, for instance after a phone call to support.
        if (IsForeignProsumerAccess(caller, nic))
        {
            return ServiceResult<UserDto>.Forbidden("You may only request deactivation of your own account.");
        }

        var existing = await _db.Users.Find(BuildNicFilter(nic)).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            return ServiceResult<UserDto>.NotFound($"No prosumer was found with NIC '{nic}'.");
        }

        // Nothing to request when the account is already out of service.
        if (existing.Status == AccountStatus.Deactivated)
        {
            return ServiceResult<UserDto>.Invalid("This account is already deactivated.");
        }

        // The Status field is deliberately NOT touched here. This endpoint only raises a
        // flag that puts the account into the back office work queue; under rule R5 only a
        // Backoffice officer may actually move an account to Deactivated (or back to Active),
        // so a prosumer can never change their own account state from the phone.
        var update = Builders<User>.Update
            .Set(u => u.DeactivationRequested, true)
            .Set(u => u.DeactivationRequestedAt, _clock.UtcNow)
            .Set(u => u.DeactivationReason, NormaliseReason(request?.Reason))
            .Set(u => u.UpdatedAt, _clock.UtcNow)
            .Set(u => u.UpdatedBy, caller.Username);

        var updated = await _db.Users.FindOneAndUpdateAsync(
            BuildNicFilter(nic),
            update,
            new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is null)
        {
            return ServiceResult<UserDto>.NotFound($"No prosumer was found with NIC '{nic}'.");
        }

        _logger.LogInformation("Deactivation requested for prosumer {Nic} by {Caller}.", nic, caller.Username);
        return ServiceResult<UserDto>.Ok(updated.ToDto(),
            "Deactivation request submitted. A Backoffice officer will review it.");
    }

    /// <summary>Permanently removes a prosumer record - Backoffice only, and only when no booking history exists.</summary>
    /// <param name="nic">NIC of the prosumer to delete.</param>
    /// <param name="caller">Authenticated caller - must be a Backoffice officer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True on deletion, or Forbidden / NotFound / Conflict.</returns>
    public async Task<ServiceResult<bool>> DeleteAsync(string nic, CurrentUser caller, CancellationToken ct = default)
    {
        // R10 - a hard delete is the most destructive act in the system, so it is Backoffice only.
        if (!caller.IsBackoffice)
        {
            return ServiceResult<bool>.Forbidden("Only a Backoffice officer may delete a prosumer account.");
        }

        var existing = await _db.Users.Find(BuildNicFilter(nic)).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            return ServiceResult<bool>.NotFound($"No prosumer was found with NIC '{nic}'.");
        }

        // Reservations reference a prosumer by NIC, so deleting an account with any history
        // would leave orphaned bookings whose owner can never be resolved again.
        var reservationFilter = Builders<EnergyReservation>.Filter.Regex(
            r => r.ProsumerNic,
            new BsonRegularExpression("^" + Regex.Escape(nic.Trim()) + "$", "i"));

        var reservationCount = await _db.Reservations.CountDocumentsAsync(reservationFilter, cancellationToken: ct);
        if (reservationCount > 0)
        {
            return ServiceResult<bool>.Conflict(
                "This prosumer has reservation history and cannot be deleted. Deactivate the account instead.");
        }

        var result = await _db.Users.DeleteOneAsync(BuildNicFilter(nic), ct);
        if (result.DeletedCount == 0)
        {
            return ServiceResult<bool>.NotFound($"No prosumer was found with NIC '{nic}'.");
        }

        _logger.LogWarning("Prosumer {Nic} permanently deleted by Backoffice officer {Caller}.", nic, caller.Username);
        return ServiceResult<bool>.Ok(true, "Prosumer deleted successfully.");
    }
}
