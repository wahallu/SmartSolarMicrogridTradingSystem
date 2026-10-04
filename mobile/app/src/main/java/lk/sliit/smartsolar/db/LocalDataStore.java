/* =============================================================================
 * File        : LocalDataStore.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Db / Local Data Store
 * Description : Stores recent API data for read-only offline use.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-24
 * =============================================================================
 */

package lk.sliit.smartsolar.db;

import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.util.Log;

import java.util.ArrayList;
import java.util.List;

import lk.sliit.smartsolar.models.ReservationModel;
import lk.sliit.smartsolar.models.StationModel;

/** Caches reference data and bookings in the local SQLite database. */
public class LocalDataStore {

    private static final String TAG = "LocalDataStore";

    private final DatabaseHelper helper;

    /** Obtains the shared database helper. */
    public LocalDataStore(Context context) {
        // One helper per process keeps SQLite's locking behaviour predictable.
        this.helper = DatabaseHelper.getInstance(context);
    }

    // =====================================================================
    // Stations
    // =====================================================================

    /** Replaces the cached station list with a freshly downloaded one. */
    public void cacheStations(List<StationModel> stations) {
        // The whole table is replaced inside one transaction so a partial write is impossible.
        if (stations == null) {
            return;
        }

        SQLiteDatabase db = helper.getWritableDatabase();
        db.beginTransaction();
        try {
            db.delete(DatabaseHelper.TABLE_STATIONS, null, null);
            long now = System.currentTimeMillis();

            for (StationModel station : stations) {
                ContentValues values = new ContentValues();
                values.put(DatabaseHelper.COL_STATION_ID, station.getId());
                values.put(DatabaseHelper.COL_STATION_CODE, station.getStationCode());
                values.put(DatabaseHelper.COL_STATION_NAME, station.getName());
                values.put(DatabaseHelper.COL_STATION_TYPE, station.getType());
                values.put(DatabaseHelper.COL_STATION_ADDRESS, station.getAddress());
                values.put(DatabaseHelper.COL_STATION_CITY, station.getCity());
                values.put(DatabaseHelper.COL_STATION_LAT, station.getLatitude());
                values.put(DatabaseHelper.COL_STATION_LNG, station.getLongitude());
                values.put(DatabaseHelper.COL_STATION_CAPACITY, station.getCapacityKwh());
                values.put(DatabaseHelper.COL_STATION_TOTAL_SLOTS, station.getTotalBatterySlots());
                values.put(DatabaseHelper.COL_STATION_AVAILABLE_SLOTS, station.getAvailableBatterySlots());
                values.put(DatabaseHelper.COL_STATION_CONTACT, station.getContactNumber());
                values.put(DatabaseHelper.COL_STATION_STATUS, station.getStatus());
                values.put(DatabaseHelper.COL_STATION_CACHED_AT, now);
                db.insertWithOnConflict(DatabaseHelper.TABLE_STATIONS, null, values,
                        SQLiteDatabase.CONFLICT_REPLACE);
            }

            recordSync(db, "stations", stations.size(), now);
            db.setTransactionSuccessful();
            Log.i(TAG, "Cached " + stations.size() + " station(s) locally.");
        } catch (Exception ex) {
            Log.e(TAG, "Failed to cache stations.", ex);
        } finally {
            db.endTransaction();
        }
    }

    /** Reads the cached station list, newest cache first. */
    public List<StationModel> getCachedStations() {
        // Returns an empty list rather than null so callers never need a null check.
        List<StationModel> stations = new ArrayList<>();
        SQLiteDatabase db = helper.getReadableDatabase();

        try (Cursor cursor = db.query(DatabaseHelper.TABLE_STATIONS, null, null, null, null, null,
                DatabaseHelper.COL_STATION_CODE + " ASC")) {

            while (cursor != null && cursor.moveToNext()) {
                StationModel station = new StationModel();
                station.setId(text(cursor, DatabaseHelper.COL_STATION_ID));
                station.setStationCode(text(cursor, DatabaseHelper.COL_STATION_CODE));
                station.setName(text(cursor, DatabaseHelper.COL_STATION_NAME));
                station.setType(text(cursor, DatabaseHelper.COL_STATION_TYPE));
                station.setAddress(text(cursor, DatabaseHelper.COL_STATION_ADDRESS));
                station.setCity(text(cursor, DatabaseHelper.COL_STATION_CITY));
                station.setLatitude(real(cursor, DatabaseHelper.COL_STATION_LAT));
                station.setLongitude(real(cursor, DatabaseHelper.COL_STATION_LNG));
                station.setCapacityKwh(real(cursor, DatabaseHelper.COL_STATION_CAPACITY));
                station.setTotalBatterySlots(integer(cursor, DatabaseHelper.COL_STATION_TOTAL_SLOTS));
                station.setAvailableBatterySlots(integer(cursor, DatabaseHelper.COL_STATION_AVAILABLE_SLOTS));
                station.setContactNumber(text(cursor, DatabaseHelper.COL_STATION_CONTACT));
                station.setStatus(text(cursor, DatabaseHelper.COL_STATION_STATUS));
                stations.add(station);
            }
        } catch (Exception ex) {
            Log.e(TAG, "Failed to read cached stations.", ex);
        }
        return stations;
    }

