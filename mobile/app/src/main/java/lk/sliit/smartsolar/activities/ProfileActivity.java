/* =============================================================================
 * File        : ProfileActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Activities / Profile
 * Description : Displays and updates the signed-in user's account details.
 * Author      : IT22049872 - Chathushka
 * Created     : 2026-09-07
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.content.DialogInterface;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.annotation.Nullable;
import androidx.appcompat.app.AlertDialog;
import androidx.core.content.ContextCompat;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.card.MaterialCardView;
import com.google.android.material.textfield.TextInputEditText;
import com.google.android.material.textfield.TextInputLayout;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.api.ApiClient;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.models.UserModel;
import lk.sliit.smartsolar.util.Constants;
import lk.sliit.smartsolar.util.ValidationUtils;

/** Shows and edits the signed in user's own account. */
public class ProfileActivity extends BaseActivity {

    // The three mutually exclusive states of the screen.
    private View layoutLoading;
    private View layoutContent;
    private View layoutError;
    private TextView textError;
    private MaterialButton btnRetry;

    // Identity header.
    private TextView textInitial;
    private TextView textName;
    private TextView textRole;
    private TextView textStatus;

    // Deactivation notice banner.
    private View panelDeactivationPending;

    // Read only account details.
    private TextView textDetailNic;
    private TextView textDetailUsername;
    private TextView textDetailEmail;
    private TextView textDetailPhone;
    private TextView textDetailAddress;
    private View rowDetailStation;
    private TextView textDetailStation;

    // Edit profile card and the staff panel that replaces it.
    private MaterialCardView cardEditProfile;
    private View panelStaffProfile;
    private TextInputLayout tilFullName;
    private TextInputLayout tilEmail;
    private TextInputLayout tilPhone;
    private TextInputLayout tilAddress;
    private TextInputEditText etFullName;
    private TextInputEditText etEmail;
    private TextInputEditText etPhone;
    private TextInputEditText etAddress;
    private MaterialButton btnSaveProfile;
    private ProgressBar progressSaveProfile;

    // Change password card.
    private TextInputLayout tilCurrentPassword;
    private TextInputLayout tilNewPassword;
    private TextInputLayout tilConfirmNewPassword;
    private TextInputEditText etCurrentPassword;
    private TextInputEditText etNewPassword;
    private TextInputEditText etConfirmNewPassword;
    private MaterialButton btnChangePassword;
    private ProgressBar progressChangePassword;

    // Deactivation request card.
    private MaterialCardView cardDeactivate;
    private MaterialButton btnRequestDeactivation;

    private MaterialButton btnLogout;

    /** The profile currently on screen, as the Web API last reported it. */
    private UserModel profile;

