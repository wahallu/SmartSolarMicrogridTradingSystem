/* =============================================================================
 * File        : ExceptionHandlingMiddleware.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Middleware
 * Description : Terminal try/catch wrapped around the whole request pipeline so
 *               that an exception nobody anticipated still leaves the server as
 *               the standard ApiResponse JSON envelope.
 *
 *               WHY THIS EXISTS: the React web client and the Android client
 *               both parse every response as ApiResponse JSON and nothing else.
 *               Without this filter an unhandled exception would return the
 *               developer exception HTML page (or an empty 500 body), which both
 *               clients would fail to deserialise - the user would see a parser
 *               crash instead of a readable error message. Catching here keeps
 *               the contract "every response is an ApiResponse" literally true.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

using System.Text.Json;
using SmartSolar.Api.Common;

namespace SmartSolar.Api.Middleware;

/// <summary>
/// Catches any exception escaping the pipeline and rewrites the response as an
/// <see cref="ApiResponse{T}"/> failure envelope carrying <see cref="ErrorCodes.InternalError"/>.
/// </summary>
public class ExceptionHandlingMiddleware
{
    /// <summary>Message returned outside Development so no internal detail is disclosed.</summary>
    private const string GenericMessage = "An unexpected error occurred while processing your request.";

    /// <summary>
    /// In short: CamelCase keeps the error envelope identical in casing to MVC's
/// success responses; PascalCase "Success" would deserialise as null on both clients.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    /// <summary>Captures the next delegate in the pipeline, the logger and the hosting environment.</summary>
    /// <param name="next">The remainder of the request pipeline.</param>
    /// <param name="logger">Diagnostics sink where the full exception is recorded.</param>
    /// <param name="env">Hosting environment, used to decide how much detail the client may see.</param>
    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        // Standard middleware constructor - all three dependencies are supplied by the host.
        _next = next;
        _logger = logger;
        _env = env;
    }

    /// <summary>Runs the rest of the pipeline and converts any escaping exception into JSON.</summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // The happy path simply passes the request along; the catch block is the whole point of this class.
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // The method and path are logged with the exception so a 500 can be traced to one endpoint.
            _logger.LogError(
                ex,
                "Unhandled exception while processing {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);

            await WriteErrorResponseAsync(context, ex);
        }
    }

    /// <summary>Replaces the in-flight response with the standard failure envelope.</summary>
    /// <param name="context">The current HTTP context whose response is being rewritten.</param>
    /// <param name="ex">The exception that escaped the pipeline.</param>
    private async Task WriteErrorResponseAsync(HttpContext context, Exception ex)
    {
        // Once the headers have been flushed the response is already on the wire and cannot be rewritten;
        // trying to change the status code at that point throws. Rethrowing hands the half written
        // response to the host, which is the only correct outcome left.
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Response for {Method} {Path} had already started, so the error envelope could not be written.",
                context.Request.Method,
                context.Request.Path);

            throw ex;
        }

        // Anything an action may have buffered is discarded so the client receives the envelope alone.
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        // ex.Message is shown only while developing. In production it would disclose internals such as
        // connection strings, file paths or MongoDB server names to anyone able to trigger a crash, so a
        // fixed generic sentence is sent instead and the real detail stays in the server log.
        var message = _env.IsDevelopment() ? ex.Message : GenericMessage;

        var envelope = ApiResponse<object>.Fail(message, ErrorCodes.InternalError);
        var payload = JsonSerializer.Serialize(envelope, SerializerOptions);

        await context.Response.WriteAsync(payload, context.RequestAborted);
    }
}

/// <summary>Registration helper for <see cref="ExceptionHandlingMiddleware"/>.</summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    /// <summary>Adds the global exception to JSON translator to the pipeline.</summary>
    /// <param name="app">The application pipeline builder.</param>
    /// <returns>The same builder so registrations can be chained.</returns>
    public static IApplicationBuilder UseExceptionHandlingMiddleware(this IApplicationBuilder app)
    {
        // Registered first in Program.cs so it wraps every later component, routing and MVC included.
        return app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