    // =====================================================================
    // Reservations
    // =====================================================================

    /** Replaces the cached reservation list with a freshly downloaded one. */
    public void cacheReservations(List<ReservationModel> reservations) {
        // As with stations, the replacement happens inside a single transaction.
        if (reservations == null) {
            return;
        }

        SQLiteDatabase db = helper.getWritableDatabase();
        db.beginTransaction();
        try {
            db.delete(DatabaseHelper.TABLE_RESERVATIONS, null, null);
            long now = System.currentTimeMillis();

            for (ReservationModel r : reservations) {
                ContentValues values = new ContentValues();
                values.put(DatabaseHelper.COL_RES_ID, r.getId());
                values.put(DatabaseHelper.COL_RES_NO, r.getReservationNo());
                values.put(DatabaseHelper.COL_RES_NIC, r.getProsumerNic());
                values.put(DatabaseHelper.COL_RES_PROSUMER_NAME, r.getProsumerName());
                values.put(DatabaseHelper.COL_RES_STATION_ID, r.getStationId());
                values.put(DatabaseHelper.COL_RES_STATION_CODE, r.getStationCode());
                values.put(DatabaseHelper.COL_RES_STATION_NAME, r.getStationName());
                values.put(DatabaseHelper.COL_RES_SLOT_ID, r.getSlotId());
                values.put(DatabaseHelper.COL_RES_SLOT_CODE, r.getSlotCode());
                values.put(DatabaseHelper.COL_RES_START_UTC, r.getReservationStartUtc());
                values.put(DatabaseHelper.COL_RES_END_UTC, r.getReservationEndUtc());
                values.put(DatabaseHelper.COL_RES_START_TIME, r.getStartTime());
                values.put(DatabaseHelper.COL_RES_END_TIME, r.getEndTime());
                values.put(DatabaseHelper.COL_RES_SLOT_TYPE, r.getSlotType());
                values.put(DatabaseHelper.COL_RES_ENERGY, r.getEnergyKwh());
                if (r.getDeliveredKwh() != null) {
                    values.put(DatabaseHelper.COL_RES_DELIVERED, r.getDeliveredKwh());
                }
                values.put(DatabaseHelper.COL_RES_STATUS, r.getStatus());
                values.put(DatabaseHelper.COL_RES_QR_TOKEN, r.getQrToken());
                values.put(DatabaseHelper.COL_RES_CAN_MODIFY, r.canModify() ? 1 : 0);
                values.put(DatabaseHelper.COL_RES_CAN_CANCEL, r.canCancel() ? 1 : 0);
                values.put(DatabaseHelper.COL_RES_HOURS_UNTIL, r.getHoursUntilStart());
                values.put(DatabaseHelper.COL_RES_CACHED_AT, now);
                db.insertWithOnConflict(DatabaseHelper.TABLE_RESERVATIONS, null, values,
                        SQLiteDatabase.CONFLICT_REPLACE);
            }

            recordSync(db, "reservations", reservations.size(), now);
            db.setTransactionSuccessful();
            Log.i(TAG, "Cached " + reservations.size() + " reservation(s) locally.");
        } catch (Exception ex) {
            Log.e(TAG, "Failed to cache reservations.", ex);
        } finally {
            db.endTransaction();
        }
    }