    /** Inflates the screen, wires every action and starts the first profile load. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // super.onCreate must run first because it creates api, session and localStore.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_profile);

        setupToolbar(R.id.toolbar_profile, getString(R.string.profile_title), true);

        bindViews();
        setupActions();

        loadProfile();
    }

    /** Looks up every view this screen drives. */
    private void bindViews() {
        // findViewById only: view binding and data binding are not permitted on this project.
        layoutLoading = findViewById(R.id.layout_profile_loading);
        layoutContent = findViewById(R.id.layout_profile_content);
        layoutError = findViewById(R.id.layout_profile_error);
        textError = findViewById(R.id.text_profile_error);
        btnRetry = findViewById(R.id.btn_profile_retry);

        textInitial = findViewById(R.id.text_profile_initial);
        textName = findViewById(R.id.text_profile_name);
        textRole = findViewById(R.id.text_profile_role);
        textStatus = findViewById(R.id.text_profile_status);

        panelDeactivationPending = findViewById(R.id.panel_deactivation_pending);

        textDetailNic = findViewById(R.id.text_detail_nic);
        textDetailUsername = findViewById(R.id.text_detail_username);
        textDetailEmail = findViewById(R.id.text_detail_email);
        textDetailPhone = findViewById(R.id.text_detail_phone);
        textDetailAddress = findViewById(R.id.text_detail_address);
        rowDetailStation = findViewById(R.id.row_detail_station);
        textDetailStation = findViewById(R.id.text_detail_station);

        cardEditProfile = findViewById(R.id.card_edit_profile);
        panelStaffProfile = findViewById(R.id.panel_staff_profile);
        tilFullName = findViewById(R.id.til_profile_full_name);
        tilEmail = findViewById(R.id.til_profile_email);
        tilPhone = findViewById(R.id.til_profile_phone);
        tilAddress = findViewById(R.id.til_profile_address);
        etFullName = findViewById(R.id.et_profile_full_name);
        etEmail = findViewById(R.id.et_profile_email);
        etPhone = findViewById(R.id.et_profile_phone);
        etAddress = findViewById(R.id.et_profile_address);
        btnSaveProfile = findViewById(R.id.btn_save_profile);
        progressSaveProfile = findViewById(R.id.progress_save_profile);

        tilCurrentPassword = findViewById(R.id.til_current_password);
        tilNewPassword = findViewById(R.id.til_new_password);
        tilConfirmNewPassword = findViewById(R.id.til_confirm_new_password);
        etCurrentPassword = findViewById(R.id.et_current_password);
        etNewPassword = findViewById(R.id.et_new_password);
        etConfirmNewPassword = findViewById(R.id.et_confirm_new_password);
        btnChangePassword = findViewById(R.id.btn_change_password);
        progressChangePassword = findViewById(R.id.progress_change_password);

        cardDeactivate = findViewById(R.id.card_deactivate);
        btnRequestDeactivation = findViewById(R.id.btn_request_deactivation);

        btnLogout = findViewById(R.id.btn_profile_logout);
    }

    /** Connects the five buttons on the screen to their handlers. */
    private void setupActions() {
        // Each handler owns exactly one API call, which keeps the flow easy to follow.
        btnRetry.setOnClickListener(new View.OnClickListener() {

            /** Retries the profile load after a failure. */
            @Override
            public void onClick(View view) {
                // Same path as the first load, so there is only one loading routine.
                loadProfile();
            }
        });

        btnSaveProfile.setOnClickListener(new View.OnClickListener() {

            /** Validates the edit form and sends it to the API. */
            @Override
            public void onClick(View view) {
                // The server re-validates everything and remains the only authority.
                saveProfile();
            }
        });

        btnChangePassword.setOnClickListener(new View.OnClickListener() {

            /** Validates the password form and sends it to the API. */
            @Override
            public void onClick(View view) {
                // Only the confirmation match is checked locally; the current password is
                // verified by the server, which is the only place that knows the hash.
                changePassword();
            }
        });

        btnRequestDeactivation.setOnClickListener(new View.OnClickListener() {

            /** Opens the reason dialog before raising a deactivation request. */
            @Override
            public void onClick(View view) {
                // Raising the request is all the phone can do - see promptDeactivation.
                promptDeactivation();
            }
        });

        btnLogout.setOnClickListener(new View.OnClickListener() {

            /** Signs the user out through the shared BaseActivity routine. */
            @Override
            public void onClick(View view) {
                // performLogout confirms first, then clears the SQLite session and caches.
                performLogout();
            }
        });
    }

    // =====================================================================
    // Loading
    // =====================================================================

    /**
     * Reads the live profile from GET /auth/me so the details on screen are the
     * server's current view of the account, not a possibly stale cached copy.
     */
    private void loadProfile() {
        // The loading state is shown first so the user never reads a half filled form.
        showOnly(layoutLoading, layoutContent, layoutError);

        api.get(Constants.EP_ME, token(), new ApiClient.Callback() {

            /** Receives the server reply on the main thread. */
            @Override
            public void onResult(ApiResult result) {
                // ApiClient posts callbacks to the main looper, so views may be touched directly.
                if (result.isSuccess() && result.getDataObject() != null) {
                    UserModel fresh = UserModel.fromJson(result.getDataObject());

                    // Keeping the SQLite session in step means the home screen header and
                    // the next cold start both show what the server just reported.
                    session.updateProfile(fresh);

                    bindProfile(fresh);
                    showOnly(layoutContent, layoutLoading, layoutError);
                    return;
                }

                handleLoadFailure(result);
            }
        });
    }

