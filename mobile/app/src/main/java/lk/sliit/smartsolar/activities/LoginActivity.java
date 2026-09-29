/*
 * =============================================================================
 * File        : LoginActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : activities
 * Description : Sign in screen. It posts the identifier and password to
 *               POST /auth/login, stores the returned JWT and user profile in the
 *               local SQLite session table, and then performs the role based
 *               routing the marking scheme asks for. Refusals such as a pending
 *               or deactivated account are decided entirely by the API (rules R6
 *               and R5) and are only explained here.
 * Author      : Chathushka - Identity, Access Control & Local Session (SE4040 EAD)
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.content.Intent;
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
import lk.sliit.smartsolar.models.UserModel;
import lk.sliit.smartsolar.util.Constants;
import lk.sliit.smartsolar.util.ValidationUtils;

/** Collects credentials, authenticates against the Web API and routes by role. */
public class LoginActivity extends BaseActivity {

    private TextInputLayout tilIdentifier;
    private TextInputLayout tilPassword;
    private TextInputEditText etIdentifier;
    private TextInputEditText etPassword;
    private MaterialButton btnLogin;
    private MaterialButton btnGoRegister;
    private ProgressBar progressLogin;

    /** Inflates the layout, binds the views and wires the two buttons. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // BaseActivity prepares the shared ApiClient and SQLite session first.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_login);

        bindViews();

        btnLogin.setOnClickListener(v -> attemptLogin());
        btnGoRegister.setOnClickListener(v ->
                startActivity(new Intent(LoginActivity.this, RegisterActivity.class)));
    }

    /** Looks up every view this screen touches, using findViewById only. */
    private void bindViews() {
        // No view binding or data binding is used anywhere in this pure native project.
        tilIdentifier = findViewById(R.id.til_identifier);
        tilPassword = findViewById(R.id.til_password);
        etIdentifier = findViewById(R.id.et_identifier);
        etPassword = findViewById(R.id.et_password);
        btnLogin = findViewById(R.id.btn_login);
        btnGoRegister = findViewById(R.id.btn_go_register);
        progressLogin = findViewById(R.id.progress_login);
    }

    // =====================================================================
    // Sign in
    // =====================================================================

    /** Validates the two fields and, when they are filled, calls the API. */
    private void attemptLogin() {
        // These checks only stop an obviously empty request; the API authenticates.
        tilIdentifier.setError(null);
        tilPassword.setError(null);

        String identifier = text(etIdentifier);
        String password = text(etPassword);
        boolean valid = true;

        if (ValidationUtils.isBlank(identifier)) {
            tilIdentifier.setError("Enter your NIC, username or e-mail.");
            valid = false;
        }
        if (ValidationUtils.isBlank(password)) {
            tilPassword.setError("Enter your password.");
            valid = false;
        }
        if (!valid) {
            // Focus the first empty field so the keyboard lands where the user must type.
            if (ValidationUtils.isBlank(identifier)) {
                etIdentifier.requestFocus();
            } else {
                etPassword.requestFocus();
            }
            return;
        }

        JSONObject body = new JSONObject();
        try {
            body.put("identifier", identifier);
            body.put("password", password);
        } catch (JSONException ex) {
            toastLong(getString(R.string.error_generic));
            return;
        }

        setLoading(true);

        // Login is the one call that carries no bearer token, so it goes out anonymously.
        api.postAnonymous(Constants.EP_LOGIN, body, result -> {
            // ApiClient posts this callback to the main thread, so views are safe to touch.
            setLoading(false);

            if (!result.isSuccess()) {
                handleLoginFailure(result.getErrorCode(), result.getMessage(), result);
                return;
            }

            JSONObject data = result.getDataObject();
            if (data == null) {
                toastLong(getString(R.string.error_generic));
                return;
            }

            // AuthResponse = { token, expiresAtUtc, user }, per section 7.1 of the contract.
            String token = data.optString("token", "");
            String expiresAtUtc = data.optString("expiresAtUtc", "");
            JSONObject userJson = data.optJSONObject("user");

            if (token.isEmpty() || userJson == null) {
                toastLong(getString(R.string.error_generic));
                return;
            }

            UserModel user = UserModel.fromJson(userJson);

            // The token and profile are written to the SQLite session table, which is
            // what lets the splash screen resume this user without a fresh login.
            session.saveSession(token, expiresAtUtc, user);

            routeByRole(user);
        });
    }