    /**
     * Reads cached reservations, optionally filtered by status. Passing null
     * returns every cached row, soonest slot first.
     */
    public List<ReservationModel> getCachedReservations(String statusFilter) {
        // The filter mirrors the status chips on the bookings screen.
        List<ReservationModel> list = new ArrayList<>();
        SQLiteDatabase db = helper.getReadableDatabase();

        String selection = null;
        String[] args = null;
        if (statusFilter != null && !statusFilter.trim().isEmpty()) {
            selection = DatabaseHelper.COL_RES_STATUS + " = ?";
            args = new String[]{statusFilter};
        }

        try (Cursor cursor = db.query(DatabaseHelper.TABLE_RESERVATIONS, null, selection, args,
                null, null, DatabaseHelper.COL_RES_START_UTC + " DESC")) {

            while (cursor != null && cursor.moveToNext()) {
                ReservationModel r = new ReservationModel();
                r.setId(text(cursor, DatabaseHelper.COL_RES_ID));
                r.setReservationNo(text(cursor, DatabaseHelper.COL_RES_NO));
                r.setProsumerNic(text(cursor, DatabaseHelper.COL_RES_NIC));
                r.setProsumerName(text(cursor, DatabaseHelper.COL_RES_PROSUMER_NAME));
                r.setStationId(text(cursor, DatabaseHelper.COL_RES_STATION_ID));
                r.setStationCode(text(cursor, DatabaseHelper.COL_RES_STATION_CODE));
                r.setStationName(text(cursor, DatabaseHelper.COL_RES_STATION_NAME));
                r.setSlotId(text(cursor, DatabaseHelper.COL_RES_SLOT_ID));
                r.setSlotCode(text(cursor, DatabaseHelper.COL_RES_SLOT_CODE));
                r.setReservationStartUtc(text(cursor, DatabaseHelper.COL_RES_START_UTC));
                r.setReservationEndUtc(text(cursor, DatabaseHelper.COL_RES_END_UTC));
                r.setStartTime(text(cursor, DatabaseHelper.COL_RES_START_TIME));
                r.setEndTime(text(cursor, DatabaseHelper.COL_RES_END_TIME));
                r.setSlotType(text(cursor, DatabaseHelper.COL_RES_SLOT_TYPE));
                r.setEnergyKwh(real(cursor, DatabaseHelper.COL_RES_ENERGY));

                int deliveredIndex = cursor.getColumnIndex(DatabaseHelper.COL_RES_DELIVERED);
                if (deliveredIndex >= 0 && !cursor.isNull(deliveredIndex)) {
                    r.setDeliveredKwh(cursor.getDouble(deliveredIndex));
                }

                r.setStatus(text(cursor, DatabaseHelper.COL_RES_STATUS));
                r.setQrToken(text(cursor, DatabaseHelper.COL_RES_QR_TOKEN));
                r.setCanModify(integer(cursor, DatabaseHelper.COL_RES_CAN_MODIFY) == 1);
                r.setCanCancel(integer(cursor, DatabaseHelper.COL_RES_CAN_CANCEL) == 1);
                r.setHoursUntilStart(real(cursor, DatabaseHelper.COL_RES_HOURS_UNTIL));
                list.add(r);
            }
        } catch (Exception ex) {
            Log.e(TAG, "Failed to read cached reservations.", ex);
        }
        return list;
    }

    /** Number of cached reservations currently held, used for the offline banner. */
    public int getCachedReservationCount() {
        // A cheap count avoids materialising the whole list just to decide whether to show a banner.
        SQLiteDatabase db = helper.getReadableDatabase();
        try (Cursor cursor = db.rawQuery("SELECT COUNT(*) FROM " + DatabaseHelper.TABLE_RESERVATIONS, null)) {
            if (cursor != null && cursor.moveToFirst()) {
                return cursor.getInt(0);
            }
        } catch (Exception ex) {
            Log.e(TAG, "Failed to count cached reservations.", ex);
        }
        return 0;
    }

    /** Milliseconds since the epoch when the named entity was last cached, or 0. */
    public long getLastSyncTime(String entity) {
        // Shown as "last updated ..." on the pull to refresh screens.
        SQLiteDatabase db = helper.getReadableDatabase();
        try (Cursor cursor = db.query(DatabaseHelper.TABLE_SYNC_LOG,
                new String[]{DatabaseHelper.COL_SYNC_AT},
                DatabaseHelper.COL_SYNC_ENTITY + " = ?", new String[]{entity},
                null, null, DatabaseHelper.COL_SYNC_AT + " DESC", "1")) {

            if (cursor != null && cursor.moveToFirst()) {
                return cursor.getLong(0);
            }
        } catch (Exception ex) {
            Log.e(TAG, "Failed to read the sync log.", ex);
        }
        return 0;
    }

    /** Appends a row to the sync audit log. */
    private void recordSync(SQLiteDatabase db, String entity, int rowCount, long timestamp) {
        // Called inside the caller's transaction so the log and the data commit together.
        ContentValues values = new ContentValues();
        values.put(DatabaseHelper.COL_SYNC_ENTITY, entity);
        values.put(DatabaseHelper.COL_SYNC_ROW_COUNT, rowCount);
        values.put(DatabaseHelper.COL_SYNC_AT, timestamp);
        db.insert(DatabaseHelper.TABLE_SYNC_LOG, null, values);
    }

    /** Reads a text column, tolerating a missing or null column. */
    private String text(Cursor cursor, String column) {
        // getColumnIndex returns -1 rather than throwing, so the index is always checked.
        int index = cursor.getColumnIndex(column);
        return (index < 0 || cursor.isNull(index)) ? null : cursor.getString(index);
    }

    /** Reads a real column, defaulting to zero. */
    private double real(Cursor cursor, String column) {
        // A missing numeric column reads as zero, which every caller can render safely.
        int index = cursor.getColumnIndex(column);
        return (index < 0 || cursor.isNull(index)) ? 0 : cursor.getDouble(index);
    }

    /** Reads an integer column, defaulting to zero. */
    private int integer(Cursor cursor, String column) {
        // Same defensive pattern as real().
        int index = cursor.getColumnIndex(column);
        return (index < 0 || cursor.isNull(index)) ? 0 : cursor.getInt(index);
    }
}
