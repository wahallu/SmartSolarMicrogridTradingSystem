/*
 * =============================================================================
 * File        : RegisterActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : activities
 * Description : Self registration for a solar prosumer, satisfying the marked
 *               "EV owner / prosumer can create their own account with the NIC as
 *               the primary key" requirement. It collects the profile, gives
 *               immediate field level feedback and posts to POST /auth/register.
 *               The account is created Pending by the server under rule R6 and a
 *               duplicate NIC is refused by the server under rule R7.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.os.Bundle;
import android.view.View;
import android.widget.ProgressBar;

import androidx.annotation.Nullable;
import androidx.appcompat.app.AlertDialog;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.textfield.TextInputEditText;
import com.google.android.material.textfield.TextInputLayout;

import org.json.JSONException;
import org.json.JSONObject;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.util.Constants;
import lk.sliit.smartsolar.util.ValidationUtils;

/** Collects a new prosumer's details and submits them to the Web API. */
public class RegisterActivity extends BaseActivity {

    private TextInputLayout tilNic;
    private TextInputLayout tilFullName;
    private TextInputLayout tilEmail;
    private TextInputLayout tilPhone;
    private TextInputLayout tilAddress;
    private TextInputLayout tilPassword;
    private TextInputLayout tilConfirmPassword;

    private TextInputEditText etNic;
    private TextInputEditText etFullName;
    private TextInputEditText etEmail;
    private TextInputEditText etPhone;
    private TextInputEditText etAddress;
    private TextInputEditText etPassword;
    private TextInputEditText etConfirmPassword;

    private MaterialButton btnRegister;
    private MaterialButton btnGoLogin;
    private ProgressBar progressRegister;

    /** Inflates the form, wires the toolbar and connects the two buttons. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // BaseActivity prepares the shared ApiClient and SQLite helpers first.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_register);

        setupToolbar(R.id.toolbar_register, getString(R.string.register_title), true);
        bindViews();

        btnRegister.setOnClickListener(v -> attemptRegister());
        btnGoLogin.setOnClickListener(v -> finish());
    }

    /** Looks up every view on the form with findViewById only. */
    private void bindViews() {
        // No view binding or data binding: the project must stay pure native Android.
        tilNic = findViewById(R.id.til_nic);
        tilFullName = findViewById(R.id.til_full_name);
        tilEmail = findViewById(R.id.til_email);
        tilPhone = findViewById(R.id.til_phone);
        tilAddress = findViewById(R.id.til_address);
        tilPassword = findViewById(R.id.til_password);
        tilConfirmPassword = findViewById(R.id.til_confirm_password);

        etNic = findViewById(R.id.et_nic);
        etFullName = findViewById(R.id.et_full_name);
        etEmail = findViewById(R.id.et_email);
        etPhone = findViewById(R.id.et_phone);
        etAddress = findViewById(R.id.et_address);
        etPassword = findViewById(R.id.et_password);
        etConfirmPassword = findViewById(R.id.et_confirm_password);

        btnRegister = findViewById(R.id.btn_register);
        btnGoLogin = findViewById(R.id.btn_go_login);
        progressRegister = findViewById(R.id.progress_register);
    }

    // =====================================================================
    // Submission
    // =====================================================================

    /** Validates the form and, when it passes, posts the registration request. */
    private void attemptRegister() {
        // Nothing is sent until the local checks pass, but the API re-validates regardless.
        if (!validateForm()) {
            return;
        }

        JSONObject body = new JSONObject();
        try {
            // The NIC is normalised (trimmed and upper cased) so it matches how the
            // server stores it and a needless uniqueness mismatch cannot occur.
            body.put("nic", ValidationUtils.normaliseNic(text(etNic)));
            body.put("fullName", text(etFullName));
            body.put("email", text(etEmail));
            body.put("phone", text(etPhone));
            body.put("address", text(etAddress));
            body.put("password", rawText(etPassword));
        } catch (JSONException ex) {
            toastLong(getString(R.string.error_generic));
            return;
        }

        setLoading(true);

        // Registration is anonymous: there is no session yet, so no bearer token is sent.
        api.postAnonymous(Constants.EP_REGISTER, body, result -> {
            // ApiClient delivers this on the main thread, so views may be touched here.
            setLoading(false);

            if (result.isSuccess()) {
                showSubmittedDialog();
                return;
            }

            handleRegisterFailure(result.getErrorCode(), result.getMessage(), result);
        });
    }

