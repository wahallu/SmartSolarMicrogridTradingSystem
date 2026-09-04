/* =============================================================================
 * File        : SlotsController.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Controllers / Slots
 * Description : HTTP surface for the bookable energy windows published at a
 *               microgrid node: browsing and filtering slots, the "only what is
 *               bookable right now" projection used by the Android booking
 *               screen, single slot creation, bulk generation over a date range,
 *               editing, blocking and deletion. The controller is deliberately
 *               THIN - it binds a DTO, calls exactly one ISlotService method and
 *               maps the returned ServiceResult onto a status code. Rule R8
 *               (a slot is bookable only while it has free capacity, is not
 *               Blocked and its node is Active) is decided entirely inside the
 *               FAT service, so neither the React grid nor the phone re-derives
 *               availability.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSolar.Api.Common;
using SmartSolar.Api.DTOs.Slots;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Controllers;

/// <summary>
/// Energy booking slot endpoints consumed by the React operator screens and by
/// the Android booking flow. Reading slots is open to any authenticated caller
/// because a prosumer must be able to see what is on offer before booking, while
/// every write is restricted to Backoffice officers and Grid Operators - slots
/// are node infrastructure, not something a prosumer may publish. Those role
/// gates are declared here and repeated inside the service, so the rule holds
/// even if this controller is ever hosted elsewhere.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/slots")]
[Produces("application/json")]
public class SlotsController : ControllerBase
{
    private readonly ISlotService _slotService;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Captures the slot service and the caller accessor.</summary>
    /// <param name="slotService">The FAT service holding every slot rule and projection.</param>
    /// <param name="currentUser">Resolves the caller of the request in flight from the JWT.</param>
    public SlotsController(ISlotService slotService, ICurrentUserAccessor currentUser)
    {
        // Dependencies are stored once; the controller itself keeps no state between requests.
        _slotService = slotService;
        _currentUser = currentUser;
    }

    // =========================================================================
    // Queries
    //
    // ROUTING NOTE: the literal routes "available" and "generate" are declared
    // BEFORE the "{id}" routes on purpose. ASP.NET Core scores a literal segment
    // higher than a parameter segment, so ordering is not strictly required by
    // the router - but keeping the literals first makes the precedence obvious
    // to a human reader and prevents anyone later "fixing" the file into a state
    // where GET /api/v1/slots/available is read as GET /api/v1/slots/{id} with
    // id = "available", which would answer 404 for a perfectly valid request.
    // =========================================================================

    /// <summary>
    /// Paged and filtered list of booking slots for the operator grid. Node, date,
    /// status, slot type and the upcoming-only switch all arrive on the query
    /// string and are interpreted by the service, never here.
    /// </summary>
    /// <param name="query">Station, date, status, slot type and paging options.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>One page of slots together with the pager counters.</returns>
    [HttpGet("")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SlotDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SlotDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SlotDto>>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<SlotDto>>>> GetSlotsAsync(
        [FromQuery] SlotQuery query, CancellationToken ct)
    {
        // A single service call performs the filtering, the paging and the projection to SlotDto.
        var result = await _slotService.GetSlotsAsync(query, ct);
        return Respond(result);
    }

    /// <summary>
    /// Slots at one node that the API would accept a booking for right now.
    /// ***RULE R8*** - free capacity, Available status, a start instant still in
    /// the future and an Active parent node are all evaluated inside the service,
    /// so the Android booking list simply renders whatever comes back.
    /// </summary>
    /// <param name="stationId">Mongo identifier of the microgrid node to inspect.</param>
    /// <param name="date">Optional calendar day filter; all upcoming days when omitted.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The bookable slots, ordered by the service.</returns>
    /// <remarks>
    /// Declared before the "{id}" route so the literal segment "available" is never
    /// mistaken for a slot identifier.
    /// </remarks>
    [HttpGet("available")]
    [ProducesResponseType(typeof(ApiResponse<List<SlotDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<SlotDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<List<SlotDto>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<List<SlotDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<SlotDto>>>> GetAvailableAsync(
        [FromQuery] string stationId, [FromQuery] DateTime? date, CancellationToken ct)
    {
        // No availability arithmetic happens here - the whole of rule R8 is a service decision.
        var result = await _slotService.GetAvailableAsync(stationId, date, ct);
        return Respond(result);
    }

    /// <summary>Reads one booking slot by its MongoDB identifier.</summary>
    /// <param name="id">Mongo ObjectId of the slot, as a string.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The slot, or the not found envelope when the id is unknown or malformed.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SlotDto>>> GetByIdAsync(string id, CancellationToken ct)
    {
        // A malformed identifier is answered as NotFound by the service, so no guard is needed here.
        var result = await _slotService.GetByIdAsync(id, ct);
        return Respond(result);
    }

    // =========================================================================
    // Commands - Backoffice officers and Grid Operators only
    // =========================================================================

    /// <summary>
    /// Publishes a single booking slot at a node. Slot code allocation, the
    /// wall-clock to UTC conversion and the uniqueness of the code within the
    /// node and the date are all handled by the service.
    /// </summary>
    /// <param name="request">Node, date, time window, slot type, capacity and battery bay.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The created slot, reported as HTTP 201.</returns>
    [HttpPost("")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SlotDto>>> CreateAsync(
        [FromBody] CreateSlotRequest request, CancellationToken ct)
    {
        // The caller identity is forwarded so the service can stamp the audit fields and re-check the role.
        var caller = _currentUser.User;
        var result = await _slotService.CreateAsync(request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Lays out a whole date range of slots for one node in a single call, so an
    /// operator does not have to create fourteen days of windows by hand. Which
    /// days are skipped because the node is closed (rule R12 territory) and which
    /// windows already exist is decided by the service, which reports the counts.
    /// </summary>
    /// <param name="request">Node, date range, hour range, duration, type, capacity and bay count.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>A summary of how many slots were created and how many were skipped.</returns>
    /// <remarks>
    /// Declared before the "{id}" route so the literal segment "generate" is never
    /// mistaken for a slot identifier.
    /// </remarks>
    [HttpPost("generate")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<GenerateSlotsResultDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<GenerateSlotsResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<GenerateSlotsResultDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<GenerateSlotsResultDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<GenerateSlotsResultDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<GenerateSlotsResultDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<GenerateSlotsResultDto>>> GenerateAsync(
        [FromBody] GenerateSlotsRequest request, CancellationToken ct)
    {
        // The generator loop, the closed day skipping and the duplicate detection all live in the service.
        var caller = _currentUser.User;
        var result = await _slotService.GenerateAsync(request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Edits an existing booking slot. The service refuses changes that would
    /// strand reservations already taken against the slot, so no comparison of
    /// bookedCount against the new maximum is ever performed here.
    /// </summary>
    /// <param name="id">Mongo identifier of the slot to edit.</param>
    /// <param name="request">New time window, slot type, capacity, bay, maximum and status.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The updated slot, or the rule violation that stopped the edit.</returns>
    [HttpPut("{id}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SlotDto>>> UpdateAsync(
        string id, [FromBody] UpdateSlotRequest request, CancellationToken ct)
    {
        // Which fields may change, and whether the change is safe for existing bookings, are service decisions.
        var caller = _currentUser.User;
        var result = await _slotService.UpdateAsync(id, request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Blocks a slot so it stops accepting new bookings - the maintenance switch a
    /// grid operator uses when a battery bay goes offline. Existing reservations
    /// are untouched; only future bookings are prevented, which is the Blocked
    /// branch of rule R8.
    /// </summary>
    /// <param name="id">Mongo identifier of the slot to block.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The blocked slot, or the failure envelope.</returns>
    [HttpPatch("{id}/block")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<SlotDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SlotDto>>> BlockAsync(string id, CancellationToken ct)
    {
        // No status comparison happens here - the service owns the slot life cycle transition.
        var caller = _currentUser.User;
        var result = await _slotService.BlockAsync(id, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Permanently removes a booking slot. The service refuses the delete while the
    /// slot still carries bookings, because those reservations reference it.
    /// </summary>
    /// <param name="id">Mongo identifier of the slot to remove.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>True on deletion, or the conflict that stopped it.</returns>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteAsync(string id, CancellationToken ct)
    {
        // The "slot still has bookings" guard is evaluated in the service, never in this controller.
        var caller = _currentUser.User;
        var result = await _slotService.DeleteAsync(id, caller, ct);
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
