/*
 * =============================================================================
 * File        : DateUtils.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : util
 * Description : Parsing and formatting for the ISO-8601 UTC timestamps the Web
 *               API exchanges. These helpers are strictly presentational - they
 *               turn a server instant into something readable on a phone. No
 *               business rule is evaluated here; the 7 day window and the 12
 *               hour notice period are decided by the API and arrive as flags.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.util;

import android.util.Log;

import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.util.Calendar;
import java.util.Date;
import java.util.Locale;
import java.util.TimeZone;

/** Formats the API's UTC timestamps for display. */
public final class DateUtils {

    private static final String TAG = "DateUtils";

    /** Utility class - never instantiated. */
    private DateUtils() {
        // Prevents construction of a helper that holds no state.
    }

    /**
     * Parses an ISO-8601 timestamp as produced by System.Text.Json, tolerating a
     * trailing Z, a fractional seconds part of any length, or neither.
     */
    public static Date parseIso(String iso) {
        // Several shapes appear across endpoints, so each candidate pattern is tried in turn.
        if (iso == null || iso.trim().isEmpty()) {
            return null;
        }

        String value = iso.trim();

        // Normalise the fractional seconds to exactly three digits, which SimpleDateFormat expects.
        int dot = value.indexOf('.');
        if (dot > 0) {
            int end = dot + 1;
            while (end < value.length() && Character.isDigit(value.charAt(end))) {
                end++;
            }
            String fraction = value.substring(dot + 1, end);
            String rest = value.substring(end);
            if (fraction.length() > 3) {
                fraction = fraction.substring(0, 3);
            }
            while (fraction.length() < 3) {
                fraction = fraction + "0";
            }
            value = value.substring(0, dot + 1) + fraction + rest;
        }

        String[] patterns = {
                "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'",
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                "yyyy-MM-dd'T'HH:mm:ss.SSS",
                "yyyy-MM-dd'T'HH:mm:ss",
                "yyyy-MM-dd"
        };

        for (String pattern : patterns) {
            try {
                SimpleDateFormat format = new SimpleDateFormat(pattern, Locale.US);
                format.setTimeZone(TimeZone.getTimeZone("UTC"));
                format.setLenient(false);
                return format.parse(value);
            } catch (ParseException ignored) {
                // Try the next pattern.
            }
        }

        Log.w(TAG, "Could not parse timestamp: " + iso);
        return null;
    }

    /** Formats an instant as "04 Sep 2026", or an empty string when unparsable. */
    public static String formatDate(String iso) {
        // Used on booking cards and history rows.
        Date date = parseIso(iso);
        return date == null ? "" : utcFormat("dd MMM yyyy").format(date);
    }

    /** Formats an instant as "04 Sep 2026, 08:00". */
    public static String formatDateTime(String iso) {
        // Used wherever the exact slot start matters.
        Date date = parseIso(iso);
        return date == null ? "" : utcFormat("dd MMM yyyy, HH:mm").format(date);
    }

    /** Formats an instant as "Fri, 04 Sep". */
    public static String formatDayDate(String iso) {
        // Used as a section header in the booking lists.
        Date date = parseIso(iso);
        return date == null ? "" : utcFormat("EEE, dd MMM").format(date);
    }

    /** Formats an instant as "08:00". */
    public static String formatTime(String iso) {
        // Used when the date is already displayed elsewhere on the row.
        Date date = parseIso(iso);
        return date == null ? "" : utcFormat("HH:mm").format(date);
    }

    /** Formats a Date as the "yyyy-MM-dd" string the API's date filters expect. */
    public static String toApiDate(Date date) {
        // Slot queries take a plain calendar day rather than a full instant.
        if (date == null) {
            return "";
        }
        return utcFormat("yyyy-MM-dd").format(date);
    }

    /** Today's date in the "yyyy-MM-dd" form the API expects. */
    public static String today() {
        // The new booking screen defaults its date picker to today.
        return toApiDate(new Date());
    }

    /** A date this many days after today, in "yyyy-MM-dd" form. */
    public static String daysFromToday(int days) {
        // Used to cap the booking date picker at the 7 day window the API enforces.
        Calendar calendar = Calendar.getInstance(TimeZone.getTimeZone("UTC"));
        calendar.add(Calendar.DAY_OF_YEAR, days);
        return toApiDate(calendar.getTime());
    }

    /**
     * Turns the server's hoursUntilStart figure into a short phrase such as
     * "in 3 days", "in 5 hours" or "started 2 hours ago".
     */
    public static String describeHoursUntil(double hours) {
        // Purely cosmetic - the button state comes from canModify / canCancel, not from this.
        if (hours < 0) {
            double past = Math.abs(hours);
            if (past < 1) {
                return "started just now";
            }
            if (past < 24) {
                return "started " + (int) past + (((int) past) == 1 ? " hour ago" : " hours ago");
            }
            int days = (int) (past / 24);
            return "started " + days + (days == 1 ? " day ago" : " days ago");
        }

        if (hours < 1) {
            int minutes = (int) Math.round(hours * 60);
            return "in " + Math.max(minutes, 1) + (minutes == 1 ? " minute" : " minutes");
        }
        if (hours < 24) {
            int whole = (int) hours;
            return "in " + whole + (whole == 1 ? " hour" : " hours");
        }
        int days = (int) (hours / 24);
        return "in " + days + (days == 1 ? " day" : " days");
    }

    /** Builds a UTC SimpleDateFormat for the given pattern. */
    private static SimpleDateFormat utcFormat(String pattern) {
        // Every timestamp in this system is UTC, so the display is UTC too and stays
        // consistent with the slot times the operator reads at the node.
        SimpleDateFormat format = new SimpleDateFormat(pattern, Locale.getDefault());
        format.setTimeZone(TimeZone.getTimeZone("UTC"));
        return format;
    }
}
