/*
 * =============================================================================
 * File        : ApiClient.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : api
 * Description : The single gateway between this application and the central C#
 *               Web API. Every screen in the app reaches the server through this
 *               class and through no other route - there is no direct database
 *               access of any kind from the phone.
 *
 *               PURE NATIVE ANDROID: networking uses java.net.HttpURLConnection
 *               from the Java platform itself and org.json from the Android
 *               framework. No Retrofit, no OkHttp, no Volley, no cross platform
 *               framework. Work is pushed onto a small fixed thread pool and the
 *               callback is posted back to the main looper, which is the
 *               standard native pattern for keeping the UI thread free.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * References  : HttpURLConnection usage follows the Android developer guide at
 *               https://developer.android.com/reference/java/net/HttpURLConnection
 * =============================================================================
 */

package lk.sliit.smartsolar.api;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.NetworkInfo;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import lk.sliit.smartsolar.BuildConfig;

/** Performs authenticated JSON calls against the central Web API. */
public class ApiClient {

    private static final String TAG = "ApiClient";
    private static final int CONNECT_TIMEOUT_MS = 15000;
    private static final int READ_TIMEOUT_MS = 20000;

    /** Four threads are ample: the UI never fires more than a handful of calls at once. */
    private static final ExecutorService EXECUTOR = Executors.newFixedThreadPool(4);

    /** Posts callbacks back to the UI thread so activities can touch views directly. */
    private static final Handler MAIN_HANDLER = new Handler(Looper.getMainLooper());

    private final Context appContext;
    private final String baseUrl;

    /** Holds the application context and the base URL from BuildConfig. */
    public ApiClient(Context context) {
        // The application context is used so this client never leaks an Activity.
        this.appContext = context.getApplicationContext();
        String url = BuildConfig.API_BASE_URL;
        this.baseUrl = url.endsWith("/") ? url : url + "/";
    }

    /** Callback delivered on the main thread once a request finishes. */
    public interface Callback {
        /** Receives the parsed envelope, whether the call succeeded or failed. */
        void onResult(ApiResult result);
    }

    // =====================================================================
    // Public request helpers
    // =====================================================================

    /** Issues a GET request without a bearer token (used for login and registration only). */
    public void getAnonymous(String path, Callback callback) {
        // Anonymous calls pass a null token so no Authorization header is added.
        enqueue("GET", path, null, null, callback);
    }

    /** Issues a GET request carrying the caller's bearer token. */
    public void get(String path, String token, Callback callback) {
        // The vast majority of screens use this method.
        enqueue("GET", path, null, token, callback);
    }

    /** Issues a POST request with a JSON body and no bearer token. */
    public void postAnonymous(String path, JSONObject body, Callback callback) {
        // Used by POST auth/login and POST auth/register.
        enqueue("POST", path, body, null, callback);
    }

    /** Issues a POST request with a JSON body and a bearer token. */
    public void post(String path, JSONObject body, String token, Callback callback) {
        // Creating a reservation, verifying a QR code and completing a job all use this.
        enqueue("POST", path, body, token, callback);
    }

    /** Issues a PUT request with a JSON body and a bearer token. */
    public void put(String path, JSONObject body, String token, Callback callback) {
        // Full updates such as editing a reservation or a profile.
        enqueue("PUT", path, body, token, callback);
    }

    /**
     * Issues a PATCH request. Android's HttpURLConnection (backed by OkHttp since
     * API 21) does accept "PATCH" as a request method, so it is set directly. On
     * the rare implementation that refuses it, execute() falls back to POST plus
     * the X-HTTP-Method-Override header, which the API's MethodOverrideMiddleware
     * translates back into a PATCH. Both paths are therefore covered.
     */
    public void patch(String path, JSONObject body, String token, Callback callback) {
        // Approve, reject, cancel, activate and deactivate all arrive here.
        enqueue("PATCH", path, body, token, callback);
    }

    /** Issues a DELETE request carrying the caller's bearer token. */
    public void delete(String path, String token, Callback callback) {
        // Used only by the back office screens, which the operator role can reach.
        enqueue("DELETE", path, null, token, callback);
    }

    // =====================================================================
    // Query string helper
    // =====================================================================

    /**
     * Appends a query string to a path, URL encoding every value and skipping any
     * entry whose value is null or blank.
     */
    public static String withQuery(String path, Map<String, String> params) {
        // Skipping blanks means callers can pass optional filters without null checks.
        if (params == null || params.isEmpty()) {
            return path;
        }

        StringBuilder sb = new StringBuilder(path);
        boolean first = !path.contains("?");
        for (Map.Entry<String, String> entry : params.entrySet()) {
            String value = entry.getValue();
            if (value == null || value.trim().isEmpty()) {
                continue;
            }
            sb.append(first ? "?" : "&");
            first = false;
            try {
                sb.append(URLEncoder.encode(entry.getKey(), "UTF-8"))
                  .append("=")
                  .append(URLEncoder.encode(value, "UTF-8"));
            } catch (IOException ex) {
                // UTF-8 is always available on Android; this branch cannot realistically run.
                Log.w(TAG, "Failed to encode query parameter " + entry.getKey(), ex);
            }
        }
        return sb.toString();
    }

    // =====================================================================
    // Internals
    // =====================================================================

