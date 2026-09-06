/*
 * =============================================================================
 * File        : StationDetailActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : activities
 * Description : Everything the Web API knows about one microgrid node: its code
 *               and service status, its address, a small non interactive Google
 *               Map pinned on its coordinates, its rated capacity, battery bay
 *               availability, operating hours and contact number.
 *
 *               THIS SCREEN IS A UI LAYER ONLY. Every figure below arrived from
 *               the API already computed - the bay counts, the hours and the
 *               status are the server's values, shown verbatim. The booking
 *               button merely opens the booking screen; whether a slot can
 *               actually be reserved at this node is decided by the FAT service
 *               layer when the request is submitted.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.content.Intent;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Bundle;
import android.text.TextUtils;
import android.util.Log;
import android.view.View;
import android.widget.ProgressBar;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.core.content.ContextCompat;
import androidx.core.widget.NestedScrollView;

import com.google.android.gms.maps.CameraUpdateFactory;
import com.google.android.gms.maps.GoogleMap;
import com.google.android.gms.maps.OnMapReadyCallback;
import com.google.android.gms.maps.SupportMapFragment;
import com.google.android.gms.maps.model.BitmapDescriptorFactory;
import com.google.android.gms.maps.model.LatLng;
import com.google.android.gms.maps.model.MarkerOptions;
import com.google.android.material.button.MaterialButton;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.Locale;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.api.ApiClient;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.models.StationModel;
import lk.sliit.smartsolar.util.Constants;

/** Full detail view of a single microgrid node. */
public class StationDetailActivity extends BaseActivity implements OnMapReadyCallback {

    private static final String TAG = "StationDetail";

    /** Zoom level that shows the node and the street it sits on. */
    private static final float NODE_ZOOM = 15f;

    // Screen states.
    private NestedScrollView scrollDetail;
    private ProgressBar progressDetail;

    // Header.
    private TextView textCode;
    private TextView textStatus;
    private TextView textName;
    private TextView textAddress;

    // Detail rows.
    private TextView textCapacity;
    private TextView textSlots;
    private TextView textHours;
    private View rowContact;
    private TextView textContact;

    // Actions.
    private MaterialButton btnBook;
    private MaterialButton btnDirections;

    /** The node being shown. */
    private StationModel station;

    /** Identifier used when the caller passed an id rather than a payload. */
    private String stationId;

    /** The map, available only after onMapReady has fired. */
    private GoogleMap googleMap;

    /** True once the map callback has arrived. */
    private boolean mapReady;

    /** Builds the screen and resolves the node from whichever extra was supplied. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // super.onCreate must run first because it creates api, session and localStore.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_station_detail);

        setupToolbar(R.id.toolbar, getString(R.string.station_detail_title), true);

        bindViews();
        setupActions();
        attachMapFragment();

        // The map screen normally hands over the exact payload it received, which spares
        // this screen a round trip. An identifier is the fallback, used for a node that
        // came out of the SQLite cache and therefore has no stored payload.
        String rawJson = getIntent().getStringExtra(Constants.EXTRA_STATION_JSON);
        stationId = getIntent().getStringExtra(Constants.EXTRA_STATION_ID);

        if (!TextUtils.isEmpty(rawJson) && bindFromJson(rawJson)) {
            return;
        }

        if (TextUtils.isEmpty(stationId)) {
            // Neither extra was usable, so there is nothing to show.
            toast(getString(R.string.error_generic));
            finish();
            return;
        }

        loadStation();
    }

    // =====================================================================
    // Setup
    // =====================================================================

    /** Looks up every view this screen drives. */
    private void bindViews() {
        // findViewById is used throughout because view binding is not permitted on this project.
        scrollDetail = findViewById(R.id.scroll_station_detail);
        progressDetail = findViewById(R.id.progress_station_detail);

        textCode = findViewById(R.id.text_station_detail_code);
        textStatus = findViewById(R.id.text_station_detail_status);
        textName = findViewById(R.id.text_station_detail_name);
        textAddress = findViewById(R.id.text_station_detail_address);

        textCapacity = findViewById(R.id.text_station_detail_capacity);
        textSlots = findViewById(R.id.text_station_detail_slots);
        textHours = findViewById(R.id.text_station_detail_hours);
        rowContact = findViewById(R.id.row_station_detail_contact);
        textContact = findViewById(R.id.text_station_detail_contact);

        btnBook = findViewById(R.id.btn_station_detail_book);
        btnDirections = findViewById(R.id.btn_station_detail_directions);
    }

