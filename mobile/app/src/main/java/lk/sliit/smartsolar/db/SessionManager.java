/*
 * =============================================================================
 * File        : SessionManager.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : db
 * Description : Stores and retrieves the signed in user's session in the local
 *               SQLite database. The assignment asks specifically for login
 *               details to persist in SQLite on Android, so the JWT and the user
 *               profile are written to the "session" table rather than to
 *               SharedPreferences.
 *
 *               The table holds at most one row (primary key fixed at 1), so a
 *               new login simply replaces the previous session.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.db;

import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.util.Log;

import org.json.JSONObject;

import lk.sliit.smartsolar.models.UserModel;
import lk.sliit.smartsolar.util.Constants;

/** Persists the authenticated session in the local SQLite database. */
public class SessionManager {

    private static final String TAG = "SessionManager";

    /** The session table always holds a single row with this id. */
    private static final int SESSION_ROW_ID = 1;

    private final DatabaseHelper helper;

    /** Obtains the shared database helper. */
    public SessionManager(Context context) {
        // The helper is a process wide singleton so all screens share one connection pool.
        this.helper = DatabaseHelper.getInstance(context);
    }

    /**
     * Writes the session produced by a successful login. Any previous session row
     * is replaced, so signing in as a different user never leaves stale data.
     */
    public void saveSession(String token, String expiresAtUtc, UserModel user) {
        // CONFLICT_REPLACE gives an upsert without a separate existence check.
        SQLiteDatabase db = helper.getWritableDatabase();
        ContentValues values = new ContentValues();

        values.put(DatabaseHelper.COL_SESSION_ID, SESSION_ROW_ID);
        values.put(DatabaseHelper.COL_SESSION_USER_ID, user.getId());
        values.put(DatabaseHelper.COL_SESSION_NIC, user.getNic());
        values.put(DatabaseHelper.COL_SESSION_USERNAME, user.getUsername());
        values.put(DatabaseHelper.COL_SESSION_FULL_NAME, user.getFullName());
        values.put(DatabaseHelper.COL_SESSION_EMAIL, user.getEmail());
        values.put(DatabaseHelper.COL_SESSION_PHONE, user.getPhone());
        values.put(DatabaseHelper.COL_SESSION_ADDRESS, user.getAddress());
        values.put(DatabaseHelper.COL_SESSION_ROLE, user.getRole());
        values.put(DatabaseHelper.COL_SESSION_STATUS, user.getStatus());
        values.put(DatabaseHelper.COL_SESSION_STATION_ID, user.getAssignedStationId());
        values.put(DatabaseHelper.COL_SESSION_STATION_CODE, user.getAssignedStationCode());
        values.put(DatabaseHelper.COL_SESSION_TOKEN, token);
        values.put(DatabaseHelper.COL_SESSION_EXPIRES_AT, expiresAtUtc);
        values.put(DatabaseHelper.COL_SESSION_SAVED_AT, System.currentTimeMillis());

        db.insertWithOnConflict(DatabaseHelper.TABLE_SESSION, null, values, SQLiteDatabase.CONFLICT_REPLACE);
        Log.i(TAG, "Session saved for " + user.getUsername() + " (" + user.getRole() + ")");
    }

    /** Updates only the cached profile fields after the user edits their own details. */
    public void updateProfile(UserModel user) {
        // Leaves the token untouched so an edit never invalidates the session.
        SQLiteDatabase db = helper.getWritableDatabase();
        ContentValues values = new ContentValues();
        values.put(DatabaseHelper.COL_SESSION_FULL_NAME, user.getFullName());
        values.put(DatabaseHelper.COL_SESSION_EMAIL, user.getEmail());
        values.put(DatabaseHelper.COL_SESSION_PHONE, user.getPhone());
        values.put(DatabaseHelper.COL_SESSION_ADDRESS, user.getAddress());
        values.put(DatabaseHelper.COL_SESSION_STATUS, user.getStatus());

        db.update(DatabaseHelper.TABLE_SESSION, values,
                DatabaseHelper.COL_SESSION_ID + " = ?",
                new String[]{String.valueOf(SESSION_ROW_ID)});
    }