    /**
     * Confirms that the request was accepted. The account is NOT usable yet: rule
     * R6 in the API creates a self registered prosumer with status Pending, and
     * only a Backoffice officer may activate it. That decision belongs to the
     * server, so this screen only reports it.
     */
    private void showSubmittedDialog() {
        // Dismissing the dialog returns to the login screen, which is the parent Activity.
        AlertDialog dialog = new AlertDialog.Builder(this)
                .setTitle("Registration submitted")
                .setMessage(getString(R.string.register_pending_notice))
                .setCancelable(false)
                .setPositiveButton(R.string.action_close, (d, which) -> d.dismiss())
                .create();

        dialog.setOnDismissListener(d -> finish());
        dialog.show();
    }

    /**
     * Explains a refused registration. A duplicate NIC is rule R7 in the API and
     * arrives as HTTP 409 with the NIC_ALREADY_EXISTS code, so the message is
     * attached to the NIC field rather than shown as a general error.
     */
    private void handleRegisterFailure(String errorCode, String message, ApiResult result) {
        // The uniqueness of the NIC is decided by the server, never by this app.
        if (Constants.ERR_NIC_EXISTS.equals(errorCode)) {
            String text = ValidationUtils.isBlank(message)
                    ? "This NIC is already registered."
                    : message;
            tilNic.setError(text);
            etNic.requestFocus();
            return;
        }

        // Everything else falls through to the shared handler, which also copes with
        // an expired session and shows the server's own end user wording.
        showApiError(result);
    }

    // =====================================================================
    // Validation
    // =====================================================================

    /**
     * Runs every field through ValidationUtils and marks the ones that fail.
     *
     * These checks are IMMEDIATE FEEDBACK ONLY. The Web API re-validates every
     * field on POST /auth/register and remains the single authority; a value this
     * form accepts can still be rejected by the server, and that rejection wins.
     */
    private boolean validateForm() {
        // Errors are cleared first so a corrected field stops showing a stale message.
        clearErrors();

        View firstInvalid = null;

        String nicError = ValidationUtils.validateNic(text(etNic));
        if (nicError != null) {
            tilNic.setError(nicError);
            firstInvalid = etNic;
        }

        String nameError = ValidationUtils.validateFullName(text(etFullName));
        if (nameError != null) {
            tilFullName.setError(nameError);
            firstInvalid = firstInvalid == null ? etFullName : firstInvalid;
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

        String passwordError = ValidationUtils.validatePassword(rawText(etPassword));
        if (passwordError != null) {
            tilPassword.setError(passwordError);
            firstInvalid = firstInvalid == null ? etPassword : firstInvalid;
        }

        String matchError = ValidationUtils.validatePasswordMatch(
                rawText(etPassword), rawText(etConfirmPassword));
        if (matchError != null) {
            tilConfirmPassword.setError(matchError);
            firstInvalid = firstInvalid == null ? etConfirmPassword : firstInvalid;
        }

        if (firstInvalid != null) {
            // Focusing the topmost problem scrolls the form to it inside the ScrollView.
            firstInvalid.requestFocus();
            return false;
        }
        return true;
    }

    /** Removes any error message currently shown on the seven fields. */
    private void clearErrors() {
        // Called at the start of every validation pass and before a retry.
        tilNic.setError(null);
        tilFullName.setError(null);
        tilEmail.setError(null);
        tilPhone.setError(null);
        tilAddress.setError(null);
        tilPassword.setError(null);
        tilConfirmPassword.setError(null);
    }

    // =====================================================================
    // Small helpers
    // =====================================================================

    /** Switches the form between its idle and its in-flight appearance. */
    private void setLoading(boolean loading) {
        // Disabling the buttons prevents a duplicate account being requested twice.
        btnRegister.setEnabled(!loading);
        btnGoLogin.setEnabled(!loading);
        progressRegister.setVisibility(loading ? View.VISIBLE : View.GONE);
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
}