    /** Wires the contact row and the two action buttons. */
    private void setupActions() {
        // The listeners are attached once; which controls are visible is decided in bindStation.
        rowContact.setOnClickListener(new View.OnClickListener() {

            /** Opens the phone dialler pre-filled with the node's number. */
            @Override
            public void onClick(View view) {
                // ACTION_DIAL only opens the dialler - it never places a call by itself.
                dialContactNumber();
            }
        });

        btnBook.setOnClickListener(new View.OnClickListener() {

            /** Opens the booking screen. */
            @Override
            public void onClick(View view) {
                // Whether this node can actually take a booking is the API's decision.
                openNewBooking();
            }
        });

        btnDirections.setOnClickListener(new View.OnClickListener() {

            /** Hands the node's coordinates to a navigation app. */
            @Override
            public void onClick(View view) {
                // Guarded so a device with no maps application simply says so.
                openDirections();
            }
        });
    }

    /**
     * Hands this Activity to the small map declared in the layout.
     *
     * As on the nearby nodes screen, getMapAsync is asynchronous, so the map and
     * the node data can arrive in either order. Both paths end in plotStation(),
     * which no-ops until the pair is complete.
     */
    private void attachMapFragment() {
        // A null fragment is tolerated: the rest of the record is still readable.
        SupportMapFragment mapFragment = (SupportMapFragment)
                getSupportFragmentManager().findFragmentById(R.id.map_station_detail);

        if (mapFragment == null) {
            Log.e(TAG, "The map fragment could not be found in activity_station_detail.xml.");
            return;
        }
        mapFragment.getMapAsync(this);
    }

    // =====================================================================
    // Loading
    // =====================================================================

    /** Rebuilds the node from a payload the caller passed through the intent. */
    private boolean bindFromJson(String rawJson) {
        // Returns false when the string is not the JSON object this screen expects,
        // so onCreate can fall back to loading the node by its identifier.
        try {
            JSONObject json = new JSONObject(rawJson);
            station = StationModel.fromJson(json);
            bindStation();
            showOnly(scrollDetail, progressDetail);
            return true;
        } catch (JSONException ex) {
            Log.w(TAG, "The station payload passed in the intent could not be read.", ex);
            return false;
        }
    }

    /** Calls GET /stations/{id} when only an identifier was supplied. */
    private void loadStation() {
        // The loading spinner replaces the content until the record arrives.
        showOnly(progressDetail, scrollDetail);

        api.get(Constants.EP_STATIONS + "/" + stationId, token(), new ApiClient.Callback() {

            /** Receives the node on the main thread. */
            @Override
            public void onResult(ApiResult result) {
                // ApiClient posts callbacks to the main looper, so views are touched directly.
                if (!result.isSuccess()) {
                    // There is nothing else on this screen to fall back to, so it closes
                    // after showing the wording the server itself supplied.
                    showApiError(result);
                    finish();
                    return;
                }

                station = StationModel.fromJson(result.getDataObject());
                bindStation();
                showOnly(scrollDetail, progressDetail);
            }
        });
    }

    // =====================================================================
    // Rendering
    // =====================================================================

    /** Fills every field of the screen from the loaded node. */
    private void bindStation() {
        // Every value below was produced by the Web API; nothing is derived on the phone.
        if (station == null) {
            return;
        }

        textCode.setText(station.getStationCode());
        textName.setText(station.getName());
        textAddress.setText(fullAddress());
        bindStatusChip();

        textCapacity.setText(String.format(Locale.getDefault(), "%.0f kWh", station.getCapacityKwh()));
        textSlots.setText(station.getSlotsLabel());
        textHours.setText(station.getHoursLabel());

        // A node without a published number simply hides the row rather than showing a blank.
        String contact = station.getContactNumber();
        if (TextUtils.isEmpty(contact)) {
            rowContact.setVisibility(View.GONE);
        } else {
            rowContact.setVisibility(View.VISIBLE);
            textContact.setText(contact);
        }

        // Booking is a prosumer action, so staff accounts never see the button. Hiding it is
        // presentation only: the API applies the role rule and refuses a staff booking anyway.
        btnBook.setVisibility(session.isProsumer() ? View.VISIBLE : View.GONE);

        plotStation();
    }

