/* =============================================================================
 * File        : MethodOverrideMiddleware.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Middleware
 * Description : Supports clients that send an HTTP method override header.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-20
 * =============================================================================
 */

namespace SmartSolar.Api.Middleware;

/// <summary>Rewrites the request verb from the X-HTTP-Method-Override header.</summary>
public class MethodOverrideMiddleware
{
    private const string HeaderName = "X-HTTP-Method-Override";

    /// <summary>Verbs a client is permitted to reach through the override header.</summary>
    private static readonly string[] AllowedOverrides = { "PATCH", "PUT", "DELETE" };

    private readonly RequestDelegate _next;
    private readonly ILogger<MethodOverrideMiddleware> _logger;

    /// <summary>Captures the next delegate in the pipeline and the logger.</summary>
    public MethodOverrideMiddleware(RequestDelegate next, ILogger<MethodOverrideMiddleware> logger)
    {
        // Standard middleware constructor - both dependencies are supplied by the host.
        _next = next;
        _logger = logger;
    }

    /// <summary>Applies the override when present, then passes the request along.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // The override is only ever honoured on a POST, so a GET can never be rewritten.
        if (HttpMethods.IsPost(context.Request.Method)
            && context.Request.Headers.TryGetValue(HeaderName, out var headerValues))
        {
            var requested = headerValues.ToString().Trim().ToUpperInvariant();

            if (AllowedOverrides.Contains(requested))
            {
                _logger.LogDebug("Rewriting POST {Path} as {Verb} via {Header}.",
                    context.Request.Path, requested, HeaderName);
                context.Request.Method = requested;
            }
            else if (!string.IsNullOrWhiteSpace(requested))
            {
                // An unrecognised value is ignored rather than trusted.
                _logger.LogWarning("Ignoring unsupported {Header} value '{Value}'.", HeaderName, requested);
            }
        }

        await _next(context);
    }
}

/// <summary>Registration helper for <see cref="MethodOverrideMiddleware"/>.</summary>
public static class MethodOverrideMiddlewareExtensions
{
    /// <summary>Adds the verb override middleware to the pipeline.</summary>
    public static IApplicationBuilder UseMethodOverride(this IApplicationBuilder app)
    {
        // Must be registered before UseAuthorization and MapControllers so routing sees the real verb.
        return app.UseMiddleware<MethodOverrideMiddleware>();
    }
}