    /** Returns the stored session as a user model, or null when nobody is signed in. */
    public UserModel getUser() {
        // Reading straight back out of SQLite is what lets the splash screen skip the login form.
        SQLiteDatabase db = helper.getReadableDatabase();
        try (Cursor cursor = db.query(DatabaseHelper.TABLE_SESSION, null,
                DatabaseHelper.COL_SESSION_ID + " = ?",
                new String[]{String.valueOf(SESSION_ROW_ID)}, null, null, null)) {

            if (cursor == null || !cursor.moveToFirst()) {
                return null;
            }

            UserModel user = new UserModel();
            user.setId(getString(cursor, DatabaseHelper.COL_SESSION_USER_ID));
            user.setNic(getString(cursor, DatabaseHelper.COL_SESSION_NIC));
            user.setUsername(getString(cursor, DatabaseHelper.COL_SESSION_USERNAME));
            user.setFullName(getString(cursor, DatabaseHelper.COL_SESSION_FULL_NAME));
            user.setEmail(getString(cursor, DatabaseHelper.COL_SESSION_EMAIL));
            user.setPhone(getString(cursor, DatabaseHelper.COL_SESSION_PHONE));
            user.setAddress(getString(cursor, DatabaseHelper.COL_SESSION_ADDRESS));
            user.setRole(getString(cursor, DatabaseHelper.COL_SESSION_ROLE));
            user.setStatus(getString(cursor, DatabaseHelper.COL_SESSION_STATUS));
            user.setAssignedStationId(getString(cursor, DatabaseHelper.COL_SESSION_STATION_ID));
            user.setAssignedStationCode(getString(cursor, DatabaseHelper.COL_SESSION_STATION_CODE));
            return user;
        } catch (Exception ex) {
            Log.e(TAG, "Failed to read the stored session.", ex);
            return null;
        }
    }

    /** Returns the stored JWT, or null when nobody is signed in. */
    public String getToken() {
        // Every authenticated ApiClient call reads the token through this method.
        SQLiteDatabase db = helper.getReadableDatabase();
        try (Cursor cursor = db.query(DatabaseHelper.TABLE_SESSION,
                new String[]{DatabaseHelper.COL_SESSION_TOKEN},
                DatabaseHelper.COL_SESSION_ID + " = ?",
                new String[]{String.valueOf(SESSION_ROW_ID)}, null, null, null)) {

            if (cursor != null && cursor.moveToFirst()) {
                return cursor.getString(0);
            }
            return null;
        } catch (Exception ex) {
            Log.e(TAG, "Failed to read the stored token.", ex);
            return null;
        }
    }

    /** True when a session row exists and still carries a token. */
    public boolean isLoggedIn() {
        // The splash screen uses this to decide between the login form and a home screen.
        String token = getToken();
        return token != null && !token.trim().isEmpty();
    }

    /** Role of the signed in user, or an empty string when nobody is signed in. */
    public String getRole() {
        // Drives which home screen the splash and login screens route to.
        UserModel user = getUser();
        return user == null || user.getRole() == null ? "" : user.getRole();
    }

    /** True when the signed in user is a solar prosumer. */
    public boolean isProsumer() {
        // Used for the role based home routing required by the marking scheme.
        return Constants.ROLE_PROSUMER.equals(getRole());
    }

    /** True when the signed in user is a grid operator. */
    public boolean isGridOperator() {
        // Grid operators get the scanner and station tools instead of the booking tools.
        return Constants.ROLE_GRID_OPERATOR.equals(getRole());
    }

    /** NIC of the signed in prosumer, or null for a staff account. */
    public String getNic() {
        // The NIC is the prosumer primary key and is sent on booking calls.
        UserModel user = getUser();
        return user == null ? null : user.getNic();
    }

    /** Clears the session row and every cached table, used on sign out. */
    public void logout() {
        // Cached rows are cleared too so the next user never sees the previous user's data.
        SQLiteDatabase db = helper.getWritableDatabase();
        db.delete(DatabaseHelper.TABLE_SESSION, null, null);
        db.delete(DatabaseHelper.TABLE_RESERVATIONS, null, null);
        db.delete(DatabaseHelper.TABLE_STATIONS, null, null);
        db.delete(DatabaseHelper.TABLE_SYNC_LOG, null, null);
        Log.i(TAG, "Session cleared and local caches emptied.");
    }

    /** Rebuilds the session's user model from a fresh /auth/me payload. */
    public void refreshFromJson(JSONObject userJson) {
        // Keeps the cached profile in step with the server after a remote change.
        if (userJson == null) {
            return;
        }
        UserModel user = UserModel.fromJson(userJson);
        updateProfile(user);
    }

    /** Reads a column as a string, returning null when the column is null. */
    private String getString(Cursor cursor, String column) {
        // Cursor.getString throws on a missing column, so the index is checked first.
        int index = cursor.getColumnIndex(column);
        if (index < 0 || cursor.isNull(index)) {
            return null;
        }
        return cursor.getString(index);
    }
}
