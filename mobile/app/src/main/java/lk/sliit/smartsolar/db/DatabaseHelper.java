/*
 * =============================================================================
 * File        : DatabaseHelper.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : db
 * Description : The local SQLite database required by the assignment. It is
 *               created and upgraded through android.database.sqlite.SQLiteOpenHelper
 *               directly - no Room, no ORM, no cross platform persistence layer.
 *
 *               WHAT IT IS FOR: this database is a session store and an offline
 *               cache, never the system of record. The MongoDB database behind
 *               the central Web API remains the single source of truth; SQLite
 *               holds the signed in user's session so the app can resume without
 *               a fresh login, plus the last known copy of the reference data so
 *               the dashboard and station list still render something useful when
 *               the phone briefly loses connectivity.
 *
 *               Tables:
 *                 session      - the signed in user and their JWT (single row)
 *                 stations     - cached microgrid nodes for the map and pickers
 *                 reservations - cached bookings for the list screens
 *                 sync_log     - an audit trail of what was cached and when
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * References  : SQLiteOpenHelper usage follows the Android developer guide at
 *               https://developer.android.com/training/data-storage/sqlite
 * =============================================================================
 */

package lk.sliit.smartsolar.db;

import android.content.Context;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;
import android.util.Log;

/** Creates and upgrades the application's local SQLite database. */
public class DatabaseHelper extends SQLiteOpenHelper {

    private static final String TAG = "DatabaseHelper";

    public static final String DATABASE_NAME = "smart_solar.db";
    public static final int DATABASE_VERSION = 1;

    // ---------------------------------------------------------------------
    // session - exactly one row, holding the currently signed in account.
    // ---------------------------------------------------------------------
    public static final String TABLE_SESSION = "session";
    public static final String COL_SESSION_ID = "id";
    public static final String COL_SESSION_USER_ID = "user_id";
    public static final String COL_SESSION_NIC = "nic";
    public static final String COL_SESSION_USERNAME = "username";
    public static final String COL_SESSION_FULL_NAME = "full_name";
    public static final String COL_SESSION_EMAIL = "email";
    public static final String COL_SESSION_PHONE = "phone";
    public static final String COL_SESSION_ADDRESS = "address";
    public static final String COL_SESSION_ROLE = "role";
    public static final String COL_SESSION_STATUS = "status";
    public static final String COL_SESSION_STATION_ID = "assigned_station_id";
    public static final String COL_SESSION_STATION_CODE = "assigned_station_code";
    public static final String COL_SESSION_TOKEN = "token";
    public static final String COL_SESSION_EXPIRES_AT = "expires_at";
    public static final String COL_SESSION_SAVED_AT = "saved_at";

    // ---------------------------------------------------------------------
    // stations - cached microgrid nodes.
    // ---------------------------------------------------------------------
    public static final String TABLE_STATIONS = "stations";
    public static final String COL_STATION_ID = "id";
    public static final String COL_STATION_CODE = "station_code";
    public static final String COL_STATION_NAME = "name";
    public static final String COL_STATION_TYPE = "type";
    public static final String COL_STATION_ADDRESS = "address";
    public static final String COL_STATION_CITY = "city";
    public static final String COL_STATION_LAT = "latitude";
    public static final String COL_STATION_LNG = "longitude";
    public static final String COL_STATION_CAPACITY = "capacity_kwh";
    public static final String COL_STATION_TOTAL_SLOTS = "total_battery_slots";
    public static final String COL_STATION_AVAILABLE_SLOTS = "available_battery_slots";
    public static final String COL_STATION_CONTACT = "contact_number";
    public static final String COL_STATION_STATUS = "status";
    public static final String COL_STATION_CACHED_AT = "cached_at";

