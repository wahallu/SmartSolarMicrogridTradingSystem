/* =============================================================================
 * File        : UsersController.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Controllers / Users Controller
 * Description : Handles administration of Backoffice and Grid Operator
 *               accounts.
 * Author      : IT22049872 - Chathushka
 * Created     : 2026-09-03
 * =============================================================================
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSolar.Api.Common;
using SmartSolar.Api.DTOs.Users;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Controllers;

/// <summary>
/// Web user administration endpoints consumed by the React back office screens.
/// ***RULE R10*** - every action on this controller is restricted to the
/// Backoffice role, because only a back office officer may create or modify web
/// user accounts and activate or deactivate them. The attribute below is the
/// coarse gate; the service repeats the check so the rule holds even if this
/// controller is ever reused by another host.
/// </summary>
[ApiController]
[Authorize(Roles = "Backoffice")]
[Route("api/v1/users")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Captures the user administration service and the caller accessor.</summary>
    /// <param name="userService">The FAT service holding every user administration rule.</param>
    /// <param name="currentUser">Resolves the caller of the request in flight from the JWT.</param>
    public UsersController(IUserService userService, ICurrentUserAccessor currentUser)
    {
        // Dependencies are stored once; the controller itself keeps no state between requests.
        _userService = userService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Paged and filtered list of web user accounts for the administration grid.
    /// Role, status, free text search and paging all arrive on the query string
    /// and are interpreted by the service, never here.
    /// </summary>
    /// <param name="query">Role, status, search text and paging options.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>One page of accounts together with the pager counters.</returns>
    [HttpGet("")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDto>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetUsersAsync(
        [FromQuery] UserQuery query, CancellationToken ct)
    {
        // A single service call performs the filtering, the paging and the projection to UserDto.
        var result = await _userService.GetUsersAsync(query, ct);
        return Respond(result);
    }

    /// <summary>Reads one web user account by its MongoDB identifier.</summary>
    /// <param name="id">Mongo ObjectId of the account, as a string.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The account, or the not found envelope when the id is unknown.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetByIdAsync(string id, CancellationToken ct)
    {
        // A malformed identifier is answered as NotFound by the service, so no guard is needed here.
        var result = await _userService.GetByIdAsync(id, ct);
        return Respond(result);
    }

    /// <summary>
    /// Creates a Backoffice or Grid Operator account (rule R10). Solar prosumers are
    /// never created through this route - they self register or are added through
    /// the /prosumers endpoints, because their business key is the NIC.
    /// </summary>
    /// <param name="request">Login name, contact details, password and role.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The created account, reported as HTTP 201.</returns>
    [HttpPost("")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateUserAsync(
        [FromBody] CreateUserRequest request, CancellationToken ct)
    {
        // Uniqueness of the username and e-mail, password hashing and the R10 role check all live in the service.
        var caller = _currentUser.User;
        var result = await _userService.CreateUserAsync(request, caller, ct);
        return Respond(result);
    }

    /// <summary>Edits an existing web user account.</summary>
    /// <param name="id">Mongo identifier of the account to edit.</param>
    /// <param name="request">New contact details, role, assigned node and status.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The updated account, or the rule violation that stopped the edit.</returns>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateUserAsync(
        string id, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        // Which fields may change, and whether the new e-mail is already taken, are service decisions.
        var caller = _currentUser.User;
        var result = await _userService.UpdateUserAsync(id, request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Returns a blocked account to Active. ***RULE R5*** - a deactivated account can
    /// only be restored by a Backoffice officer, which the class level role gate and
    /// the service together guarantee.
    /// </summary>
    /// <param name="id">Mongo identifier of the account to reactivate.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The reactivated account, or the failure envelope.</returns>
    [HttpPatch("{id}/activate")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> ActivateAsync(string id, CancellationToken ct)
    {
        // No status comparison happens here - the service owns the account life cycle transition.
        var caller = _currentUser.User;
        var result = await _userService.ActivateAsync(id, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Blocks an account from logging in, optionally recording a reason.
    /// </summary>
    /// <param name="id">Mongo identifier of the account to block.</param>
    /// <param name="request">Optional free text reason; may be omitted entirely.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The deactivated account, or the failure envelope.</returns>
    /// <remarks>
    /// The body is OPTIONAL on purpose. The React back office sends this request with
    /// no body at all when the officer does not type a reason, and an Android client
    /// behind a proxy may strip an empty body as well. Declaring the parameter as
    /// nullable and substituting an empty DeactivateRequest keeps that call a valid
    /// 200 instead of a model binding 400, while the service still receives a real
    /// object and never has to null check.
    /// </remarks>
    [HttpPatch("{id}/deactivate")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> DeactivateAsync(
        string id, [FromBody] DeactivateRequest? request, CancellationToken ct)
    {
        // A missing body becomes an empty reason so the service contract stays non nullable.
        var caller = _currentUser.User;
        var result = await _userService.DeactivateAsync(id, request ?? new DeactivateRequest(), caller, ct);
        return Respond(result);
    }

    /// <summary>Sets a new password on a user's behalf (rule R10).</summary>
    /// <param name="id">Mongo identifier of the account whose password is replaced.</param>
    /// <param name="request">The replacement password chosen by the officer.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>True when the password was replaced, otherwise the failure envelope.</returns>
    [HttpPost("{id}/reset-password")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> ResetPasswordAsync(
        string id, [FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        // Salting and hashing the replacement password is a service concern, never a controller one.
        var caller = _currentUser.User;
        var result = await _userService.ResetPasswordAsync(id, request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Permanently removes a web user account. The service refuses to delete the
    /// caller's own account so an officer cannot lock the back office out of itself.
    /// </summary>
    /// <param name="id">Mongo identifier of the account to remove.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>True on deletion, or the conflict that stopped it.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteAsync(string id, CancellationToken ct)
    {
        // The "cannot delete yourself" guard is evaluated in the service against the caller identity.
        var caller = _currentUser.User;
        var result = await _userService.DeleteAsync(id, caller, ct);
        return Respond(result);
    }

    // =========================================================================
    // Private helper
    // =========================================================================

    /// <summary>Translates a service outcome into the matching HTTP status code and envelope.</summary>
    /// <typeparam name="T">Payload type carried by the result.</typeparam>
    /// <param name="result">The outcome produced by the FAT service layer.</param>
    /// <returns>The uniform ApiResponse envelope wrapped in the correct status code.</returns>
    private ActionResult<ApiResponse<T>> Respond<T>(ServiceResult<T> result)
    {
        // Translates the service layer outcome into the matching HTTP status code and envelope.
        var body = result.IsSuccess
            ? ApiResponse<T>.Ok(result.Value, result.Message)
            : ApiResponse<T>.Fail(result.Message, result.ErrorCode, result.Errors);

        return result.Status switch
        {
            ResultStatus.Success         => Ok(body),
            ResultStatus.Created         => StatusCode(StatusCodes.Status201Created, body),
            ResultStatus.ValidationError => BadRequest(body),
            ResultStatus.NotFound        => NotFound(body),
            ResultStatus.Conflict        => Conflict(body),
            ResultStatus.Forbidden       => StatusCode(StatusCodes.Status403Forbidden, body),
            ResultStatus.Unauthorized    => Unauthorized(body),
            _                            => StatusCode(StatusCodes.Status500InternalServerError, body),
        };
    }
}