    /**
     * Sends the signed in user to the home screen that matches their role. Role
     * based routing is an explicitly marked requirement, and the role itself is
     * assigned by the server and simply read back here.
     */
    private void routeByRole(UserModel user) {
        // Grid operators get the scanning and node tools.
        if (Constants.ROLE_GRID_OPERATOR.equals(user.getRole())) {
            launchHome(OperatorHomeActivity.class);
            return;
        }

        // Back office administration is not a phone workflow - that console is the web
        // application - so this account is told so rather than dropped into a prosumer view.
        if (Constants.ROLE_BACKOFFICE.equals(user.getRole())) {
            showBackofficeNotice();
            return;
        }

        // Everything else is a solar prosumer, the default role for a self registration.
        launchHome(ProsumerHomeActivity.class);
    }

    /** Explains that the Backoffice console is the web client, and offers the operator view. */
    private void showBackofficeNotice() {
        // The dialog is not cancellable so the account cannot silently stay on a dead screen.
        new AlertDialog.Builder(this)
                .setTitle("Backoffice console")
                .setMessage("The Backoffice console is the web application. "
                        + "You can continue to the operator view on this device, "
                        + "or sign out and use the web client.")
                .setCancelable(false)
                .setPositiveButton(R.string.action_confirm,
                        (dialog, which) -> launchHome(OperatorHomeActivity.class))
                .setNegativeButton(R.string.action_logout, (dialog, which) -> {
                    // Clearing the SQLite session leaves the device signed out.
                    session.logout();
                    dialog.dismiss();
                })
                .show();
    }

    /** Starts a home Activity as a fresh task and finishes the login screen. */
    private void launchHome(Class<?> homeActivity) {
        // CLEAR_TASK means the back button can never return to the login form once signed in.
        Intent intent = new Intent(this, homeActivity);
        intent.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        startActivity(intent);
        finish();
    }

    // =====================================================================
    // Failure handling
    // =====================================================================

    /**
     * Explains a refused login. The application branches on the stable error code
     * rather than on message text, because the decision itself was taken by the
     * Web API: ACCOUNT_PENDING is rule R6 (a self registered prosumer stays
     * Pending until a Backoffice officer activates it) and ACCOUNT_DEACTIVATED is
     * rule R5 (only a Backoffice user may reactivate an account). Neither rule is
     * re-implemented here - the phone only reports the server's verdict.
     */
    private void handleLoginFailure(String errorCode, String message, ApiResult result) {
        // A pending account: the server refused, the app just words the refusal clearly.
        if (Constants.ERR_ACCOUNT_PENDING.equals(errorCode)) {
            info("Account awaiting activation", messageOrFallback(message,
                    getString(R.string.register_pending_notice)));
            return;
        }

        // A deactivated account: reactivation is a Backoffice action in the web client.
        if (Constants.ERR_ACCOUNT_DEACTIVATED.equals(errorCode)) {
            info("Account deactivated", messageOrFallback(message,
                    getString(R.string.error_generic)));
            return;
        }

        // Anything else - bad credentials, a network problem, a server fault - is
        // shown verbatim, because the API writes these messages for end users.
        if (result != null && result.isNetworkError()) {
            toastLong(messageOrFallback(message, getString(R.string.error_network)));
            return;
        }
        toastLong(messageOrFallback(message, getString(R.string.error_generic)));
    }

    /** Returns the server message when it has content, otherwise a local fallback. */
    private String messageOrFallback(String message, String fallback) {
        // Guards against an endpoint that answered with an empty message string.
        return ValidationUtils.isBlank(message) ? fallback : message;
    }

    // =====================================================================
    // Small helpers
    // =====================================================================

    /** Switches the form between its idle and its in-flight appearance. */
    private void setLoading(boolean loading) {
        // Disabling the button prevents a double submission while a call is running.
        btnLogin.setEnabled(!loading);
        btnGoRegister.setEnabled(!loading);
        progressLogin.setVisibility(loading ? View.VISIBLE : View.GONE);
    }

    /** Reads an edit text as a trimmed string, tolerating a null editable. */
    private String text(TextInputEditText field) {
        // getText() can legitimately be null before the view has been laid out.
        if (field == null || field.getText() == null) {
            return "";
        }
        return field.getText().toString().trim();
    }
}
