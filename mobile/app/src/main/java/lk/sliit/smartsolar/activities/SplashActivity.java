/*
 * =============================================================================
 * File        : SplashActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : activities
 * Description : Launcher screen of the Android client. It shows the brand mark
 *               for a short, fixed moment and then decides where the user goes:
 *               straight to their role home screen when a session already exists
 *               in the local SQLite database, or to the login form when it does
 *               not. This is the entry point for the marked "session persistence
 *               in SQLite" and "role based routing" requirements.
 * Author      : Chathushka - Identity, Access Control & Local Session (SE4040 EAD)
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;

import androidx.annotation.Nullable;

import lk.sliit.smartsolar.R;

/** Decides between the login form and a role home screen on application start. */
public class SplashActivity extends BaseActivity {

    /** How long the brand mark stays on screen before routing, in milliseconds. */
    private static final long SPLASH_DELAY_MS = 1200L;

    /** Posts the routing decision after the delay; held so it can be cancelled. */
    private final Handler splashHandler = new Handler(Looper.getMainLooper());

    /** The delayed routing task, kept as a field so onDestroy can remove it. */
    private Runnable routeTask;

    /** Inflates the splash layout and schedules the routing decision. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // BaseActivity builds the ApiClient, the SessionManager and the local cache first.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_splash);

        routeTask = this::routeToNextScreen;
        splashHandler.postDelayed(routeTask, SPLASH_DELAY_MS);
    }

    /**
     * Sends the user to the correct next screen.
     *
     * The session (JWT plus the cached user profile) is read back out of the local
     * SQLite "session" table by SessionManager. That local read is exactly what
     * lets the application resume without asking the user to sign in again after
     * the process has been killed, which is the persistence the assignment marks.
     */
    private void routeToNextScreen() {
        // isLoggedIn() is a SQLite lookup, not a network call, so this stays instant.
        if (session.isLoggedIn()) {
            // goToRoleHome lives in BaseActivity and is the single place that maps a
            // role name onto a home Activity. It clears the task and finishes this screen.
            goToRoleHome();
            return;
        }

        Intent intent = new Intent(this, LoginActivity.class);
        intent.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        startActivity(intent);
        finish();
    }

    /** Cancels the pending routing task so it never runs against a dead Activity. */
    @Override
    protected void onDestroy() {
        // Without this, rotating or backing out during the delay would leak the callback.
        if (routeTask != null) {
            splashHandler.removeCallbacks(routeTask);
        }
        super.onDestroy();
    }
}