    /**
     * Handles a failed profile load. When the phone simply has no connection the
     * cached SQLite session is shown instead, so the user can still read their
     * own details offline; any other failure shows the error state.
     */
    private void handleLoadFailure(ApiResult result) {
        // An expired session is dealt with centrally by BaseActivity.
        if (handleAuthFailure(result)) {
            return;
        }

        UserModel cached = session.getUser();
        if (result.isNetworkError() && cached != null) {
            // FALLBACK: the locally stored session copy of the profile. It is a cache of the
            // last successful server response and is never treated as the authority.
            bindProfile(cached);
            showOnly(layoutContent, layoutLoading, layoutError);
            toast(getString(R.string.offline_cached_notice));
            return;
        }

        textError.setText(result.getMessage().isEmpty()
                ? getString(R.string.error_generic)
                : result.getMessage());
        showOnly(layoutError, layoutLoading, layoutContent);
        showApiError(result);
    }

    // =====================================================================
    // Rendering
    // =====================================================================

    /** Paints the whole screen from one user model. */
    private void bindProfile(UserModel user) {
        // Every value below was supplied by the Web API; nothing is derived on the phone.
        if (user == null) {
            return;
        }
        profile = user;

        textInitial.setText(user.getInitial());
        textName.setText(user.getDisplayName());

        // The role and the account status are both server owned values.
        textRole.setText(valueOrDash(user.getRole()));
        tintChip(textRole, R.color.solar_100, R.color.solar_800);

        textStatus.setText(valueOrDash(user.getStatus()));
        tintChip(textStatus, statusFillColour(user.getStatus()), statusTextColour(user.getStatus()));

        bindDetails(user);
        bindRoleSpecificSections(user);
        bindDeactivationState(user);
    }

    /** Fills the read only details card. */
    private void bindDetails(UserModel user) {
        // A staff account has no NIC, so a dash is shown rather than an empty row.
        textDetailNic.setText(valueOrDash(user.getNic()));
        textDetailUsername.setText(valueOrDash(user.getUsername()));
        textDetailEmail.setText(valueOrDash(user.getEmail()));
        textDetailPhone.setText(valueOrDash(user.getPhone()));
        textDetailAddress.setText(valueOrDash(user.getAddress()));

        // Only a Grid Operator is tied to a home microgrid node, so the row appears
        // only when the API actually sent one.
        String station = ValidationUtils.isBlank(user.getAssignedStationCode())
                ? user.getAssignedStationId()
                : user.getAssignedStationCode();

        if (ValidationUtils.isBlank(station)) {
            rowDetailStation.setVisibility(View.GONE);
        } else {
            textDetailStation.setText(station);
            rowDetailStation.setVisibility(View.VISIBLE);
        }
    }

    /**
     * Decides which of the two profile blocks the account may use.
     *
     * A Prosumer owns their own record, so PUT /prosumers/{nic} accepts an edit
     * from them (the API allows "Backoffice, self"). A Grid Operator or a
     * Backoffice officer is a staff account created and maintained by a
     * Backoffice officer in the web application - there is no self service
     * endpoint for them at all, so the edit form is replaced by an information
     * panel rather than shown as a control that would only fail with a 403.
     */
    private void bindRoleSpecificSections(UserModel user) {
        // The same reasoning applies to the deactivation request, which is a
        // prosumer-only endpoint on the API.
        boolean isProsumer = user.isProsumer();

        cardEditProfile.setVisibility(isProsumer ? View.VISIBLE : View.GONE);
        panelStaffProfile.setVisibility(isProsumer ? View.GONE : View.VISIBLE);
        cardDeactivate.setVisibility(isProsumer ? View.VISIBLE : View.GONE);

        if (isProsumer) {
            // Pre-filling the form means the user edits their details instead of retyping them.
            etFullName.setText(user.getFullName());
            etEmail.setText(user.getEmail());
            etPhone.setText(user.getPhone());
            etAddress.setText(user.getAddress());
        }
    }