    // ---------------------------------------------------------------------
    // reservations - cached bookings belonging to the signed in prosumer.
    // ---------------------------------------------------------------------
    public static final String TABLE_RESERVATIONS = "reservations";
    public static final String COL_RES_ID = "id";
    public static final String COL_RES_NO = "reservation_no";
    public static final String COL_RES_NIC = "prosumer_nic";
    public static final String COL_RES_PROSUMER_NAME = "prosumer_name";
    public static final String COL_RES_STATION_ID = "station_id";
    public static final String COL_RES_STATION_CODE = "station_code";
    public static final String COL_RES_STATION_NAME = "station_name";
    public static final String COL_RES_SLOT_ID = "slot_id";
    public static final String COL_RES_SLOT_CODE = "slot_code";
    public static final String COL_RES_START_UTC = "reservation_start_utc";
    public static final String COL_RES_END_UTC = "reservation_end_utc";
    public static final String COL_RES_START_TIME = "start_time";
    public static final String COL_RES_END_TIME = "end_time";
    public static final String COL_RES_SLOT_TYPE = "slot_type";
    public static final String COL_RES_ENERGY = "energy_kwh";
    public static final String COL_RES_DELIVERED = "delivered_kwh";
    public static final String COL_RES_STATUS = "status";
    public static final String COL_RES_QR_TOKEN = "qr_token";
    public static final String COL_RES_CAN_MODIFY = "can_modify";
    public static final String COL_RES_CAN_CANCEL = "can_cancel";
    public static final String COL_RES_HOURS_UNTIL = "hours_until_start";
    public static final String COL_RES_CACHED_AT = "cached_at";

    // ---------------------------------------------------------------------
    // sync_log - what was cached, when, and how many rows.
    // ---------------------------------------------------------------------
    public static final String TABLE_SYNC_LOG = "sync_log";
    public static final String COL_SYNC_ID = "id";
    public static final String COL_SYNC_ENTITY = "entity";
    public static final String COL_SYNC_ROW_COUNT = "row_count";
    public static final String COL_SYNC_AT = "synced_at";

    // ---------------------------------------------------------------------
    // DDL
    // ---------------------------------------------------------------------

    private static final String CREATE_SESSION =
            "CREATE TABLE " + TABLE_SESSION + " (" +
            COL_SESSION_ID + " INTEGER PRIMARY KEY, " +
            COL_SESSION_USER_ID + " TEXT NOT NULL, " +
            COL_SESSION_NIC + " TEXT, " +
            COL_SESSION_USERNAME + " TEXT NOT NULL, " +
            COL_SESSION_FULL_NAME + " TEXT, " +
            COL_SESSION_EMAIL + " TEXT, " +
            COL_SESSION_PHONE + " TEXT, " +
            COL_SESSION_ADDRESS + " TEXT, " +
            COL_SESSION_ROLE + " TEXT NOT NULL, " +
            COL_SESSION_STATUS + " TEXT, " +
            COL_SESSION_STATION_ID + " TEXT, " +
            COL_SESSION_STATION_CODE + " TEXT, " +
            COL_SESSION_TOKEN + " TEXT NOT NULL, " +
            COL_SESSION_EXPIRES_AT + " TEXT, " +
            COL_SESSION_SAVED_AT + " INTEGER NOT NULL)";

    private static final String CREATE_STATIONS =
            "CREATE TABLE " + TABLE_STATIONS + " (" +
            COL_STATION_ID + " TEXT PRIMARY KEY, " +
            COL_STATION_CODE + " TEXT NOT NULL, " +
            COL_STATION_NAME + " TEXT NOT NULL, " +
            COL_STATION_TYPE + " TEXT, " +
            COL_STATION_ADDRESS + " TEXT, " +
            COL_STATION_CITY + " TEXT, " +
            COL_STATION_LAT + " REAL NOT NULL DEFAULT 0, " +
            COL_STATION_LNG + " REAL NOT NULL DEFAULT 0, " +
            COL_STATION_CAPACITY + " REAL NOT NULL DEFAULT 0, " +
            COL_STATION_TOTAL_SLOTS + " INTEGER NOT NULL DEFAULT 0, " +
            COL_STATION_AVAILABLE_SLOTS + " INTEGER NOT NULL DEFAULT 0, " +
            COL_STATION_CONTACT + " TEXT, " +
            COL_STATION_STATUS + " TEXT, " +
            COL_STATION_CACHED_AT + " INTEGER NOT NULL)";