    /** Joins the street address and the city into one line. */
    private String fullAddress() {
        // The comma is skipped when the server sent no city for this node.
        String address = station.getAddress() == null ? "" : station.getAddress();
        String city = station.getCity();
        return TextUtils.isEmpty(city) ? address : address + ", " + city;
    }

    /** Writes the service status into the pill and tints the pill to match. */
    private void bindStatusChip() {
        // The single bg_status_chip drawable is recoloured at runtime, exactly as
        // StationAdapter does on a node card, so the two views read identically.
        boolean active = station.isActive();
        textStatus.setText(station.getStatus());
        textStatus.setTextColor(ContextCompat.getColor(this,
                active ? R.color.status_completed_text : R.color.status_cancelled_text));

        Drawable pill = textStatus.getBackground();
        if (pill instanceof GradientDrawable) {
            // mutate() gives this chip its own state so no shared drawable is recoloured.
            GradientDrawable shape = (GradientDrawable) pill.mutate();
            shape.setColor(ContextCompat.getColor(this,
                    active ? R.color.status_completed_bg : R.color.status_cancelled_bg));
        }
    }

    // =====================================================================
    // Map
    // =====================================================================

    /** Receives the ready map and draws the node if it has already been loaded. */
    @Override
    public void onMapReady(@NonNull GoogleMap map) {
        // This may run before or after the node arrives; plotStation() copes with both.
        googleMap = map;
        mapReady = true;

        // The map here is a picture of one location inside a scrolling page, so every
        // gesture is switched off: it must never swallow the drag that scrolls the screen.
        googleMap.getUiSettings().setAllGesturesEnabled(false);
        googleMap.getUiSettings().setMapToolbarEnabled(false);

        plotStation();
    }

    /** Centres the small map on the node and drops a single marker. */
    private void plotStation() {
        // No-ops until BOTH the map callback and the node data have arrived.
        if (!mapReady || googleMap == null || station == null) {
            return;
        }

        LatLng position = new LatLng(station.getLatitude(), station.getLongitude());
        googleMap.clear();
        googleMap.addMarker(new MarkerOptions()
                .position(position)
                .title(station.getStationCode())
                .snippet(station.getName())
                .icon(BitmapDescriptorFactory.defaultMarker(
                        station.isActive()
                                ? BitmapDescriptorFactory.HUE_ORANGE
                                : BitmapDescriptorFactory.HUE_AZURE)));
        googleMap.moveCamera(CameraUpdateFactory.newLatLngZoom(position, NODE_ZOOM));
    }

    // =====================================================================
    // Actions
    // =====================================================================

    /** Opens the phone dialler with the node's contact number filled in. */
    private void dialContactNumber() {
        // ACTION_DIAL needs no permission because it only pre-fills the dialler; the user
        // still presses call. resolveActivity guards the rare device with no dialler at all.
        if (station == null || TextUtils.isEmpty(station.getContactNumber())) {
            return;
        }

        Intent intent = new Intent(Intent.ACTION_DIAL,
                Uri.parse("tel:" + station.getContactNumber()));

        if (intent.resolveActivity(getPackageManager()) == null) {
            toast(getString(R.string.error_generic));
            return;
        }
        startActivity(intent);
    }

    /** Opens the booking screen so a slot can be reserved at this node. */
    private void openNewBooking() {
        // The identifier travels as a hint. The booking screen still loads the node list and
        // the available slots from the API, and the API alone decides whether the request is
        // accepted - the 7 day window and slot availability are its rules, not the phone's.
        Intent intent = new Intent(this, NewBookingActivity.class);
        if (station != null && station.getId() != null) {
            intent.putExtra(Constants.EXTRA_STATION_ID, station.getId());
        }
        startActivity(intent);
    }

    /**
     * Hands the node's coordinates to whichever navigation app the device has.
     *
     * resolveActivity is checked FIRST. startActivity with an unhandled intent throws
     * ActivityNotFoundException, so on a device with no maps application this guard is
     * the difference between a polite message and a crash.
     */
    private void openDirections() {
        // "google.navigation:q=lat,lng" is the documented turn by turn navigation intent.
        if (station == null) {
            return;
        }

        Uri destination = Uri.parse("google.navigation:q="
                + station.getLatitude() + "," + station.getLongitude());
        Intent intent = new Intent(Intent.ACTION_VIEW, destination);

        if (intent.resolveActivity(getPackageManager()) == null) {
            // No navigation app is installed, so the user is told rather than shown a crash.
            toast(getString(R.string.error_generic));
            return;
        }
        startActivity(intent);
    }
}
