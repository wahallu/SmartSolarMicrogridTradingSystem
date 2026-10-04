/* =============================================================================
 * File        : ServiceResult.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Common
 * Description : Carries a service result before a controller turns it into
 *               HTTP.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-24
 * =============================================================================
 */

namespace SmartSolar.Api.Common;

/// <summary>Outcome categories a service method can report.</summary>
public enum ResultStatus
{
    Success = 0,
    Created = 1,
    ValidationError = 2,
    NotFound = 3,
    Conflict = 4,
    Forbidden = 5,
    Unauthorized = 6,
    Error = 7
}

/// <summary>Result of a service operation that produces a value.</summary>
/// <typeparam name="T">Type of the produced value.</typeparam>
public class ServiceResult<T>
{
    public ResultStatus Status { get; init; } = ResultStatus.Success;
    public T? Value { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public Dictionary<string, List<string>>? Errors { get; init; }

    /// <summary>True when the operation completed without a rule violation.</summary>
    public bool IsSuccess => Status is ResultStatus.Success or ResultStatus.Created;

    /// <summary>Operation succeeded and returns a value.</summary>
    public static ServiceResult<T> Ok(T value, string message = "Request completed successfully.")
    {
        // Success path - the caller may read Value safely.
        return new ServiceResult<T> { Status = ResultStatus.Success, Value = value, Message = message };
    }

    /// <summary>A new resource was created; the controller maps this to HTTP 201.</summary>
    public static ServiceResult<T> Created(T value, string message = "Resource created successfully.")
    {
        // Distinguished from Ok purely so the controller can emit 201 rather than 200.
        return new ServiceResult<T> { Status = ResultStatus.Created, Value = value, Message = message };
    }

    /// <summary>Input failed validation or violated a business rule (HTTP 400).</summary>
    public static ServiceResult<T> Invalid(string message, string? errorCode = null,
        Dictionary<string, List<string>>? errors = null)
    {
        // Used for the 7 day window and the 12 hour notice rules among others.
        return new ServiceResult<T>
        {
            Status = ResultStatus.ValidationError,
            Message = message,
            ErrorCode = errorCode ?? ErrorCodes.ValidationFailed,
            Errors = errors
        };
    }

    /// <summary>The requested document does not exist (HTTP 404).</summary>
    public static ServiceResult<T> NotFound(string message, string? errorCode = null)
    {
        // Kept separate from Invalid so clients can distinguish "wrong id" from "bad request".
        return new ServiceResult<T>
        {
            Status = ResultStatus.NotFound,
            Message = message,
            ErrorCode = errorCode ?? ErrorCodes.NotFound
        };
    }

    /// <summary>The operation clashes with existing state (HTTP 409).</summary>
    public static ServiceResult<T> Conflict(string message, string? errorCode = null)
    {
        // Duplicate NIC, station still holding reservations, slot already full.
        return new ServiceResult<T>
        {
            Status = ResultStatus.Conflict,
            Message = message,
            ErrorCode = errorCode ?? ErrorCodes.Conflict
        };
    }

    /// <summary>The caller is authenticated but not permitted (HTTP 403).</summary>
    public static ServiceResult<T> Forbidden(string message, string? errorCode = null)
    {
        // Role checks and the "prosumers may only touch their own data" rule.
        return new ServiceResult<T>
        {
            Status = ResultStatus.Forbidden,
            Message = message,
            ErrorCode = errorCode ?? ErrorCodes.Forbidden
        };
    }

    /// <summary>Credentials are missing or wrong (HTTP 401).</summary>
    public static ServiceResult<T> Unauthorized(string message, string? errorCode = null)
    {
        // Only produced by the authentication service.
        return new ServiceResult<T>
        {
            Status = ResultStatus.Unauthorized,
            Message = message,
            ErrorCode = errorCode ?? ErrorCodes.InvalidCredentials
        };
    }

    /// <summary>An unexpected failure (HTTP 500).</summary>
    public static ServiceResult<T> Error(string message, string? errorCode = null)
    {
        // Reserved for genuinely unexpected conditions, never for rule violations.
        return new ServiceResult<T>
        {
            Status = ResultStatus.Error,
            Message = message,
            ErrorCode = errorCode ?? ErrorCodes.InternalError
        };
    }
}

/// <summary>Convenience factory for service operations that produce no value.</summary>
public static class ServiceResult
{
    /// <summary>Success with no payload.</summary>
    public static ServiceResult<bool> Ok(string message = "Request completed successfully.")
    {
        // Returns true so a caller may still branch on Value if it wants to.
        return ServiceResult<bool>.Ok(true, message);
    }
}
