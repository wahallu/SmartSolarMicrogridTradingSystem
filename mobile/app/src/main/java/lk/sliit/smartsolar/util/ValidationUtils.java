/*
 * =============================================================================
 * File        : ValidationUtils.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : util
 * Description : Field level input checks used by the registration, profile and
 *               booking forms.
 *
 *               SCOPE: these checks exist only to give the user immediate
 *               feedback while typing. They are NOT the system's validation -
 *               the Web API re-validates everything and is the only authority.
 *               Nothing here duplicates a business rule; the 7 day window, the
 *               12 hour notice period and slot availability are all decided by
 *               the server.
 * Author      : Chathushka - Identity, Access Control & Local Session (SE4040 EAD)
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.util;

import android.util.Patterns;

import java.util.regex.Pattern;

/** Immediate feedback checks for the on screen forms. */
public final class ValidationUtils {

    /**
     * Sri Lankan NIC: either nine digits followed by V or X (the old format), or
     * twelve digits (the current format). The same expression is enforced on the
     * server, so a value accepted here will be accepted there.
     */
    private static final Pattern NIC_PATTERN = Pattern.compile("^(\\d{9}[VvXx]|\\d{12})$");

    /** Nine to fifteen characters of digits, spaces, brackets, plus and hyphen. */
    private static final Pattern PHONE_PATTERN = Pattern.compile("^[0-9+\\-\\s()]{9,15}$");

    /** Utility class - never instantiated. */
    private ValidationUtils() {
        // Prevents construction of a helper that holds no state.
    }

    /** True when the value is null, empty or only whitespace. */
    public static boolean isBlank(String value) {
        // Used before every other check so a blank field reports "required" rather than "invalid".
        return value == null || value.trim().isEmpty();
    }

    /** Returns an error message for a NIC, or null when it is acceptable. */
    public static String validateNic(String nic) {
        // Mirrors the RegularExpression attribute on RegisterProsumerRequest.
        if (isBlank(nic)) {
            return "NIC is required.";
        }
        if (!NIC_PATTERN.matcher(nic.trim()).matches()) {
            return "Enter a valid NIC: 9 digits followed by V or X, or 12 digits.";
        }
        return null;
    }

    /** Returns an error message for an e-mail address, or null when acceptable. */
    public static String validateEmail(String email) {
        // Patterns.EMAIL_ADDRESS is the framework's own matcher, so behaviour matches the platform.
        if (isBlank(email)) {
            return "E-mail is required.";
        }
        if (!Patterns.EMAIL_ADDRESS.matcher(email.trim()).matches()) {
            return "Enter a valid e-mail address.";
        }
        return null;
    }

    /** Returns an error message for a phone number, or null when acceptable. */
    public static String validatePhone(String phone) {
        // Deliberately permissive: the server applies the authoritative rule.
        if (isBlank(phone)) {
            return "Phone number is required.";
        }
        if (!PHONE_PATTERN.matcher(phone.trim()).matches()) {
            return "Enter a valid phone number.";
        }
        return null;
    }

    /** Returns an error message for a full name, or null when acceptable. */
    public static String validateFullName(String name) {
        // Matches the StringLength(120, MinimumLength = 3) attribute on the server DTO.
        if (isBlank(name)) {
            return "Full name is required.";
        }
        String trimmed = name.trim();
        if (trimmed.length() < 3) {
            return "Full name must be at least 3 characters.";
        }
        if (trimmed.length() > 120) {
            return "Full name cannot exceed 120 characters.";
        }
        return null;
    }

    /** Returns an error message for a password, or null when acceptable. */
    public static String validatePassword(String password) {
        // Six characters is the server minimum; anything stronger is encouraged, not enforced.
        if (isBlank(password)) {
            return "Password is required.";
        }
        if (password.length() < 6) {
            return "Password must be at least 6 characters.";
        }
        if (password.length() > 64) {
            return "Password cannot exceed 64 characters.";
        }
        return null;
    }

    /** Returns an error message when two passwords differ, or null when they match. */
    public static String validatePasswordMatch(String password, String confirm) {
        // A purely local check - the server never sees the confirmation field.
        if (isBlank(confirm)) {
            return "Please confirm your password.";
        }
        if (!confirm.equals(password)) {
            return "The passwords do not match.";
        }
        return null;
    }

    /** Returns an error message for an address, or null when acceptable. */
    public static String validateAddress(String address) {
        // Matches the StringLength(250, MinimumLength = 5) attribute on the server DTO.
        if (isBlank(address)) {
            return "Address is required.";
        }
        if (address.trim().length() < 5) {
            return "Address must be at least 5 characters.";
        }
        return null;
    }

    /** Returns an error message for a kWh amount, or null when acceptable. */
    public static String validateEnergy(String energyText, double slotCapacity) {
        // The capacity ceiling shown here is the one the server sent for the chosen slot.
        if (isBlank(energyText)) {
            return "Enter the energy amount in kWh.";
        }
        try {
            double energy = Double.parseDouble(energyText.trim());
            if (energy <= 0) {
                return "Energy must be greater than zero.";
            }
            if (slotCapacity > 0 && energy > slotCapacity) {
                return String.format(java.util.Locale.getDefault(),
                        "This slot can trade at most %.1f kWh.", slotCapacity);
            }
            return null;
        } catch (NumberFormatException ex) {
            return "Enter a valid number.";
        }
    }

    /** Normalises a NIC for sending to the API: trimmed and upper case. */
    public static String normaliseNic(String nic) {
        // The server stores NICs upper case, so the client matches to avoid a needless mismatch.
        return nic == null ? "" : nic.trim().toUpperCase();
    }
}
