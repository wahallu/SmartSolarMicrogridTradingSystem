/* =============================================================================
 * File        : ScannerActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Activities / Scanner
 * Description : Scans or accepts a booking token for server verification.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-25
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.Manifest;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Bundle;
import android.view.LayoutInflater;
import android.view.View;
import android.widget.ImageButton;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.appcompat.app.AlertDialog;
import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;

import com.google.android.material.button.MaterialButton;
import com.google.android.material.textfield.TextInputEditText;
import com.google.zxing.ResultPoint;
import com.journeyapps.barcodescanner.BarcodeCallback;
import com.journeyapps.barcodescanner.BarcodeResult;
import com.journeyapps.barcodescanner.CaptureManager;
import com.journeyapps.barcodescanner.DecoratedBarcodeView;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.List;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.util.Constants;

/** Scans a prosumer's booking QR code and verifies it against the Web API. */
public class ScannerActivity extends BaseActivity {

    private DecoratedBarcodeView barcodeView;
    private CaptureManager capture;
    private View overlayVerifying;

    /** Guards against the decoder firing twice while a verification is in flight. */
    private boolean verifying;

    /** Builds the screen, wires the scanner and asks for camera permission. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // BaseActivity prepares the shared api, session and localStore helpers first.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_scanner);

        barcodeView = findViewById(R.id.barcode_view);
        overlayVerifying = findViewById(R.id.overlay_verifying);

        ImageButton back = findViewById(R.id.btn_scanner_back);
        back.setOnClickListener(v -> finish());

        MaterialButton manual = findViewById(R.id.btn_enter_manually);
        manual.setOnClickListener(v -> promptForToken());

        // CaptureManager owns the camera lifecycle; the Activity just forwards to it.
        capture = new CaptureManager(this, barcodeView);
        capture.initializeFromIntent(getIntent(), savedInstanceState);

        ensureCameraPermission();
    }

    /** Requests CAMERA at runtime, explaining why when the user has refused before. */
    private void ensureCameraPermission() {
        // Without the permission the preview stays black, so the reason is worth stating.
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA)
                == PackageManager.PERMISSION_GRANTED) {
            startDecoding();
            return;
        }

        if (ActivityCompat.shouldShowRequestPermissionRationale(this, Manifest.permission.CAMERA)) {
            info(getString(R.string.scanner_title), getString(R.string.permission_camera_rationale));
        }

        ActivityCompat.requestPermissions(this,
                new String[]{Manifest.permission.CAMERA},
                Constants.REQ_CAMERA_PERMISSION);
    }

    /** Arms the decoder for a single scan. */
    private void startDecoding() {
        // decodeSingle stops after one result, which is what a one-booking-at-a-time
        // workflow wants; it is re-armed explicitly after a failed verification.
        verifying = false;
        barcodeView.decodeSingle(new BarcodeCallback() {

            /** Receives the decoded text and sends it to the server for verification. */
            @Override
            public void barcodeResult(BarcodeResult result) {
                // The guard stops a second frame decoding while the first is still in flight.
                if (verifying || result == null || result.getText() == null) {
                    return;
                }
                verifyToken(result.getText());
            }

            /** Reports the corner points ZXing is currently tracking in the frame. */
            @Override
            public void possibleResultPoints(List<ResultPoint> resultPoints) {
                
            }
        });
    }

    /**
     * Posts the scanned text to the Web API.
     *
     * The raw text may be the bare token OR the entire JSON payload that was
     * encoded into the image. The API accepts both, and - importantly - it
     * trusts ONLY the token: every other field is re-read from the database, so
     * a tampered payload gains nothing.
     */
    private void verifyToken(final String rawText) {
        // Marking the screen busy prevents a double submission from a second decode.
        verifying = true;
        overlayVerifying.setVisibility(View.VISIBLE);

        JSONObject body = new JSONObject();
        try {
            body.put("qrToken", rawText.trim());
        } catch (JSONException ex) {
            overlayVerifying.setVisibility(View.GONE);
            toastLong(getString(R.string.error_generic));
            startDecoding();
            return;
        }

        api.post(Constants.EP_VERIFY_QR, body, token(), result -> {
            overlayVerifying.setVisibility(View.GONE);

            if (result.isSuccess() && result.getDataObject() != null) {
                openVerifyResult(result.getDataObject().toString(), rawText.trim());
                return;
            }

            // An expired session is handled centrally by the base class.
            if (handleAuthFailure(result)) {
                return;
            }

            showVerificationFailure(result);
        });
    }

    /** Hands the verified booking to the screen that finalises the transfer. */
    private void openVerifyResult(String reservationJson, String qrToken) {
        // The token is forwarded because completing the transfer needs it again.
        Intent intent = new Intent(this, VerifyResultActivity.class);
        intent.putExtra(Constants.EXTRA_RESERVATION_JSON, reservationJson);
        intent.putExtra(Constants.EXTRA_QR_TOKEN, qrToken);
        startActivity(intent);
        finish();
    }

    /**
     * Explains why the server refused the code and offers another attempt.
     * Each branch is a rule R9 outcome and is an expected result, not a defect.
     */
    private void showVerificationFailure(ApiResult result) {
        // The title names the specific rule outcome; the body is the server's own wording.
        String code = result.getErrorCode();
        String title;

        if (Constants.ERR_QR_INVALID.equals(code)) {
            title = "QR code not recognised";
        } else if (Constants.ERR_QR_NOT_APPROVED.equals(code)) {
            title = "Booking is not approved";
        } else if (Constants.ERR_QR_OUT_OF_WINDOW.equals(code)) {
            title = "Outside the transfer window";
        } else if (result.isNetworkError()) {
            title = getString(R.string.error_network);
        } else {
            title = "Verification failed";
        }

        String message = result.getMessage().isEmpty()
                ? getString(R.string.error_generic)
                : result.getMessage();

        new AlertDialog.Builder(this)
                .setTitle(title)
                .setMessage(message)
                .setCancelable(false)
                .setPositiveButton("Scan again", (dialog, which) -> startDecoding())
                .setNegativeButton(R.string.action_close, (dialog, which) -> finish())
                .show();
    }

    /**
     * Lets the operator type a token instead of scanning one. This is the path
     * used on an emulator, where no real camera is available to point at a code.
     */
    private void promptForToken() {
        // The typed value goes through verifyToken unchanged, so the flow is identical.
        View content = LayoutInflater.from(this).inflate(R.layout.dialog_token_input, null, false);
        final TextInputEditText input = content.findViewById(R.id.token_input);

        new AlertDialog.Builder(this)
                .setTitle(R.string.verify_title)
                .setView(content)
                .setPositiveButton(R.string.action_confirm, (dialog, which) -> {
                    String typed = input.getText() == null ? "" : input.getText().toString().trim();
                    if (typed.isEmpty()) {
                        toast("Enter the booking QR token.");
                        return;
                    }
                    verifyToken(typed);
                })
                .setNegativeButton(R.string.action_cancel, (dialog, which) -> dialog.dismiss())
                .show();
    }

    // =====================================================================
    // CaptureManager lifecycle forwarding - all six callbacks are required
    // =====================================================================

    /** Resumes the camera preview. */
    @Override
    protected void onResume() {
        // CaptureManager reopens the camera; skipping this leaves a black preview.
        super.onResume();
        capture.onResume();
    }

    /** Releases the camera so other applications can use it. */
    @Override
    protected void onPause() {
        // Holding the camera across a pause would block every other app on the device.
        super.onPause();
        capture.onPause();
    }

    /** Tears the scanner down. */
    @Override
    protected void onDestroy() {
        // Releases the decoder thread and the camera handle.
        super.onDestroy();
        capture.onDestroy();
    }

    /** Preserves the scanner state across a configuration change. */
    @Override
    protected void onSaveInstanceState(@NonNull Bundle outState) {
        // CaptureManager stores its own orientation lock state here.
        super.onSaveInstanceState(outState);
        capture.onSaveInstanceState(outState);
    }

    /** Forwards the camera permission answer and starts decoding once granted. */
    @Override
    public void onRequestPermissionsResult(int requestCode, @NonNull String[] permissions,
                                           @NonNull int[] grantResults) {
        // CaptureManager needs the result too, so it is forwarded before the local handling.
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        capture.onRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode == Constants.REQ_CAMERA_PERMISSION) {
            boolean granted = grantResults.length > 0
                    && grantResults[0] == PackageManager.PERMISSION_GRANTED;

            if (granted) {
                startDecoding();
            } else {
                // Refusing the camera is not fatal - the manual entry path still works.
                info(getString(R.string.scanner_title),
                        getString(R.string.permission_camera_rationale)
                                + "\n\nYou can still verify a booking using \"Enter code manually\".");
            }
        }
    }
}
