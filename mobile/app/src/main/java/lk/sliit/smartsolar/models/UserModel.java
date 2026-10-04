/* =============================================================================
 * File        : UserModel.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Models / User Model
 * Description : Parses account and profile details returned by the Web API.
 * Author      : IT22049872 - Chathushka
 * Created     : 2026-09-06
 * =============================================================================
 */

package lk.sliit.smartsolar.models;

import org.json.JSONObject;

import java.io.Serializable;

import lk.sliit.smartsolar.util.Constants;

/** A Backoffice officer, Grid Operator or Solar Prosumer account. */
public class UserModel implements Serializable {

    private String id;
    private String nic;
    private String username;
    private String email;
    private String fullName;
    private String phone;
    private String address;
    private String role;
    private String status;
    private boolean deactivationRequested;
    private String deactivationRequestedAt;
    private String deactivationReason;
    private String assignedStationId;
    private String assignedStationCode;
    private String lastLoginAt;
    private String createdAt;

    /** Creates an empty model; the setters or fromJson populate it. */
    public UserModel() {
        // A no argument constructor is needed by SessionManager when reading from SQLite.
    }

    /** Builds a model from the JSON object the Web API returned. */
    public static UserModel fromJson(JSONObject json) {
        // optString with an explicit null default keeps genuinely absent fields null.
        UserModel user = new UserModel();
        if (json == null) {
            return user;
        }

        user.id = json.optString("id", "");
        user.nic = json.isNull("nic") ? null : json.optString("nic", null);
        user.username = json.optString("username", "");
        user.email = json.optString("email", "");
        user.fullName = json.optString("fullName", "");
        user.phone = json.optString("phone", "");
        user.address = json.optString("address", "");
        user.role = json.optString("role", "");
        user.status = json.optString("status", "");
        user.deactivationRequested = json.optBoolean("deactivationRequested", false);
        user.deactivationRequestedAt = json.isNull("deactivationRequestedAt")
                ? null : json.optString("deactivationRequestedAt", null);
        user.deactivationReason = json.isNull("deactivationReason")
                ? null : json.optString("deactivationReason", null);
        user.assignedStationId = json.isNull("assignedStationId")
                ? null : json.optString("assignedStationId", null);
        user.assignedStationCode = json.isNull("assignedStationCode")
                ? null : json.optString("assignedStationCode", null);
        user.lastLoginAt = json.isNull("lastLoginAt") ? null : json.optString("lastLoginAt", null);
        user.createdAt = json.optString("createdAt", "");
        return user;
    }

    /** True when this account is a solar prosumer. */
    public boolean isProsumer() {
        // Used to pick the correct home screen after login.
        return Constants.ROLE_PROSUMER.equals(role);
    }

    /** True when this account is a microgrid site operator. */
    public boolean isGridOperator() {
        // Operators see the QR scanner and station tools.
        return Constants.ROLE_GRID_OPERATOR.equals(role);
    }

    /** True when this account is a back office administrator. */
    public boolean isBackoffice() {
        // Back office users normally work in the web application rather than the phone.
        return Constants.ROLE_BACKOFFICE.equals(role);
    }

    /** True when the account may currently sign in and transact. */
    public boolean isActive() {
        // Pending and Deactivated accounts are refused at login by the API (rules R5/R6).
        return Constants.STATUS_ACTIVE.equals(status);
    }

    /** Friendly label for the account, falling back to the username. */
    public String getDisplayName() {
        // Keeps the toolbar greeting sensible even for a profile with no full name.
        if (fullName != null && !fullName.trim().isEmpty()) {
            return fullName;
        }
        return username == null ? "" : username;
    }

    /** First letter of the display name, used for the avatar circle. */
    public String getInitial() {
        // Guards against an empty name so substring never throws.
        String name = getDisplayName();
        return name.isEmpty() ? "?" : name.substring(0, 1).toUpperCase();
    }

    // ---------------------------------------------------------------------
    // Getters and setters
    // ---------------------------------------------------------------------

    /** Mongo identifier of the account. */
    public String getId() { return id; }

    /** Sets the Mongo identifier. */
    public void setId(String id) { this.id = id; }

    /** National Identity Card number - the prosumer primary key. */
    public String getNic() { return nic; }

    /** Sets the NIC. */
    public void setNic(String nic) { this.nic = nic; }

    /** Login name. */
    public String getUsername() { return username; }

    /** Sets the login name. */
    public void setUsername(String username) { this.username = username; }

    /** E-mail address. */
    public String getEmail() { return email; }

    /** Sets the e-mail address. */
    public void setEmail(String email) { this.email = email; }

    /** Full name of the account holder. */
    public String getFullName() { return fullName; }

    /** Sets the full name. */
    public void setFullName(String fullName) { this.fullName = fullName; }

    /** Contact telephone number. */
    public String getPhone() { return phone; }

    /** Sets the telephone number. */
    public void setPhone(String phone) { this.phone = phone; }

    /** Postal address. */
    public String getAddress() { return address; }

    /** Sets the postal address. */
    public void setAddress(String address) { this.address = address; }

    /** Role name: Backoffice, GridOperator or Prosumer. */
    public String getRole() { return role; }

    /** Sets the role name. */
    public void setRole(String role) { this.role = role; }

    /** Account status: Pending, Active or Deactivated. */
    public String getStatus() { return status; }

    /** Sets the account status. */
    public void setStatus(String status) { this.status = status; }

    /** True when the prosumer has asked to be deactivated. */
    public boolean isDeactivationRequested() { return deactivationRequested; }

    /** Sets the deactivation request flag. */
    public void setDeactivationRequested(boolean value) { this.deactivationRequested = value; }

    /** When the deactivation was requested, ISO-8601 UTC. */
    public String getDeactivationRequestedAt() { return deactivationRequestedAt; }

    /** Reason supplied with the deactivation request. */
    public String getDeactivationReason() { return deactivationReason; }

    /** Home microgrid node of a grid operator. */
    public String getAssignedStationId() { return assignedStationId; }

    /** Sets the home microgrid node id. */
    public void setAssignedStationId(String id) { this.assignedStationId = id; }

    /** Code of the operator's home microgrid node. */
    public String getAssignedStationCode() { return assignedStationCode; }

    /** Sets the operator's home node code. */
    public void setAssignedStationCode(String code) { this.assignedStationCode = code; }

    /** Timestamp of the previous successful login. */
    public String getLastLoginAt() { return lastLoginAt; }

    /** When the account was created. */
    public String getCreatedAt() { return createdAt; }
}