    /**
     * Shows the pending deactivation banner when the profile already carries the
     * flag. The flag is set by the Web API when the request is accepted, and only
     * a Backoffice officer can complete the deactivation (rule R5), so the phone
     * simply reports what the server said.
     */
    private void bindDeactivationState(UserModel user) {
        // A second request would be pointless while one is already awaiting review.
        boolean pending = user.isDeactivationRequested();
        panelDeactivationPending.setVisibility(pending ? View.VISIBLE : View.GONE);
        btnRequestDeactivation.setEnabled(!pending);
    }

    // =====================================================================
    // Edit profile
    // =====================================================================

    /** Validates the edit form and sends it to PUT /prosumers/{nic}. */
    private void saveProfile() {
        // Nothing leaves the phone until the local checks pass, but the API re-validates.
        if (!validateProfileForm()) {
            return;
        }

        String nic = currentNic();
        if (ValidationUtils.isBlank(nic)) {
            // Without a NIC there is no prosumer record to address, which can only happen
            // for a staff account - and the form is hidden for those.
            toastLong(getString(R.string.error_generic));
            return;
        }

        JSONObject body = new JSONObject();
        try {
            body.put("fullName", text(etFullName));
            body.put("email", text(etEmail));
            body.put("phone", text(etPhone));
            body.put("address", text(etAddress));
        } catch (JSONException ex) {
            toastLong(getString(R.string.error_generic));
            return;
        }

        setSavingProfile(true);

        // The API route is PUT /prosumers/{nic}, allowed for Backoffice and for the
        // owner of the record; rule R11 means a prosumer can only ever reach their own.
        api.put(Constants.EP_PROSUMERS + "/" + nic, body, token(), new ApiClient.Callback() {

            /** Receives the server reply on the main thread. */
            @Override
            public void onResult(ApiResult result) {
                // Views are touched directly because the callback already runs on the UI thread.
                setSavingProfile(false);

                if (!result.isSuccess()) {
                    handleProfileSaveFailure(result);
                    return;
                }

                UserModel updated = result.getDataObject() == null
                        ? profileWithLocalEdits()
                        : UserModel.fromJson(result.getDataObject());

                // The cached SQLite session is updated so the dashboard header, the
                // greeting and the next cold start all show the new details.
                session.updateProfile(updated);

                bindProfile(updated);
                toast(result.getMessage().isEmpty()
                        ? "Profile updated."
                        : result.getMessage());
            }
        });
    }

    /** Reports a refused profile edit, attaching field messages where the API sent them. */
    private void handleProfileSaveFailure(ApiResult result) {
        // The API can return per-field messages under "errors"; those are far more useful
        // beside the offending field than in a toast.
        JSONObject fieldErrors = result.getFieldErrors();
        if (fieldErrors != null) {
            applyFieldError(fieldErrors, "fullName", tilFullName);
            applyFieldError(fieldErrors, "email", tilEmail);
            applyFieldError(fieldErrors, "phone", tilPhone);
            applyFieldError(fieldErrors, "address", tilAddress);
        }

        // Everything else falls through to the shared handler, which also copes with an
        // expired session and shows the server's own end user wording.
        showApiError(result);
    }

    /** Copies one server field message onto its text input, when present. */
    private void applyFieldError(JSONObject fieldErrors, String field, TextInputLayout layout) {
        // The value is an array of messages; the first one is enough for a phone form.
        if (fieldErrors == null || layout == null || !fieldErrors.has(field)) {
            return;
        }
        JSONArray messages = fieldErrors.optJSONArray(field);
        if (messages != null && messages.length() > 0) {
            layout.setError(messages.optString(0, ""));
        }
    }

