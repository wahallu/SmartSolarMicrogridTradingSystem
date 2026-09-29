/* =============================================================================
 * File        : ProsumersController.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Controllers / Prosumers
 * Description : HTTP surface for solar prosumer management, keyed by the National
 *               Identity Card number rather than by a Mongo identifier: listing,
 *               the pending activation queue, profile read and edit, the account
 *               life cycle (activate / deactivate / self service deactivation
 *               request raised from the Android app) and hard delete. The
 *               controller is deliberately THIN - it binds a DTO, calls exactly
 *               one IProsumerService method and maps the returned ServiceResult
 *               onto a status code. Rules R5, R10 and R11 are declared here only
 *               as coarse role gates and are truly enforced inside the FAT
 *               service, so neither the React client nor the Android client can
 *               bypass them.
 * Author      : Chathushka - Identity, Access Control & Local Session (SE4040 EAD)
 * Created     : 2026-09-04
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
/// Solar prosumer endpoints consumed by the React back office screens and by the
/// native Android application. Every action requires an authenticated caller; the
/// finer grained decisions - "a prosumer may only read or edit their own record"
/// (***RULE R11***) and "only a Backoffice officer may reactivate an account"
/// (***RULE R5***) - are made by the FAT service, which receives the caller
/// identity from the JWT and answers Forbidden when the rule is broken.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/prosumers")]
[Produces("application/json")]
public class ProsumersController : ControllerBase
{
    private readonly IProsumerService _prosumerService;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Captures the prosumer service and the caller accessor.</summary>
    /// <param name="prosumerService">The FAT service holding every prosumer rule.</param>
    /// <param name="currentUser">Resolves the caller of the request in flight from the JWT.</param>
    public ProsumersController(IProsumerService prosumerService, ICurrentUserAccessor currentUser)
    {
        // Dependencies are stored once; the controller itself keeps no state between requests.
        _prosumerService = prosumerService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Paged and filtered list of solar prosumers for the staff grids. Status, free
    /// text search and paging all arrive on the query string and are interpreted by
    /// the service, never here.
    /// </summary>
    /// <param name="query">Status, search text and paging options.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>One page of prosumer accounts together with the pager counters.</returns>
    [HttpGet("")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDto>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetProsumersAsync(
        [FromQuery] UserQuery query, CancellationToken ct)
    {
        // A single service call performs the filtering, the paging and the projection to UserDto.
        var result = await _prosumerService.GetProsumersAsync(query, ct);
        return Respond(result);
    }

    /// <summary>
    /// The pending activation queue - self registered prosumers who cannot log in
    /// yet (rule R6) and are waiting for a back office officer to approve them.
    /// </summary>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>Every prosumer whose account is still awaiting activation.</returns>
    /// <remarks>
    /// ROUTING: this literal "pending" route is declared BEFORE the "{nic}" route on
    /// purpose. ASP.NET Core scores a literal segment higher than a parameter
    /// segment, and keeping the two in this order also makes the intent obvious to a
    /// reader: "pending" is an endpoint name and must never be captured as a NIC.
    /// </remarks>
    [HttpGet("pending")]
    [Authorize(Roles = "Backoffice")]
    [ProducesResponseType(typeof(ApiResponse<List<UserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<UserDto>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<List<UserDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetPendingActivationsAsync(CancellationToken ct)
    {
        // Which status counts as "pending activation" is decided by the service, not by this controller.
        var result = await _prosumerService.GetPendingActivationsAsync(ct);
        return Respond(result);
    }

    /// <summary>
    /// Reads a single prosumer profile by NIC. Staff may read anybody; a prosumer
    /// caller may read only their own record (***RULE R11***), which the service
    /// decides by comparing the route NIC with the NIC claim on the bearer token.
    /// </summary>
    /// <param name="nic">National Identity Card number that identifies the prosumer.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The prosumer profile, or the failure envelope.</returns>
    /// <remarks>
    /// ROUTING: declared AFTER the literal "pending" route above so that a request to
    /// /api/v1/prosumers/pending is never bound with nic = "pending".
    /// </remarks>
    [HttpGet("{nic}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetByNicAsync(string nic, CancellationToken ct)
    {
        // The caller identity is handed to the service so it alone applies the self service rule R11.
        var caller = _currentUser.User;
        var result = await _prosumerService.GetByNicAsync(nic, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Creates a prosumer directly from the back office (***RULE R10***). NIC
    /// uniqueness (***RULE R7***) and password hashing are service concerns.
    /// </summary>
    /// <param name="request">NIC, contact details, password and an optional initial status.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The created prosumer, reported as HTTP 201.</returns>
    [HttpPost("")]
    [Authorize(Roles = "Backoffice")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateAsync(
        [FromBody] CreateProsumerRequest request, CancellationToken ct)
    {
        // Duplicate NIC detection (R7) and the default Pending status are decided inside the service.
        var caller = _currentUser.User;
        var result = await _prosumerService.CreateAsync(request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Edits a prosumer profile. A back office officer may edit anybody; a prosumer
    /// may edit only their own profile from the Android app (***RULE R11***).
    /// </summary>
    /// <param name="nic">National Identity Card number of the profile being edited.</param>
    /// <param name="request">The editable contact fields.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The updated profile, or the rule violation that stopped the edit.</returns>
    [HttpPut("{nic}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<UserDto>>> UpdateAsync(
        string nic, [FromBody] UpdateProsumerRequest request, CancellationToken ct)
    {
        // Which fields may change, and whether the caller owns this NIC, are service decisions (R11).
        var caller = _currentUser.User;
        var result = await _prosumerService.UpdateAsync(nic, request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Returns a prosumer account to Active. ***RULE R5*** - a deactivated account
    /// may be reactivated ONLY by a Backoffice officer, never by the prosumer and
    /// never by a Grid Operator. The role attribute below is the coarse gate and the
    /// service repeats the same check, so the rule holds on both clients.
    /// </summary>
    /// <param name="nic">National Identity Card number of the account to reactivate.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The reactivated prosumer, or the failure envelope.</returns>
    [HttpPatch("{nic}/activate")]
    [Authorize(Roles = "Backoffice")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> ActivateAsync(string nic, CancellationToken ct)
    {
        // No status comparison happens here - the service owns the R5 account life cycle transition.
        var caller = _currentUser.User;
        var result = await _prosumerService.ActivateAsync(nic, caller, ct);
        return Respond(result);
    }

    /// <summary>Blocks a prosumer account from logging in, optionally recording a reason.</summary>
    /// <param name="nic">National Identity Card number of the account to block.</param>
    /// <param name="request">Optional free text reason; may be omitted entirely.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The deactivated prosumer, or the failure envelope.</returns>
    /// <remarks>
    /// The body is OPTIONAL on purpose. The React back office sends this request with
    /// no body at all when the officer does not type a reason, so declaring the
    /// parameter as nullable and substituting an empty DeactivateRequest keeps that
    /// call a valid 200 instead of a model binding 400, while the service still
    /// receives a real object and never has to null check.
    /// </remarks>
    [HttpPatch("{nic}/deactivate")]
    [Authorize(Roles = "Backoffice")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> DeactivateAsync(
        string nic, [FromBody] DeactivateRequest? request, CancellationToken ct)
    {
        // A missing body becomes an empty reason so the service contract stays non nullable.
        var caller = _currentUser.User;
        var result = await _prosumerService.DeactivateAsync(nic, request ?? new DeactivateRequest(), caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Raises the prosumer's own deactivation request from the Android application.
    /// This never deactivates the account by itself: it flags the record so the back
    /// office can act on it, which is what keeps ***RULE R5*** intact - reactivation,
    /// and the deactivation itself, remain back office decisions.
    /// </summary>
    /// <param name="nic">National Identity Card number of the requesting prosumer.</param>
    /// <param name="request">Optional free text reason; may be omitted entirely.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The flagged profile, or the rule violation that stopped the request.</returns>
    [HttpPost("{nic}/request-deactivation")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> RequestDeactivationAsync(
        string nic, [FromBody] DeactivateRequest? request, CancellationToken ct)
    {
        // Whether this caller owns the NIC is checked by the service under rule R11, not here.
        var caller = _currentUser.User;
        var result = await _prosumerService.RequestDeactivationAsync(
            nic, request ?? new DeactivateRequest(), caller, ct);
        return Respond(result);
    }

    /// <summary>Permanently removes a prosumer record - Backoffice only (rule R10).</summary>
    /// <param name="nic">National Identity Card number of the record to remove.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>True on deletion, or the conflict that stopped it.</returns>
    [HttpDelete("{nic}")]
    [Authorize(Roles = "Backoffice")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteAsync(string nic, CancellationToken ct)
    {
        // Whether booking history blocks the delete is a service decision, evaluated against the caller.
        var caller = _currentUser.User;
        var result = await _prosumerService.DeleteAsync(nic, caller, ct);
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