    /** Queues the request on the background pool and marshals the reply to the UI thread. */
    private void enqueue(final String method, final String path, final JSONObject body,
                         final String token, final Callback callback) {
        // A connectivity check up front turns a 20 second timeout into an instant message.
        if (!isOnline()) {
            MAIN_HANDLER.post(() -> callback.onResult(
                    ApiResult.networkFailure("No internet connection. Please check your network and try again.")));
            return;
        }

        EXECUTOR.execute(() -> {
            final ApiResult result = execute(method, path, body, token);
            MAIN_HANDLER.post(() -> callback.onResult(result));
        });
    }

    /** Performs the blocking HTTP call. Runs on a background thread only. */
    private ApiResult execute(String method, String path, JSONObject body, String token) {
        // Everything in this method is deliberately synchronous - the caller already backgrounded it.
        HttpURLConnection connection = null;
        try {
            URL url = new URL(baseUrl + path);
            connection = (HttpURLConnection) url.openConnection();

            // Android accepts PATCH directly; the catch is the safety net for any
            // implementation that does not, and the API understands both forms.
            try {
                connection.setRequestMethod(method);
            } catch (java.net.ProtocolException protocolEx) {
                Log.w(TAG, "Request method " + method + " rejected; falling back to POST override.", protocolEx);
                connection.setRequestMethod("POST");
                connection.setRequestProperty("X-HTTP-Method-Override", method);
            }

            connection.setConnectTimeout(CONNECT_TIMEOUT_MS);
            connection.setReadTimeout(READ_TIMEOUT_MS);
            connection.setRequestProperty("Accept", "application/json");

            if (token != null && !token.isEmpty()) {
                connection.setRequestProperty("Authorization", "Bearer " + token);
            }

            // A body is written for every verb that carries one, including tunnelled PATCH.
            if (body != null) {
                connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                byte[] payload = body.toString().getBytes(StandardCharsets.UTF_8);
                connection.setFixedLengthStreamingMode(payload.length);
                try (OutputStream os = connection.getOutputStream()) {
                    os.write(payload);
                    os.flush();
                }
            }

            int status = connection.getResponseCode();

            // 4xx and 5xx responses arrive on the error stream, not the input stream.
            InputStream stream = (status >= 200 && status < 400)
                    ? connection.getInputStream()
                    : connection.getErrorStream();

            String raw = readAll(stream);
            Log.d(TAG, method + " " + path + " -> " + status);
            return parse(status, raw);

        } catch (IOException ex) {
            Log.e(TAG, "Request failed: " + method + " " + path, ex);
            return ApiResult.networkFailure(
                    "Could not reach the Smart Solar service. Please confirm the API is running and try again.");
        } catch (Exception ex) {
            Log.e(TAG, "Unexpected failure: " + method + " " + path, ex);
            return ApiResult.networkFailure("An unexpected error occurred: " + ex.getMessage());
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    /** Reads a stream fully into a string, tolerating a null stream. */
    private String readAll(InputStream stream) throws IOException {
        // A null error stream is legal when the server sends a status with no body.
        if (stream == null) {
            return "";
        }
        StringBuilder sb = new StringBuilder();
        try (BufferedReader reader = new BufferedReader(
                new InputStreamReader(stream, StandardCharsets.UTF_8))) {
            String line;
            while ((line = reader.readLine()) != null) {
                sb.append(line);
            }
        }
        return sb.toString();
    }

    /**
     * Converts the raw response text into an ApiResult. The Web API always answers
     * with the envelope {success, message, data, errorCode, errors}, so one parser
     * serves every endpoint in the application.
     */
    private ApiResult parse(int status, String raw) {
        // An empty body on a 2xx is treated as a success with no payload.
        if (raw == null || raw.trim().isEmpty()) {
            return status >= 200 && status < 300
                    ? ApiResult.successObject(status, "Request completed successfully.", null)
                    : ApiResult.failure(status, "The server returned an empty response (HTTP " + status + ").",
                                        "EMPTY_RESPONSE", null);
        }

        try {
            JSONObject envelope = new JSONObject(raw);
            boolean success = envelope.optBoolean("success", status >= 200 && status < 300);
            String message = envelope.optString("message", "");
            String errorCode = envelope.isNull("errorCode") ? null : envelope.optString("errorCode", null);
            JSONObject fieldErrors = envelope.optJSONObject("errors");

            if (!success) {
                return ApiResult.failure(status, message, errorCode, fieldErrors);
            }

            // "data" is an object for single entities and paged wrappers, an array for lists.
            Object data = envelope.isNull("data") ? null : envelope.opt("data");
            if (data instanceof JSONArray) {
                return ApiResult.successArray(status, message, (JSONArray) data);
            }
            if (data instanceof JSONObject) {
                return ApiResult.successObject(status, message, (JSONObject) data);
            }
            return ApiResult.successObject(status, message, null);

        } catch (Exception ex) {
            // A non JSON body means something other than our API answered - say so plainly.
            Log.e(TAG, "Could not parse response: " + raw, ex);
            return ApiResult.failure(status,
                    "The server response could not be read. Please confirm the API base URL is correct.",
                    "PARSE_ERROR", null);
        }
    }

    /** True when the device currently has an active network. */
    private boolean isOnline() {
        // Uses the deprecated but universally supported API so minSdk 24 devices work unchanged.
        try {
            ConnectivityManager cm = (ConnectivityManager)
                    appContext.getSystemService(Context.CONNECTIVITY_SERVICE);
            if (cm == null) {
                return true;
            }
            NetworkInfo info = cm.getActiveNetworkInfo();
            return info != null && info.isConnected();
        } catch (Exception ex) {
            // If the check itself fails, let the request proceed and fail naturally.
            Log.w(TAG, "Connectivity check failed; attempting the request anyway.", ex);
            return true;
        }
    }
}