    /**
     * Runs the four editable fields through ValidationUtils.
     *
     * These checks are IMMEDIATE FEEDBACK ONLY. The Web API re-validates every
     * field on PUT /prosumers/{nic} and remains the single authority; a value
     * this form accepts can still be rejected by the server, and that wins.
     */
    private boolean validateProfileForm() {
        // Errors are cleared first so a corrected field stops showing a stale message.
        tilFullName.setError(null);
        tilEmail.setError(null);
        tilPhone.setError(null);
        tilAddress.setError(null);

        View firstInvalid = null;

        String nameError = ValidationUtils.validateFullName(text(etFullName));
        if (nameError != null) {
            tilFullName.setError(nameError);
            firstInvalid = etFullName;
        }

        String emailError = ValidationUtils.validateEmail(text(etEmail));
        if (emailError != null) {
            tilEmail.setError(emailError);
            firstInvalid = firstInvalid == null ? etEmail : firstInvalid;
        }

        String phoneError = ValidationUtils.validatePhone(text(etPhone));
        if (phoneError != null) {
            tilPhone.setError(phoneError);
            firstInvalid = firstInvalid == null ? etPhone : firstInvalid;
        }

        String addressError = ValidationUtils.validateAddress(text(etAddress));
        if (addressError != null) {
            tilAddress.setError(addressError);
            firstInvalid = firstInvalid == null ? etAddress : firstInvalid;
        }

        if (firstInvalid != null) {
            // Focusing the topmost problem scrolls the form to it inside the ScrollView.
            firstInvalid.requestFocus();
            return false;
        }
        return true;
    }

    /** Builds a user model from the loaded profile plus whatever is in the form. */
    private UserModel profileWithLocalEdits() {
        // Used only when the API confirmed the save but returned no payload of its own.
        UserModel merged = new UserModel();
        if (profile != null) {
            merged.setId(profile.getId());
            merged.setNic(profile.getNic());
            merged.setUsername(profile.getUsername());
            merged.setRole(profile.getRole());
            merged.setStatus(profile.getStatus());
            merged.setAssignedStationId(profile.getAssignedStationId());
            merged.setAssignedStationCode(profile.getAssignedStationCode());
            merged.setDeactivationRequested(profile.isDeactivationRequested());
        }
        merged.setFullName(text(etFullName));
        merged.setEmail(text(etEmail));
        merged.setPhone(text(etPhone));
        merged.setAddress(text(etAddress));
        return merged;
    }

    /** Switches the edit card between its idle and its in-flight appearance. */
    private void setSavingProfile(boolean saving) {
        // Disabling the button prevents the same edit being submitted twice.
        btnSaveProfile.setEnabled(!saving);
        progressSaveProfile.setVisibility(saving ? View.VISIBLE : View.GONE);
    }

    // =====================================================================
    // Change password
    // =====================================================================

    /** Validates the password form and posts it to POST /auth/change-password. */
    private void changePassword() {
        // The confirmation field never leaves the phone: the server only receives the pair.
        if (!validatePasswordForm()) {
            return;
        }

        JSONObject body = new JSONObject();
        try {
            body.put("currentPassword", rawText(etCurrentPassword));
            body.put("newPassword", rawText(etNewPassword));
        } catch (JSONException ex) {
            toastLong(getString(R.string.error_generic));
            return;
        }

        setChangingPassword(true);

        api.post(Constants.EP_CHANGE_PASSWORD, body, token(), new ApiClient.Callback() {

            /** Receives the server reply on the main thread. */
            @Override
            public void onResult(ApiResult result) {
                // Whether the current password was right is the SERVER's verdict, never the
                // app's - the phone holds no password hash and cannot check it.
                setChangingPassword(false);

                if (!result.isSuccess()) {
                    showApiError(result);
                    return;
                }

                clearPasswordFields();
                toast(result.getMessage().isEmpty()
                        ? getString(R.string.action_change_password)
                        : result.getMessage());
            }
        });
    }

