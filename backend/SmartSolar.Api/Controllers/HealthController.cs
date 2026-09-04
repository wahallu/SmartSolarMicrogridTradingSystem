/* =============================================================================
 * File        : HealthController.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Controllers / Health
 * Description : The single anonymous "is the system up" endpoint, and the first
 *               request anyone - a marker, a load balancer or the Android
 *               application on start up - sends after a deployment. It pings
 *               MongoDB through MongoDbContext and reports the process identity
 *               (database name, environment, version) plus the server's UTC
 *               clock, so a client that sees odd booking behaviour can prove in
 *               one call whether the API is alive, which database it opened and
 *               whether its clock agrees with the caller's.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartSolar.Api.Common;
using SmartSolar.Api.Data;

namespace SmartSolar.Api.Controllers;

/// <summary>
/// Liveness and readiness probe for the Smart Solar Microgrid API. The whole
/// class is anonymous on purpose: a monitor must be able to reach it before any
/// account exists and without holding a bearer token. It is the only controller
/// in the solution that talks to <see cref="MongoDbContext"/> directly, because
/// "can this process still reach its database" is an infrastructure question
/// rather than a business rule, so no FAT service owns it.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/health")]
[Produces("application/json")]
public class HealthController : ControllerBase
{
    /// <summary>Semantic version of this API build, surfaced so a marker can confirm what is deployed.</summary>
    private const string ApiVersion = "1.0.0";

    private readonly MongoDbContext _db;
    private readonly IClock _clock;
    private readonly IWebHostEnvironment _env;

    /// <summary>Captures the data context, the injected clock and the hosting environment.</summary>
    /// <param name="db">Mongo context used only for its connectivity ping and database name.</param>
    /// <param name="clock">Injected clock - the reported instant is the very same source every business rule reads.</param>
    /// <param name="env">Hosting environment, so the response states whether this is Development or Production.</param>
    public HealthController(MongoDbContext db, IClock clock, IWebHostEnvironment env)
    {
        // Dependencies are stored once; the controller keeps no state between requests.
        _db = db;
        _clock = clock;
        _env = env;
    }

    /// <summary>
    /// Reports whether the API process is running and whether its MongoDB
    /// connection is currently usable.
    /// </summary>
    /// <param name="ct">Cancellation token flowed through to the MongoDB ping.</param>
    /// <returns>
    /// HTTP 200 with status "Healthy" when the ping succeeds, or HTTP 503 with
    /// status "Degraded" and the error code DATABASE_UNAVAILABLE when the process
    /// is answering but the database is not reachable.
    /// </returns>
    /// <remarks>
    /// The two outcomes are deliberately distinguished. A 503 here means the web
    /// tier is alive and the fault is downstream in MongoDB, which is a very
    /// different diagnosis from no response at all (process or container down).
    /// The payload is intentionally richer than a bare "OK": databaseName proves
    /// which database the configured connection string actually opened,
    /// environment proves which appsettings profile won, and utcNow lets a client
    /// compare clocks - every time sensitive rule in this system (the 7 day
    /// booking window R1, the 12 hour update and cancel notice R2 and R3, and the
    /// QR scan window R9) is evaluated against this server clock in UTC, so a
    /// device whose own clock has drifted can detect that here rather than by
    /// filing a bug about a "wrongly rejected" reservation.
    /// This action performs no domain logic: it only reports observed state.
    /// </remarks>
    [HttpGet("")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiResponse<object>>> GetHealthAsync(CancellationToken ct)
    {
        // PingAsync swallows its own MongoException and returns false, so this endpoint always answers.
        var connected = await _db.PingAsync(ct);

        // The payload is assembled once and returned by both branches so a monitor parses one shape.
        var payload = new
        {
            status = connected ? "Healthy" : "Degraded",
            database = connected ? "Connected" : "Disconnected",
            databaseName = _db.DatabaseName,
            utcNow = _clock.UtcNow,
            version = ApiVersion,
            environment = _env.EnvironmentName
        };

        if (!connected)
        {
            // 503 tells an orchestrator to keep this instance out of rotation until Mongo recovers.
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail(
                    "The API is running but the MongoDB connection is unavailable.",
                    "DATABASE_UNAVAILABLE"));
        }

        // 200 with the full envelope: clients branch on Success exactly as they do for every other endpoint.
        return Ok(ApiResponse<object>.Ok(payload, "Smart Solar Microgrid API is running."));
    }
}
