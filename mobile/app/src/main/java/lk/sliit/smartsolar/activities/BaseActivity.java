/* =============================================================================
 * File        : BaseActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Activities / Base
 * Description : Shares API, session, toolbar and error handling across
 *               screens.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-19
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.content.Intent;
import android.graphics.drawable.Drawable;
import android.view.View;
import android.widget.Toast;

import androidx.annotation.Nullable;
import androidx.appcompat.app.AlertDialog;
import androidx.appcompat.app.AppCompatActivity;
import androidx.appcompat.widget.Toolbar;
import androidx.core.content.ContextCompat;

import com.google.android.material.snackbar.Snackbar;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.api.ApiClient;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.db.LocalDataStore;
import lk.sliit.smartsolar.db.SessionManager;
import lk.sliit.smartsolar.util.Constants;

/** Shared plumbing for every screen in the application. */
public abstract class BaseActivity extends AppCompatActivity {

    /** Talks to the central Web API. Created once per Activity. */
    protected ApiClient api;

    /** Reads and writes the signed in session in local SQLite. */
    protected SessionManager session;

    /** Reads and writes the local SQLite caches. */
    protected LocalDataStore localStore;

    /** Builds the shared helpers before the subclass runs its own setup. */
    @Override
    protected void onCreate(@Nullable android.os.Bundle savedInstanceState) {
        // Every subclass calls super.onCreate first, so these are always ready.
        super.onCreate(savedInstanceState);
        api = new ApiClient(this);
        session = new SessionManager(this);
        localStore = new LocalDataStore(this);
    }

    /** Returns the bearer token for the current session, or null when signed out. */
    protected String token() {
        // Passed to every authenticated ApiClient call.
        return session.getToken();
    }

    /**
     * Wires a toolbar as the action bar and optionally shows a back arrow that
     * finishes the Activity.
     */
    protected void setupToolbar(int toolbarId, String title, boolean showBack) {
        // Guarded so a layout without a toolbar does not crash the screen.
        Toolbar toolbar = findViewById(toolbarId);
        if (toolbar == null) {
            return;
        }

        setSupportActionBar(toolbar);
        if (getSupportActionBar() != null) {
            getSupportActionBar().setTitle(title);
            getSupportActionBar().setDisplayHomeAsUpEnabled(showBack);
        }

        if (showBack) {
            toolbar.setNavigationIcon(R.drawable.ic_arrow_back);
            // The bar is white, so the arrow is tinted ink. mutate() keeps the tint off the
            // shared drawable state that every other screen's back arrow draws from.
            Drawable arrow = toolbar.getNavigationIcon();
            if (arrow != null) {
                arrow = arrow.mutate();
                arrow.setTint(ContextCompat.getColor(this, R.color.slate_900));
                toolbar.setNavigationIcon(arrow);
            }
            toolbar.setNavigationOnClickListener(v -> finish());
        }
    }

    /** Shows a short toast. */
    protected void toast(String message) {
        // Used for brief confirmations that do not need an action.
        if (message != null && !message.trim().isEmpty()) {
            Toast.makeText(this, message, Toast.LENGTH_SHORT).show();
        }
    }

    /** Shows a longer toast, used for error text the user needs time to read. */
    protected void toastLong(String message) {
        // Rule violation messages from the API are deliberately wordy, so they get the longer toast.
        if (message != null && !message.trim().isEmpty()) {
            Toast.makeText(this, message, Toast.LENGTH_LONG).show();
        }
    }

    /** Shows a snackbar anchored to a view, optionally with a retry action. */
    protected void snack(View anchor, String message, @Nullable Runnable retry) {
        // Preferred over a toast when the user can do something about the problem.
        if (anchor == null || message == null) {
            return;
        }
        Snackbar bar = Snackbar.make(anchor, message, Snackbar.LENGTH_LONG);
        if (retry != null) {
            bar.setAction(R.string.action_retry, v -> retry.run());
        }
        bar.show();
    }

    /** Shows a simple confirmation dialog and runs the action when confirmed. */
    protected void confirm(String title, String message, String confirmLabel, Runnable onConfirm) {
        // Used before any destructive action such as cancelling a booking.
        new AlertDialog.Builder(this)
                .setTitle(title)
                .setMessage(message)
                .setPositiveButton(confirmLabel, (dialog, which) -> onConfirm.run())
                .setNegativeButton(R.string.action_cancel, (dialog, which) -> dialog.dismiss())
                .show();
    }

    /** Shows an informational dialog with a single dismiss button. */
    protected void info(String title, String message) {
        // Used to explain a rule the server enforced, such as the 12 hour notice period.
        new AlertDialog.Builder(this)
                .setTitle(title)
                .setMessage(message)
                .setPositiveButton(R.string.action_close, (dialog, which) -> dialog.dismiss())
                .show();
    }

    /**
     * Single place that decides what to do with a failed API call. Returns true
     * when the failure was handled here (an expired session), so the caller can
     * stop and not also show its own message.
     */
    protected boolean handleAuthFailure(ApiResult result) {
        // A 401 means the stored token is no longer valid, so the session is cleared
        // and the user is returned to the login screen rather than left on a dead page.
        if (result != null && result.isUnauthorized()) {
            session.logout();
            toastLong(getString(R.string.error_session_expired));
            Intent intent = new Intent(this, LoginActivity.class);
            intent.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
            startActivity(intent);
            finish();
            return true;
        }
        return false;
    }

    /**
     * Convenience for the common failure path: handle an expired session, and
     * otherwise show the server's own message, which is already written for an
     * end user (for example the wording of the 12 hour notice rule).
     */
    protected void showApiError(ApiResult result) {
        // The API's message is preferred over anything the app could invent.
        if (handleAuthFailure(result)) {
            return;
        }
        String message = result == null || result.getMessage().isEmpty()
                ? getString(R.string.error_generic)
                : result.getMessage();
        toastLong(message);
    }

    /** Signs the user out, clears local data and returns to the login screen. */
    protected void performLogout() {
        // Clearing the SQLite caches too means the next user never sees stale rows.
        confirm(getString(R.string.action_logout),
                "Sign out of Smart Solar?",
                getString(R.string.action_logout),
                () -> {
                    session.logout();
                    Intent intent = new Intent(this, LoginActivity.class);
                    intent.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
                    startActivity(intent);
                    finish();
                });
    }

    /** Routes to the home screen that matches the signed in user's role. */
    protected void goToRoleHome() {
        // The assignment marks role based home routing, so it lives in one place.
        Intent intent;
        if (Constants.ROLE_GRID_OPERATOR.equals(session.getRole())) {
            intent = new Intent(this, OperatorHomeActivity.class);
        } else {
            intent = new Intent(this, ProsumerHomeActivity.class);
        }
        intent.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        startActivity(intent);
        finish();
    }

    /** Shows one view and hides the other two, for the loading / content / empty pattern. */
    protected void showOnly(@Nullable View toShow, View... toHide) {
        // Keeps the three state handling identical on every list screen.
        for (View view : toHide) {
            if (view != null) {
                view.setVisibility(View.GONE);
            }
        }
        if (toShow != null) {
            toShow.setVisibility(View.VISIBLE);
        }
    }
}