    /**
     * Checks the three password fields. Only the length and the confirmation
     * match are checked here; whether the current password is correct is decided
     * by the Web API.
     */
    private boolean validatePasswordForm() {
        // Errors are cleared first so a corrected field stops showing a stale message.
        tilCurrentPassword.setError(null);
        tilNewPassword.setError(null);
        tilConfirmNewPassword.setError(null);

        View firstInvalid = null;

        if (ValidationUtils.isBlank(rawText(etCurrentPassword))) {
            tilCurrentPassword.setError("Enter your current password.");
            firstInvalid = etCurrentPassword;
        }

        String newError = ValidationUtils.validatePassword(rawText(etNewPassword));
        if (newError != null) {
            tilNewPassword.setError(newError);
            firstInvalid = firstInvalid == null ? etNewPassword : firstInvalid;
        }

        String matchError = ValidationUtils.validatePasswordMatch(
                rawText(etNewPassword), rawText(etConfirmNewPassword));
        if (matchError != null) {
            tilConfirmNewPassword.setError(matchError);
            firstInvalid = firstInvalid == null ? etConfirmNewPassword : firstInvalid;
        }

        if (firstInvalid != null) {
            // Focusing the topmost problem scrolls the form to it inside the ScrollView.
            firstInvalid.requestFocus();
            return false;
        }
        return true;
    }

    /** Empties the three password fields after a successful change. */
    private void clearPasswordFields() {
        // Leaving a password on screen after the change would be careless.
        etCurrentPassword.setText("");
        etNewPassword.setText("");
        etConfirmNewPassword.setText("");
        etCurrentPassword.clearFocus();
        etNewPassword.clearFocus();
        etConfirmNewPassword.clearFocus();
    }

    /** Switches the password card between its idle and its in-flight appearance. */
    private void setChangingPassword(boolean busy) {
        // Disabling the button prevents a double submission.
        btnChangePassword.setEnabled(!busy);
        progressChangePassword.setVisibility(busy ? View.VISIBLE : View.GONE);
    }

    // =====================================================================
    // Deactivation request
    // =====================================================================

