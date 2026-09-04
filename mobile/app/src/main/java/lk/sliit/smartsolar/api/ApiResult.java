/*
 * =============================================================================
 * File        : ApiResult.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : api
 * Description : The parsed form of the JSON envelope that every Web API endpoint
 *               returns. Because the server always answers with the same shape
 *               ({success, message, data, errorCode, errors}), the whole app
 *               needs exactly one result type and one error handling path.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.api;

import org.json.JSONArray;
import org.json.JSONObject;

/** Outcome of one call to the central Web API. */
public class ApiResult {

    private final boolean success;
    private final int httpStatus;
    private final String message;
    private final String errorCode;
    private final JSONObject dataObject;
    private final JSONArray dataArray;
    private final JSONObject fieldErrors;

    /** Builds a result. Use the static factory methods rather than calling this directly. */
    private ApiResult(boolean success, int httpStatus, String message, String errorCode,
                      JSONObject dataObject, JSONArray dataArray, JSONObject fieldErrors) {
        // Only one of dataObject / dataArray is ever populated, depending on the endpoint.
        this.success = success;
        this.httpStatus = httpStatus;
        this.message = message;
        this.errorCode = errorCode;
        this.dataObject = dataObject;
        this.dataArray = dataArray;
        this.fieldErrors = fieldErrors;
    }

    /** Creates a successful result whose payload is a JSON object. */
    public static ApiResult successObject(int httpStatus, String message, JSONObject data) {
        // Used by every endpoint that returns a single entity or a paged wrapper.
        return new ApiResult(true, httpStatus, message, null, data, null, null);
    }

    /** Creates a successful result whose payload is a JSON array. */
    public static ApiResult successArray(int httpStatus, String message, JSONArray data) {
        // Used by list endpoints such as /stations/nearby and /reservations/my.
        return new ApiResult(true, httpStatus, message, null, null, data, null);
    }

    /** Creates a failed result carrying the server's error code and message. */
    public static ApiResult failure(int httpStatus, String message, String errorCode, JSONObject fieldErrors) {
        // The error code is what the UI branches on; the message is what it shows.
        return new ApiResult(false, httpStatus, message, errorCode, null, null, fieldErrors);
    }

    /** Creates a failed result for a transport level problem such as no connectivity. */
    public static ApiResult networkFailure(String message) {
        // HTTP status 0 marks "the request never reached the server".
        return new ApiResult(false, 0, message, "NETWORK_ERROR", null, null, null);
    }

    /** True when the server reported success. */
    public boolean isSuccess() {
        // Callers should always branch on this before reading any payload.
        return success;
    }

    /** HTTP status code, or 0 when the request never reached the server. */
    public int getHttpStatus() {
        // Useful for distinguishing 401 (token expired) from 403 (wrong role).
        return httpStatus;
    }

    /** Human readable message supplied by the server, safe to show to the user. */
    public String getMessage() {
        // The API writes these messages for end users, so they are shown verbatim.
        return message == null ? "" : message;
    }

    /** Stable machine readable error identifier, or null on success. */
    public String getErrorCode() {
        // See Constants.ERR_* for the codes this application reacts to.
        return errorCode;
    }

    /** Payload when the endpoint returns a single object; may be null. */
    public JSONObject getDataObject() {
        // Null for list endpoints and for endpoints that return no payload.
        return dataObject;
    }

    /** Payload when the endpoint returns an array; may be null. */
    public JSONArray getDataArray() {
        // Null for object endpoints.
        return dataArray;
    }

    /** Field level validation messages keyed by property name; may be null. */
    public JSONObject getFieldErrors() {
        // Populated when the server rejects a form with per-field messages.
        return fieldErrors;
    }

    /** True when the failure was a transport problem rather than a server response. */
    public boolean isNetworkError() {
        // Lets the UI offer "retry" instead of "fix your input".
        return httpStatus == 0;
    }

    /** True when the session token was rejected and the user must log in again. */
    public boolean isUnauthorized() {
        // 401 means the JWT is missing or expired.
        return httpStatus == 401;
    }

    /**
     * Extracts the "items" array from a paged payload, or null when the payload is
     * not paged. The Web API wraps pages as {items, page, pageSize, totalCount}.
     */
    public JSONArray getPagedItems() {
        // Paged endpoints return an object; the rows live under "items".
        if (dataObject == null) {
            return null;
        }
        return dataObject.optJSONArray("items");
    }

    /** Total row count reported by a paged payload, or 0 when absent. */
    public int getPagedTotalCount() {
        // Drives the "showing X of Y" labels in the list screens.
        return dataObject == null ? 0 : dataObject.optInt("totalCount", 0);
    }
}
