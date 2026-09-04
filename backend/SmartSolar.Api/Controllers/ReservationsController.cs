/* =============================================================================
 * File        : ReservationsController.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Controllers / Reservations
 * Description : HTTP surface for the whole power trading reservation workflow:
 *               staff search, a prosumer's own bookings, booking history, the
 *               live dashboard counters, creation, modification, cancellation,
 *               approval with QR issue, rejection, QR retrieval, QR verification
 *               and completion of the energy transfer. The controller is
 *               deliberately THIN - it binds a DTO, calls exactly one
 *               IReservationService method and maps the returned ServiceResult
 *               onto a status code. Every rule that governs a reservation lives
 *               in the FAT service: R1 (7 day window), R2 / R3 (12 hour notice),
 *               R9 (QR scan window), R11 (a prosumer may only touch their own
 *               data), R12 (node closed) and R13 (duplicate booking). Neither
 *               the React screens nor the Android app re-derives any of them.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSolar.Api.Common;
using SmartSolar.Api.DTOs.Reservations;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Controllers;

/// <summary>
/// Energy reservation endpoints shared by the React back office screens, the
/// React grid operator console and the Android prosumer application. The class
/// level [Authorize] means no reservation is ever readable anonymously; the
/// finer grained decisions - "is this caller the owner of this booking", "may
/// this booking still be cancelled" - are taken inside the service, which is
/// handed the CurrentUser so it can apply rule R11 itself. Only the two coarse
/// staff-only gates (the search grid and the approve / reject / scan actions)
/// are declared here with [Authorize(Roles = ...)], and the service repeats the
/// same check so the rule survives if this controller is ever re-hosted.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/reservations")]
[Produces("application/json")]
public class ReservationsController : ControllerBase
{
    private readonly IReservationService _reservationService;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Captures the reservation service and the caller accessor.</summary>
    /// <param name="reservationService">The FAT service holding every reservation rule.</param>
    /// <param name="currentUser">Resolves the caller of the request in flight from the JWT.</param>
    public ReservationsController(IReservationService reservationService, ICurrentUserAccessor currentUser)
    {
        // Dependencies are stored once; the controller itself keeps no state between requests.
        _reservationService = reservationService;
        _currentUser = currentUser;
    }

    // =========================================================================
    // LITERAL ROUTES - THESE MUST STAY ABOVE EVERY "{id}" ROUTE
    //
    // *** CRITICAL ROUTING NOTE ***
    // "my", "history", "dashboard", "verify-qr" and "complete" are constant path
    // segments that sit in exactly the same position as the reservation
    // identifier in "{id}". They are therefore declared FIRST, before any route
    // containing "{id}". If somebody later moves "{id}" above them, a request
    // for GET /api/v1/reservations/my would be matched as
    // GET /api/v1/reservations/{id} with id = "my", the service would fail to
    // parse "my" as a Mongo ObjectId and the endpoint would answer 404 for a
    // perfectly valid request. Keep the literals first.
    // =========================================================================

    /// <summary>
    /// Paged and filtered reservation list for the staff search screens. NIC,
    /// node, status, date range, free text and paging all arrive on the query
    /// string and are interpreted by the service, never here.
    /// </summary>
    /// <param name="query">NIC, station, status, from/to dates, search text and paging.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>One page of reservations together with the pager counters.</returns>
    [HttpGet("")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ReservationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ReservationDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ReservationDto>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ReservationDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<ReservationDto>>>> GetReservationsAsync(
        [FromQuery] ReservationQuery query, CancellationToken ct)
    {
        // The caller is forwarded so the service can narrow the result set for a grid operator if it chooses to.
        var caller = _currentUser.User;
        var result = await _reservationService.GetReservationsAsync(query, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// The signed in prosumer's own bookings, sliced by scope. Which statuses and
    /// which time boundaries belong to "current", "pending" or "history" is a
    /// service decision, so the Android tabs simply render what comes back.
    /// </summary>
    /// <param name="scope">Current, Pending, History or All; defaults to All.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The caller's reservations for the requested slice.</returns>
    /// <remarks>
    /// LITERAL ROUTE - declared before "{id}" so the segment "my" is never read as
    /// a reservation identifier.
    /// </remarks>
    [HttpGet("my")]
    [ProducesResponseType(typeof(ApiResponse<List<ReservationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<ReservationDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<List<ReservationDto>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<List<ReservationDto>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<List<ReservationDto>>>> GetMyReservationsAsync(
        [FromQuery] ReservationScope scope = ReservationScope.All, CancellationToken ct = default)
    {
        // The owning NIC is taken from the token by the service (rule R11), never from the query string.
        var caller = _currentUser.User;
        var result = await _reservationService.GetMyReservationsAsync(scope, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Full booking history for one NIC. Staff may pass any NIC; a prosumer caller
    /// is forced onto their own NIC by the service under rule R11, so passing
    /// somebody else's number simply returns the caller's own history or a 403.
    /// </summary>
    /// <param name="nic">NIC to report on; ignored for a prosumer caller.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>Every reservation ever placed for the resolved NIC.</returns>
    /// <remarks>
    /// LITERAL ROUTE - declared before "{id}" so the segment "history" is never read
    /// as a reservation identifier.
    /// </remarks>
    [HttpGet("history")]
    [ProducesResponseType(typeof(ApiResponse<List<ReservationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<List<ReservationDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<List<ReservationDto>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<List<ReservationDto>>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<List<ReservationDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<List<ReservationDto>>>> GetHistoryAsync(
        [FromQuery] string? nic, CancellationToken ct)
    {
        // No role branching happens here - deciding whose history may be read is rule R11, owned by the service.
        var caller = _currentUser.User;
        var result = await _reservationService.GetHistoryAsync(nic, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Live dashboard counters plus the next few upcoming bookings, optionally
    /// scoped to one node and/or one prosumer. Every count - including the staff
    /// only prosumer activation figures, which are zeroed for a prosumer caller -
    /// is aggregated in the service so both dashboards show identical numbers.
    /// </summary>
    /// <param name="stationId">Optional node filter applied to every counter.</param>
    /// <param name="nic">Optional prosumer filter; forced to self for a prosumer caller.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The populated dashboard payload.</returns>
    /// <remarks>
    /// LITERAL ROUTE - declared before "{id}" so the segment "dashboard" is never
    /// read as a reservation identifier.
    /// </remarks>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(ApiResponse<DashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<DashboardDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<DashboardDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<DashboardDto>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<DashboardDto>>> GetDashboardAsync(
        [FromQuery] string? stationId, [FromQuery] string? nic, CancellationToken ct)
    {
        // The controller performs no counting and no scoping; it forwards two optional filters and the caller.
        var caller = _currentUser.User;
        var result = await _reservationService.GetDashboardAsync(stationId, nic, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Places a new energy reservation against a published slot. ***RULES R1, R8,
    /// R12 and R13*** - the seven day booking window, slot capacity, the node's
    /// weekly schedule and the "one active booking per slot per prosumer" guard
    /// are all evaluated in the service. A prosumer caller cannot book for anybody
    /// else: the service overwrites ProsumerNic with the NIC on the token (R11).
    /// </summary>
    /// <param name="request">Node, slot, requested energy and optional notes.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The created reservation, reported as HTTP 201.</returns>
    /// <remarks>
    /// LITERAL ROUTE - kept in the literal block so the route table reads in the
    /// intended order.
    /// </remarks>
    [HttpPost("")]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> CreateAsync(
        [FromBody] CreateReservationRequest request, CancellationToken ct)
    {
        // No date arithmetic here: whether the slot starts inside the seven day window is rule R1, a service decision.
        var caller = _currentUser.User;
        var result = await _reservationService.CreateAsync(request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Validates a token read out of a scanned QR image. ***RULE R9*** - the token
    /// must exist, the reservation must be Approved and the current instant must
    /// fall inside the grace window around the slot. The scanner must not trust any
    /// other field in the QR payload: this endpoint is the authority.
    /// </summary>
    /// <param name="request">The token lifted from the scanned QR payload.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The reservation behind the token, or the R9 failure that rejected it.</returns>
    /// <remarks>
    /// LITERAL ROUTE - declared before "{id}" so the segment "verify-qr" is never
    /// read as a reservation identifier.
    /// </remarks>
    [HttpPost("verify-qr")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> VerifyQrAsync(
        [FromBody] VerifyQrRequest request, CancellationToken ct)
    {
        // The whole of rule R9 - token lookup, status check and scan window - is evaluated inside the service.
        var caller = _currentUser.User;
        var result = await _reservationService.VerifyQrAsync(request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Finalises an energy transfer after a successful scan: the operator records
    /// the kWh actually delivered and the reservation moves to Completed. The same
    /// rule R9 checks are re-applied by the service, so a token that has drifted
    /// out of its window between the scan and the confirmation is still refused.
    /// </summary>
    /// <param name="request">Token, delivered energy in kWh and optional operator notes.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The completed reservation, or the rule violation that stopped it.</returns>
    /// <remarks>
    /// LITERAL ROUTE - declared before "{id}" so the segment "complete" is never
    /// read as a reservation identifier.
    /// </remarks>
    [HttpPost("complete")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> CompleteAsync(
        [FromBody] CompleteReservationRequest request, CancellationToken ct)
    {
        // The status transition to Completed and the release of the slot capacity both belong to the service.
        var caller = _currentUser.User;
        var result = await _reservationService.CompleteAsync(request, caller, ct);
        return Respond(result);
    }

    // =========================================================================
    // IDENTIFIER ROUTES - everything below carries "{id}"
    //
    // Nothing with a constant first segment may be added below this line. See the
    // critical routing note above: a literal declared after "{id}" is at risk of
    // being swallowed by the parameter route.
    // =========================================================================

    /// <summary>
    /// Reads one reservation. Visible to the owning prosumer and to staff only -
    /// rule R11 is applied by the service against the caller passed to it.
    /// </summary>
    /// <param name="id">Mongo ObjectId of the reservation, as a string.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The reservation, or the not found / forbidden envelope.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> GetByIdAsync(string id, CancellationToken ct)
    {
        // A malformed identifier is answered as NotFound by the service, so no guard is needed in the controller.
        var caller = _currentUser.User;
        var result = await _reservationService.GetByIdAsync(id, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Returns the QR material for an approved reservation: the token, the exact
    /// payload string and a base64 PNG with no data URI prefix, ready for the
    /// Android app to show on screen. Only an approved booking has QR material,
    /// which is why the service, not this method, decides whether to hand it over.
    /// </summary>
    /// <param name="id">Mongo identifier of the reservation whose QR is wanted.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The QR payload and rendered image, or the failure envelope.</returns>
    [HttpGet("{id}/qr")]
    [ProducesResponseType(typeof(ApiResponse<QrCodeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<QrCodeDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<QrCodeDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<QrCodeDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<QrCodeDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<QrCodeDto>>> GetQrCodeAsync(string id, CancellationToken ct)
    {
        // Ownership (R11) and the "is it approved yet" check are both made by the service before the PNG is rendered.
        var caller = _currentUser.User;
        var result = await _reservationService.GetQrCodeAsync(id, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Moves a reservation to another slot and/or changes the requested energy.
    ///
    /// ***RULE R2 - EXPECTED OUTCOME, NOT A BUG.*** A reservation may be updated
    /// only while its status is Pending or Approved AND at least twelve hours of
    /// notice remain before the slot starts. When that is not the case the service
    /// returns a ValidationError, which this controller reports as HTTP 400 with
    /// errorCode "RES_UPDATE_12H". That 400 is the rule working correctly - both
    /// clients are expected to show the returned message to the user. The clients
    /// never compute the twelve hours themselves; they read ReservationDto.CanModify,
    /// which the server has already evaluated.
    /// </summary>
    /// <param name="id">Mongo identifier of the reservation to change.</param>
    /// <param name="request">Optional new slot and/or new energy amount.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The updated reservation, or the R2 violation that stopped the edit.</returns>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> UpdateAsync(
        string id, [FromBody] UpdateReservationRequest request, CancellationToken ct)
    {
        // No hour counting and no status comparison happen here - rule R2 is decided entirely in the service.
        var caller = _currentUser.User;
        var result = await _reservationService.UpdateAsync(id, request, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Withdraws a reservation, optionally recording why.
    ///
    /// ***RULE R3 - EXPECTED OUTCOME, NOT A BUG.*** A reservation may be cancelled
    /// only while its status is Pending or Approved AND at least twelve hours of
    /// notice remain before the slot starts. Outside that window the service
    /// returns a ValidationError which becomes HTTP 400 with errorCode
    /// "RES_CANCEL_12H". That 400 is the rule firing as designed, and the message
    /// it carries is meant to be shown to the prosumer. The buttons on both
    /// clients are driven by the server computed ReservationDto.CanCancel flag,
    /// so no client ever re-implements the twelve hour arithmetic.
    /// </summary>
    /// <param name="id">Mongo identifier of the reservation to cancel.</param>
    /// <param name="request">Optional cancellation reason; the body may be omitted entirely.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The cancelled reservation, or the R3 violation that stopped it.</returns>
    [HttpPatch("{id}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> CancelAsync(
        string id, [FromBody] CancelReservationRequest? request, CancellationToken ct)
    {
        // The reason is optional, so an absent body is normalised into an empty request before the single service call.
        var caller = _currentUser.User;
        var result = await _reservationService.CancelAsync(id, request ?? new CancelReservationRequest(), caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Approves a pending reservation and issues its QR token. Token generation,
    /// the QR payload string and the stamping of the approver are all performed by
    /// the service; this method only names the reservation and the caller.
    /// </summary>
    /// <param name="id">Mongo identifier of the reservation to approve.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The approved reservation carrying its new QR token.</returns>
    [HttpPatch("{id}/approve")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> ApproveAsync(string id, CancellationToken ct)
    {
        // Whether the reservation is in an approvable state is a service decision, so this method holds no branch.
        var caller = _currentUser.User;
        var result = await _reservationService.ApproveAsync(id, caller, ct);
        return Respond(result);
    }

    /// <summary>
    /// Refuses a reservation with a mandatory reason, which the prosumer sees in
    /// the mobile app. Releasing the capacity the booking was holding on its slot
    /// is part of the same service operation.
    /// </summary>
    /// <param name="id">Mongo identifier of the reservation to reject.</param>
    /// <param name="request">The rejection reason, required by the DTO.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The rejected reservation, or the failure envelope.</returns>
    [HttpPatch("{id}/reject")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ReservationDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ReservationDto>>> RejectAsync(
        string id, [FromBody] RejectReservationRequest request, CancellationToken ct)
    {
        // The state machine check - only a Pending booking may be rejected - lives in the service, not here.
        var caller = _currentUser.User;
        var result = await _reservationService.RejectAsync(id, request, caller, ct);
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
