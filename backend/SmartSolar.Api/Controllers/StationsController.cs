/* =============================================================================
 * File        : StationsController.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Controllers / Stations Controller
 * Description : Exposes station search, map and maintenance endpoints.
 * Author      : IT22207272 - Nisal
 * Created     : 2026-09-10
 * =============================================================================
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSolar.Api.Common;
using SmartSolar.Api.DTOs.Stations;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Controllers;

/// <summary>
/// Microgrid node endpoints consumed by the React back office screens and by the
/// Android map screen. Reading a node is open to any authenticated caller,
/// because a prosumer must be able to browse the nodes they may book at, while
/// every write is gated per action: ***RULE R10*** restricts registration,
/// editing and the activate / deactivate / delete life cycle to the Backoffice
/// role, and schedule plus battery bay maintenance is additionally granted to a
/// Grid Operator because those are day to day operational tasks. The attributes
/// below are only the coarse gate - the service repeats every check, so the rules
/// hold even if this controller is ever reused by another host.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/stations")]
[Produces("application/json")]
public class StationsController : ControllerBase
{
    private readonly IStationService _stationService;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Captures the microgrid node service and the caller accessor.</summary>
    /// <param name="stationService">The FAT service holding every microgrid node rule.</param>
    /// <param name="currentUser">Resolves the caller of the request in flight from the JWT.</param>
    public StationsController(IStationService stationService, ICurrentUserAccessor currentUser)
    {
        // Dependencies are stored once; the controller itself keeps no state between requests.
        _stationService = stationService;
        _currentUser = currentUser;
    }

    // =========================================================================
    // Queries - any authenticated caller
    // =========================================================================

    /// <summary>
    /// Paged and filtered list of microgrid nodes for the administration grid and
    /// for the node picker on both clients. Status, city, free text search and
    /// paging all arrive on the query string and are interpreted by the service,
    /// never here.
    /// </summary>
    /// <param name="query">Status, city, search text and paging options.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>One page of nodes together with the pager counters.</returns>
    [HttpGet("")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<StationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<StationDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<StationDto>>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<StationDto>>>> GetStationsAsync(
        [FromQuery] StationQuery query, CancellationToken ct)
    {
        // A single service call performs the filtering, the paging and the projection to StationDto.
        var result = await _stationService.GetStationsAsync(query, ct);
        return Respond(result);
    }

    /// <summary>
    /// Active microgrid nodes within a radius of a GPS point, nearest first - the
    /// map feature used by the Android Google Maps screen and the React map.
    /// The great circle distance in kilometres is computed by the service and
    /// shipped as a finished number, so neither client re-derives it.
    /// </summary>
    /// <param name="query">Caller position, optional radius override and a result limit.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>Nodes ordered by ascending distance, each carrying its distanceKm.</returns>
    /// <remarks>
    /// ROUTING - this literal "nearby" route MUST be declared before the "{id}"
    /// route below. Were it declared afterwards the word "nearby" would be a
    /// perfectly good candidate for the {id} parameter and the request could be
    /// matched as a lookup for a node whose identifier is literally "nearby",
    /// which would answer 404 instead of returning the map projection.
    /// </remarks>
    [HttpGet("nearby")]
    [ProducesResponseType(typeof(ApiResponse<List<NearbyStationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<NearbyStationDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<List<NearbyStationDto>>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<List<NearbyStationDto>>>> GetNearbyAsync(
        [FromQuery] NearbyStationQuery query, CancellationToken ct)
    {
        // The radius default, the Haversine distances and the ordering are all service concerns.
        var result = await _stationService.GetNearbyAsync(query, ct);
        return Respond(result);
    }

    /// <summary>Reads one microgrid node by its MongoDB identifier.</summary>
    /// <param name="id">Mongo ObjectId of the node, as a string.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The node, or the not found envelope when the id is unknown or malformed.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<StationDto>>> GetByIdAsync(string id, CancellationToken ct)
    {
        // A malformed identifier is answered as NotFound by the service, so no guard is needed here.
        var result = await _stationService.GetByIdAsync(id, ct);
        return Respond(result);
    }

    // =========================================================================
    // Commands - back office and operator maintenance
    // =========================================================================

    /// <summary>
    /// Registers a new microgrid node with its GPS position, kWh capacity and
    /// battery bay counts (rule R10 - Backoffice only).
    /// </summary>
    /// <param name="request">Node master data, operating hours and optional weekly schedule.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The created node, reported as HTTP 201.</returns>
    [HttpPost("")]
    [Authorize(Roles = "Backoffice")]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<StationDto>>> CreateAsync(
        [FromBody] CreateStationRequest request, CancellationToken ct)
    {
        // Station code uniqueness, the battery bay sanity check and the default schedule all live in the service.
        var caller = _currentUser.User;
        var result = await _stationService.CreateAsync(request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Edits an existing microgrid node (rule R10 - Backoffice only). The station
    /// code itself is absent from the payload because it is the immutable business
    /// key that slots and reservations were denormalised against.
    /// </summary>
    /// <param name="id">Mongo identifier of the node to edit.</param>
    /// <param name="request">New master data, position, capacity and operating hours.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The updated node, or the rule violation that stopped the edit.</returns>
    [HttpPut("{id}")]
    [Authorize(Roles = "Backoffice")]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<StationDto>>> UpdateAsync(
        string id, [FromBody] UpdateStationRequest request, CancellationToken ct)
    {
        // Refreshing the denormalised node code and name on slots and reservations is a service concern.
        var caller = _currentUser.User;
        var result = await _stationService.UpdateAsync(id, request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Replaces the seven day operating schedule of a node. Granted to Grid
    /// Operators as well as the back office because opening times change on site.
    /// The schedule is what rule R12 is later evaluated against when a booking is
    /// placed, so the service validates its shape and its times, not this method.
    /// </summary>
    /// <param name="id">Mongo identifier of the node.</param>
    /// <param name="request">Default operating hours plus exactly seven day rows.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The updated node, or the validation failure that stopped the change.</returns>
    [HttpPatch("{id}/schedule")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<StationDto>>> UpdateScheduleAsync(
        string id, [FromBody] UpdateScheduleRequest request, CancellationToken ct)
    {
        // Seven distinct days and "close must follow open" are checked by the service, never here.
        var caller = _currentUser.User;
        var result = await _stationService.UpdateScheduleAsync(id, request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Updates the number of free battery bays reported by a node. This is the
    /// figure a Grid Operator maintains from the mobile app during a shift, which
    /// is why the role gate is wider than the back office only routes.
    /// </summary>
    /// <param name="id">Mongo identifier of the node.</param>
    /// <param name="request">The new number of free battery bays.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The updated node, or the validation failure that stopped the change.</returns>
    [HttpPatch("{id}/availability")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<StationDto>>> UpdateAvailabilityAsync(
        string id, [FromBody] UpdateAvailabilityRequest request, CancellationToken ct)
    {
        // Whether the reported figure fits inside the installed bay count is a service decision.
        var caller = _currentUser.User;
        var result = await _stationService.UpdateAvailabilityAsync(id, request, caller, ct);
        return Respond(result);
    }

    /// <summary>Returns a microgrid node to service (rule R10 - Backoffice only).</summary>
    /// <param name="id">Mongo identifier of the node to reactivate.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The reactivated node, or the failure envelope.</returns>
    [HttpPatch("{id}/activate")]
    [Authorize(Roles = "Backoffice")]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<StationDto>>> ActivateAsync(string id, CancellationToken ct)
    {
        // No status comparison happens here - the service owns the node life cycle transition.
        var caller = _currentUser.User;
        var result = await _stationService.ActivateAsync(id, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Takes a microgrid node out of service (rule R10 - Backoffice only).
    /// </summary>
    /// <param name="id">Mongo identifier of the node to take out of service.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The deactivated node, or the conflict envelope that stopped it.</returns>
    /// <remarks>
    /// ***RULE R4*** - the service refuses to deactivate a node that still holds
    /// reservations in status Pending or Approved whose start instant has not
    /// passed, and answers HTTP 409 with the error code STATION_HAS_ACTIVE_RES
    /// carrying the offending count in its message. That 409 is an EXPECTED
    /// outcome, not a server fault: the React back office surfaces the message to
    /// the officer so they can cancel or complete those bookings first, then retry.
    /// The rule itself is evaluated only in the service - this method makes no
    /// status comparison and no date arithmetic of its own.
    /// </remarks>
    [HttpPatch("{id}/deactivate")]
    [Authorize(Roles = "Backoffice")]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<StationDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<StationDto>>> DeactivateAsync(string id, CancellationToken ct)
    {
        // The R4 guard is counted and enforced inside the service; the controller only reports its verdict.
        var caller = _currentUser.User;
        var result = await _stationService.DeactivateAsync(id, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Permanently removes a microgrid node together with its slots. The service
    /// refuses the delete once any reservation - past or future - references the
    /// node, because that history is part of the audit trail.
    /// </summary>
    /// <param name="id">Mongo identifier of the node to remove.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>True on deletion, or the conflict that stopped it.</returns>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Backoffice")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteAsync(string id, CancellationToken ct)
    {
        // Cascading the slot removal and blocking the delete on reservation history are service concerns.
        var caller = _currentUser.User;
        var result = await _stationService.DeleteAsync(id, caller, ct);
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
