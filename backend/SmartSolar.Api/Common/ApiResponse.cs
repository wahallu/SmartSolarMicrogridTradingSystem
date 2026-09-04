/* =============================================================================
 * File        : ApiResponse.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Common
 * Description : The single JSON envelope every endpoint returns, success or
 *               failure. Because the shape never changes, the React client and
 *               the Android client each need exactly one deserialiser and one
 *               error handler instead of one per endpoint.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

namespace SmartSolar.Api.Common;

/// <summary>Uniform response envelope carrying a typed payload.</summary>
/// <typeparam name="T">Type of the <see cref="Data"/> payload.</typeparam>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }

    /// <summary>Stable machine readable code, e.g. "RES_CANCEL_12H". Null on success.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Field level validation messages, keyed by property name.</summary>
    public Dictionary<string, List<string>>? Errors { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Builds a successful envelope around a payload.</summary>
    public static ApiResponse<T> Ok(T? data, string message = "Request completed successfully.")
    {
        // A success envelope always clears the error fields so clients can branch on Success alone.
        return new ApiResponse<T> { Success = true, Message = message, Data = data };
    }

    /// <summary>Builds a failure envelope carrying an error code and optional field errors.</summary>
    public static ApiResponse<T> Fail(string message, string? errorCode = null,
        Dictionary<string, List<string>>? errors = null)
    {
        // Data is left at its default so the client never reads a half populated payload.
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode,
            Errors = errors
        };
    }
}

/// <summary>Non generic helper for endpoints that return no payload.</summary>
public static class ApiResponse
{
    /// <summary>Successful envelope with a null payload.</summary>
    public static ApiResponse<object?> Ok(string message = "Request completed successfully.")
    {
        // Used by activate / deactivate style endpoints that only report an outcome.
        return ApiResponse<object?>.Ok(null, message);
    }

    /// <summary>Failure envelope with a null payload.</summary>
    public static ApiResponse<object?> Fail(string message, string? errorCode = null)
    {
        // Mirrors the generic overload for the no-payload case.
        return ApiResponse<object?>.Fail(message, errorCode);
    }
}