    private static final String CREATE_RESERVATIONS =
            "CREATE TABLE " + TABLE_RESERVATIONS + " (" +
            COL_RES_ID + " TEXT PRIMARY KEY, " +
            COL_RES_NO + " TEXT, " +
            COL_RES_NIC + " TEXT, " +
            COL_RES_PROSUMER_NAME + " TEXT, " +
            COL_RES_STATION_ID + " TEXT, " +
            COL_RES_STATION_CODE + " TEXT, " +
            COL_RES_STATION_NAME + " TEXT, " +
            COL_RES_SLOT_ID + " TEXT, " +
            COL_RES_SLOT_CODE + " TEXT, " +
            COL_RES_START_UTC + " TEXT, " +
            COL_RES_END_UTC + " TEXT, " +
            COL_RES_START_TIME + " TEXT, " +
            COL_RES_END_TIME + " TEXT, " +
            COL_RES_SLOT_TYPE + " TEXT, " +
            COL_RES_ENERGY + " REAL NOT NULL DEFAULT 0, " +
            COL_RES_DELIVERED + " REAL, " +
            COL_RES_STATUS + " TEXT, " +
            COL_RES_QR_TOKEN + " TEXT, " +
            COL_RES_CAN_MODIFY + " INTEGER NOT NULL DEFAULT 0, " +
            COL_RES_CAN_CANCEL + " INTEGER NOT NULL DEFAULT 0, " +
            COL_RES_HOURS_UNTIL + " REAL NOT NULL DEFAULT 0, " +
            COL_RES_CACHED_AT + " INTEGER NOT NULL)";

    private static final String CREATE_SYNC_LOG =
            "CREATE TABLE " + TABLE_SYNC_LOG + " (" +
            COL_SYNC_ID + " INTEGER PRIMARY KEY AUTOINCREMENT, " +
            COL_SYNC_ENTITY + " TEXT NOT NULL, " +
            COL_SYNC_ROW_COUNT + " INTEGER NOT NULL DEFAULT 0, " +
            COL_SYNC_AT + " INTEGER NOT NULL)";

    /** Indexes that back the list screens' filters. */
    private static final String[] CREATE_INDEXES = {
            "CREATE INDEX idx_res_status ON " + TABLE_RESERVATIONS + " (" + COL_RES_STATUS + ")",
            "CREATE INDEX idx_res_nic ON " + TABLE_RESERVATIONS + " (" + COL_RES_NIC + ")",
            "CREATE INDEX idx_res_start ON " + TABLE_RESERVATIONS + " (" + COL_RES_START_UTC + ")",
            "CREATE INDEX idx_station_status ON " + TABLE_STATIONS + " (" + COL_STATION_STATUS + ")"
    };

    private static DatabaseHelper instance;

    /** Private constructor - use getInstance so the whole app shares one helper. */
    private DatabaseHelper(Context context) {
        // The application context is used so the helper never holds on to an Activity.
        super(context.getApplicationContext(), DATABASE_NAME, null, DATABASE_VERSION);
    }

    /** Returns the process wide singleton, creating it on first use. */
    public static synchronized DatabaseHelper getInstance(Context context) {
        // A single helper keeps SQLite's own connection pooling and locking correct.
        if (instance == null) {
            instance = new DatabaseHelper(context);
        }
        return instance;
    }

    /** Creates every table and index the first time the app runs. */
    @Override
    public void onCreate(SQLiteDatabase db) {
        // Executed once, inside SQLiteOpenHelper's own transaction.
        Log.i(TAG, "Creating local SQLite schema version " + DATABASE_VERSION);
        db.execSQL(CREATE_SESSION);
        db.execSQL(CREATE_STATIONS);
        db.execSQL(CREATE_RESERVATIONS);
        db.execSQL(CREATE_SYNC_LOG);
        for (String index : CREATE_INDEXES) {
            db.execSQL(index);
        }
    }

    /**
     * Upgrades the schema. Every table here is a cache or a session that can be
     * rebuilt from the Web API, so dropping and recreating is safe and keeps the
     * upgrade path trivial. A table holding unsynchronised user data would need a
     * real migration instead.
     */
    @Override
    public void onUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) {
        // Safe because MongoDB behind the API remains the source of truth.
        Log.w(TAG, "Upgrading local database from " + oldVersion + " to " + newVersion + "; cached data will be rebuilt.");
        db.execSQL("DROP TABLE IF EXISTS " + TABLE_SYNC_LOG);
        db.execSQL("DROP TABLE IF EXISTS " + TABLE_RESERVATIONS);
        db.execSQL("DROP TABLE IF EXISTS " + TABLE_STATIONS);
        db.execSQL("DROP TABLE IF EXISTS " + TABLE_SESSION);
        onCreate(db);
    }

    /** Enables foreign key enforcement whenever a connection is opened. */
    @Override
    public void onConfigure(SQLiteDatabase db) {
        // SQLite has foreign keys off by default; turning them on is good practice.
        super.onConfigure(db);
        db.setForeignKeyConstraintsEnabled(true);
    }
}
