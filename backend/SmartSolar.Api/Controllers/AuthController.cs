/* =============================================================================
 * File        : AuthController.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Controllers / Auth
 * Description : HTTP surface for logging in, self registering as a solar
 *               prosumer, reading the caller's own profile and changing the
 *               caller's own password. The controller is deliberately THIN: it
 *               binds a DTO, calls exactly one IAuthService method and maps the
 *               returned ServiceResult onto a status code. Every credential
 *               check and every account state rule (R5, R6, R7) lives in the
 *               service, which is what keeps this a FAT service architecture.
 * Author      : Chathushka - Identity, Access Control & Local Session (SE4040 EAD)
 * Created     : 2026-09-04
 * =============================================================================
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSolar.Api.Common;
using SmartSolar.Api.DTOs.Auth;
using SmartSolar.Api.DTOs.Users;
using SmartSolar.Api.Security;
using SmartSolar.Api.Services.Interfaces;

namespace SmartSolar.Api.Controllers;

/// <summary>
/// Authentication endpoints shared by the React web application and the native
/// Android application. The class requires a bearer token by default; the two
/// public entry points opt out explicitly with <see cref="AllowAnonymousAttribute"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Captures the authentication service and the caller accessor.</summary>
    /// <param name="authService">The FAT service holding every authentication rule.</param>
    /// <param name="currentUser">Resolves the caller of the request in flight from the JWT.</param>
    public AuthController(IAuthService authService, ICurrentUserAccessor currentUser)
    {
        // Dependencies are stored once; the controller itself keeps no state between requests.
        _authService = authService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Authenticates a Backoffice officer, a Grid Operator or a solar prosumer and
    /// issues an eight hour JWT. The identifier may be a NIC, a username or an
    /// e-mail address - the service decides which, not the client.
    /// </summary>
    /// <param name="request">Identifier and password posted by the client.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The token, its expiry instant and the caller's profile.</returns>
    /// <remarks>
    /// A 403 carrying the error code ACCOUNT_PENDING (rule R6) or ACCOUNT_DEACTIVATED
    /// (rule R5) is a NORMAL, EXPECTED outcome of this endpoint, not a server fault.
    /// ACCOUNT_PENDING means a self registered prosumer is still waiting for a
    /// Backoffice officer to activate the account; ACCOUNT_DEACTIVATED means the
    /// account was blocked and only a Backoffice officer may restore it. Both clients
    /// branch on the errorCode and present their own specific message and next step
    /// (for example "Your account is awaiting approval") rather than a generic
    /// "login failed", and neither client re-evaluates the account status itself.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> LoginAsync(
        [FromBody] LoginRequest request, CancellationToken ct)
    {
        // One service call: credential verification and the R5 / R6 account state rules all happen there.
        var result = await _authService.LoginAsync(request, ct);
        return Respond(result);
    }

    /// <summary>
    /// Self registration used by the Android application. The new account is created
    /// with status Pending and cannot log in until a Backoffice officer activates it
    /// (rule R6); a duplicate NIC is refused with NIC_ALREADY_EXISTS (rule R7).
    /// </summary>
    /// <param name="request">NIC, name, contact details and the chosen password.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The created profile, reported as HTTP 201.</returns>
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<UserDto>>> RegisterProsumerAsync(
        [FromBody] RegisterProsumerRequest request, CancellationToken ct)
    {
        // The uniqueness of the NIC and the initial Pending status are decided by the service, never here.
        var result = await _authService.RegisterProsumerAsync(request, ct);
        return Respond(result);
    }

    /// <summary>Returns the profile of whoever presented the bearer token.</summary>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>The caller's own account details.</returns>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetCurrentUserAsync(CancellationToken ct)
    {
        // The caller is projected from the validated JWT claims and handed to the service unchanged.
        var caller = _currentUser.User;
        var result = await _authService.GetCurrentUserAsync(caller, ct);
        return Respond(result);
    }

    /// <summary>Changes the caller's own password after the current one is verified.</summary>
    /// <param name="request">The current password and the replacement password.</param>
    /// <param name="ct">Cancellation token flowed through to the service.</param>
    /// <returns>True when the password was replaced, otherwise the failure envelope.</returns>
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        // Verifying the current password and re-hashing the new one are service concerns.
        var caller = _currentUser.User;
        var result = await _authService.ChangePasswordAsync(caller, request, ct);
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