    /**
     * Asks for an optional reason and then raises the request.
     *
     * IMPORTANT: this only RAISES a request. The account stays Active until a
     * Backoffice officer acts on it - rule R5 in the Web API restricts the
     * activate and deactivate operations to that role, and PATCH
     * /prosumers/{nic}/deactivate is not reachable from this application at all.
     */
    private void promptDeactivation() {
        // dialog_reason_input is a shared layout, so any screen that needs a reason reuses it.
        View dialogView = LayoutInflater.from(this)
                .inflate(R.layout.dialog_reason_input, null, false);
        final TextInputEditText reasonInput = dialogView.findViewById(R.id.reason_input);

        new AlertDialog.Builder(this)
                .setTitle(R.string.action_request_deactivation)
                .setMessage(R.string.confirm_request_deactivation)
                .setView(dialogView)
                .setPositiveButton(R.string.action_confirm, new DialogInterface.OnClickListener() {

                    /** Sends the typed reason to the API. */
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        // The reason is optional on the endpoint, so an empty box is allowed.
                        submitDeactivationRequest(reasonText(reasonInput));
                    }
                })
                .setNegativeButton(R.string.action_cancel, new DialogInterface.OnClickListener() {

                    /** Closes the dialog without contacting the API. */
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        // Nothing was sent, so there is nothing to undo.
                        dialog.dismiss();
                    }
                })
                .show();
    }

    /** Posts the deactivation request to POST /prosumers/{nic}/request-deactivation. */
    private void submitDeactivationRequest(String reason) {
        // Only the owner of the record may call this route, and the JWT identifies them.
        String nic = currentNic();
        if (ValidationUtils.isBlank(nic)) {
            toastLong(getString(R.string.error_generic));
            return;
        }

        JSONObject body = new JSONObject();
        try {
            body.put("reason", reason);
        } catch (JSONException ex) {
            toastLong(getString(R.string.error_generic));
            return;
        }

        btnRequestDeactivation.setEnabled(false);

        api.post(Constants.EP_PROSUMERS + "/" + nic + "/request-deactivation", body, token(),
                new ApiClient.Callback() {

                    /** Receives the server reply on the main thread. */
                    @Override
                    public void onResult(ApiResult result) {
                        // The API sets deactivationRequested; the app never changes a status itself.
                        if (!result.isSuccess()) {
                            btnRequestDeactivation.setEnabled(true);
                            showApiError(result);
                            return;
                        }

                        // The flag now lives on the server record too, so the local copy is
                        // updated to match and the banner survives a re-bind of this screen.
                        if (profile != null) {
                            profile.setDeactivationRequested(true);
                        }
                        panelDeactivationPending.setVisibility(View.VISIBLE);

                        // The account remains usable until a Backoffice officer acts (rule R5).
                        toast(result.getMessage().isEmpty()
                                ? getString(R.string.deactivation_pending_notice)
                                : result.getMessage());
                    }
                });
    }

    // =====================================================================
    // Small helpers
    // =====================================================================

    /** NIC of the account currently on screen, preferring the freshly loaded profile. */
    private String currentNic() {
        // Falls back to the SQLite session when the profile call has not completed.
        if (profile != null && !ValidationUtils.isBlank(profile.getNic())) {
            return ValidationUtils.normaliseNic(profile.getNic());
        }
        String cached = session.getNic();
        return cached == null ? "" : ValidationUtils.normaliseNic(cached);
    }

    /** Recolours a status pill in place, reusing the single bg_status_chip drawable. */
    private void tintChip(TextView chip, int fillColour, int textColour) {
        // mutate() gives this chip its own state so the other chip is not recoloured with it.
        chip.setTextColor(ContextCompat.getColor(this, textColour));
        Drawable pill = chip.getBackground();
        if (pill instanceof GradientDrawable) {
            GradientDrawable shape = (GradientDrawable) pill.mutate();
            shape.setColor(ContextCompat.getColor(this, fillColour));
        }
    }

    /** Maps an account status onto its pill fill colour. */
    private int statusFillColour(String status) {
        // Mirrors the badge palette used by the web client so both look like one product.
        if (Constants.STATUS_ACTIVE.equals(status)) {
            return R.color.status_completed_bg;
        }
        if (Constants.STATUS_DEACTIVATED.equals(status)) {
            return R.color.status_rejected_bg;
        }
        return R.color.status_pending_bg;
    }

    /** Maps an account status onto its pill text colour. */
    private int statusTextColour(String status) {
        // Each fill has a matching dark tone that keeps the label readable.
        if (Constants.STATUS_ACTIVE.equals(status)) {
            return R.color.status_completed_text;
        }
        if (Constants.STATUS_DEACTIVATED.equals(status)) {
            return R.color.status_rejected_text;
        }
        return R.color.status_pending_text;
    }

    /** Returns the value, or a dash when the API sent nothing for that field. */
    private String valueOrDash(String value) {
        // A staff account legitimately has no NIC, and an operator may have no address.
        return ValidationUtils.isBlank(value) ? "-" : value.trim();
    }

    /** Reads an edit text as a trimmed string, tolerating a null editable. */
    private String text(TextInputEditText field) {
        // Trimming is safe for the profile fields and avoids accidental leading spaces.
        if (field == null || field.getText() == null) {
            return "";
        }
        return field.getText().toString().trim();
    }

    /** Reads an edit text without trimming, used for the password fields. */
    private String rawText(TextInputEditText field) {
        // A password may legitimately begin or end with a space, so it is never trimmed.
        if (field == null || field.getText() == null) {
            return "";
        }
        return field.getText().toString();
    }

    /** Reads the reason dialog field as a trimmed string. */
    private String reasonText(TextInputEditText field) {
        // The reason is optional, so an empty string is a perfectly valid value to send.
        if (field == null || field.getText() == null) {
            return "";
        }
        return field.getText().toString().trim();
    }
}
