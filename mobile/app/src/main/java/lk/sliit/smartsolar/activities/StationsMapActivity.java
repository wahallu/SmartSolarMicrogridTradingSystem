/* =============================================================================
 * File        : StationsMapActivity.java
 * Project     : Smart Solar Microgrid Trading System - Android Client
 * Module      : Activities / Stations Map
 * Description : Displays nearby stations on Google Maps and in a list.
 * Author      : IT22207272 - Nisal
 * Created     : 2026-09-12
 * =============================================================================
 */

package lk.sliit.smartsolar.activities;

import android.Manifest;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.location.Location;
import android.os.Bundle;
import android.text.Editable;
import android.text.TextWatcher;
import android.util.Log;
import android.view.View;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.EditText;
import android.widget.ProgressBar;
import android.widget.Spinner;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;

import com.google.android.gms.location.LocationServices;
import com.google.android.gms.maps.CameraUpdateFactory;
import com.google.android.gms.maps.GoogleMap;
import com.google.android.gms.maps.OnMapReadyCallback;
import com.google.android.gms.maps.SupportMapFragment;
import com.google.android.gms.maps.model.BitmapDescriptorFactory;
import com.google.android.gms.maps.model.LatLng;
import com.google.android.gms.maps.model.Marker;
import com.google.android.gms.maps.model.MarkerOptions;
import com.google.android.gms.tasks.OnFailureListener;
import com.google.android.gms.tasks.OnSuccessListener;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

import lk.sliit.smartsolar.R;
import lk.sliit.smartsolar.adapters.StationAdapter;
import lk.sliit.smartsolar.api.ApiClient;
import lk.sliit.smartsolar.api.ApiResult;
import lk.sliit.smartsolar.models.StationModel;
import lk.sliit.smartsolar.util.Constants;

/** Google Maps view of the microgrid nodes near the signed in user. */
public class StationsMapActivity extends BaseActivity implements OnMapReadyCallback {

    private static final String TAG = "StationsMap";

    /** Radii offered by the spinner, in kilometres. */
    private static final int[] RADIUS_OPTIONS = {5, 10, 25, 50, 100};

    /** Most rows the nearby endpoint is asked for; a phone map never needs more. */
    private static final String RESULT_LIMIT = "50";

    /** Zoom level that shows a whole city, used for the overview camera. */
    private static final float OVERVIEW_ZOOM = 11f;

    /** Zoom level used when the user picks one node out of the list. */
    private static final float FOCUS_ZOOM = 14f;

    // Views.
    private Spinner spinnerRadius;
    private EditText inputSearch;
    private RecyclerView recyclerStations;
    private ProgressBar progressStations;
    private TextView textEmpty;
    private TextView textOffline;

    private StationAdapter adapter;

    /** The map, available only after onMapReady has fired. */
    private GoogleMap googleMap;

    /** True once the map callback has arrived, which may be before or after the data. */
    private boolean mapReady;

    /** True once one station reply (live or cached) has been rendered. */
    private boolean stationsLoaded;

    /** Every node the API returned for the current radius. */
    private List<StationModel> allStations = new ArrayList<>();

    /** The subset currently listed and plotted after the search box was applied. */
    private List<StationModel> visibleStations = new ArrayList<>();

    /**
     * Resolves an info window back to the node it belongs to.
     *
     * The Maps SDK hands the click listener a Marker, not our model, and a Marker
     * carries only a title and a snippet. Keeping this map means the tapped marker
     * can be translated back into the full StationModel that produced it.
     */
    private final Map<Marker, StationModel> markerStations = new HashMap<>();

    /** Lets a list row find the marker it should open, keyed by node identifier. */
    private final Map<String, Marker> markersByStationId = new HashMap<>();

    /**
     * The untouched JSON of each node, keyed by identifier. It is kept alongside
     * the parsed models so the detail screen can be handed exactly what the server
     * sent rather than a value re-serialised on the phone.
     */
    private final Map<String, String> rawJsonByStationId = new HashMap<>();

    /** Map centre: the device position when it is known, Malabe otherwise. */
    private double centreLat = Constants.FALLBACK_LAT;
    private double centreLng = Constants.FALLBACK_LNG;

    /** Radius sent as ?radiusKm=, chosen with the spinner. */
    private int radiusKm = (int) Constants.DEFAULT_RADIUS_KM;

    /** Identifies the newest nearby-node request so an older reply cannot replace it. */
    private int stationRequestGeneration;

    /** Builds the screen, attaches the map fragment and starts the permission flow. */
    @Override
    protected void onCreate(@Nullable Bundle savedInstanceState) {
        // super.onCreate must run first because it creates api, session and localStore.
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_stations_map);

        setupToolbar(R.id.toolbar_stations_map, getString(R.string.map_title), true);

        bindViews();
        setupRecycler();
        setupRadiusSpinner();
        setupSearch();
        attachMapFragment();

        showOnly(progressStations, recyclerStations, textEmpty);
        ensureLocationPermission();
    }

    // =====================================================================
    // Setup
    // =====================================================================

    /** Looks up every view this screen drives. */
    private void bindViews() {
        // findViewById is used throughout because view binding is not permitted on this project.
        spinnerRadius = findViewById(R.id.spinner_map_radius);
        inputSearch = findViewById(R.id.input_map_search);
        recyclerStations = findViewById(R.id.station_list);
        progressStations = findViewById(R.id.progress_map);
        textEmpty = findViewById(R.id.text_map_empty);
        textOffline = findViewById(R.id.text_map_offline);
    }

    /** Attaches the adapter and the layout manager to the node list. */
    private void setupRecycler() {
        // A vertical list of node cards sits under the map, reusing the shared adapter.
        recyclerStations.setLayoutManager(new LinearLayoutManager(this));
        adapter = new StationAdapter(new ArrayList<StationModel>(),
                new StationAdapter.OnStationClick() {

                    /** Moves the map onto the tapped node and opens its info window. */
                    @Override
                    public void onClick(StationModel station) {
                        // Tapping a row drives the map; opening the detail screen is done
                        // from the info window, which mirrors how Google Maps itself behaves.
                        focusStation(station);
                    }
                });
        recyclerStations.setAdapter(adapter);
    }

    /** Fills the radius spinner and re-queries the API whenever the choice changes. */
    private void setupRadiusSpinner() {
        // The captions are built from RADIUS_OPTIONS so a label can never drift from the
        // value that is actually sent to the server as ?radiusKm=.
        List<String> labels = new ArrayList<>();
        int defaultIndex = 0;
        for (int i = 0; i < RADIUS_OPTIONS.length; i++) {
            labels.add(RADIUS_OPTIONS[i] + " km");
            if (RADIUS_OPTIONS[i] == radiusKm) {
                defaultIndex = i;
            }
        }

        ArrayAdapter<String> radiusAdapter = new ArrayAdapter<>(this,
                android.R.layout.simple_spinner_item, labels);
        radiusAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spinnerRadius.setAdapter(radiusAdapter);
        spinnerRadius.setSelection(defaultIndex);

        spinnerRadius.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {

            /** Re-queries the API when a different radius is picked. */
            @Override
            public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                // Android fires this once during layout with the pre-selected entry, so the
                // request is only sent when the value has genuinely changed.
                int chosen = RADIUS_OPTIONS[position];
                if (chosen == radiusKm) {
                    return;
                }
                radiusKm = chosen;
                loadStations();
            }

            /** Required by the interface; the spinner always keeps a selection. */
            @Override
            public void onNothingSelected(AdapterView<?> parent) {
                // Intentionally empty.
            }
        });
    }

    /** Wires the search box to the client side filter. */
    private void setupSearch() {
        // NOTE: this filter is presentation only. It narrows the rows the SERVER already
        // selected and ranked; it never changes which nodes are inside the radius and it
        // never re-orders them, so the API remains the only authority on the result set.
        inputSearch.addTextChangedListener(new TextWatcher() {

            /** Required by TextWatcher; nothing is needed before the text changes. */
            @Override
            public void beforeTextChanged(CharSequence s, int start, int count, int after) {
                // Intentionally empty.
            }

            /** Required by TextWatcher; the filter runs after the edit is applied. */
            @Override
            public void onTextChanged(CharSequence s, int start, int before, int count) {
                // Intentionally empty.
            }

            /** Re-filters the list and the markers as the user types. */
            @Override
            public void afterTextChanged(Editable editable) {
                // No network call is made, so there is no need to debounce this one.
                applyFilter();
            }
        });
    }

    /**
     * Hands this Activity to the map fragment declared in the layout.
     *
     * getMapAsync is ASYNCHRONOUS: onMapReady may fire before or after the station
     * request returns. Neither callback assumes the other has run - each one records
     * what it received and then calls plotMarkers(), which no-ops until both the map
     * and the data are present. That is why the map is never touched here.
     */
    private void attachMapFragment() {
        // findFragmentById can return null if the fragment failed to inflate, so it is guarded.
        SupportMapFragment mapFragment =
                (SupportMapFragment) getSupportFragmentManager().findFragmentById(R.id.map_fragment);

        if (mapFragment == null) {
            // The list underneath still works, so the screen reports the problem and carries on.
            Log.e(TAG, "The map fragment could not be found in activity_stations_map.xml.");
            toast(getString(R.string.error_generic));
            return;
        }
        mapFragment.getMapAsync(this);
    }

    // =====================================================================
    // Map
    // =====================================================================

    /** Receives the ready map, centres it and draws whatever data has already arrived. */
    @Override
    public void onMapReady(@NonNull GoogleMap map) {
        // This may run before or after loadStations() has answered; plotMarkers() copes with both.
        googleMap = map;
        mapReady = true;

        googleMap.getUiSettings().setZoomControlsEnabled(true);
        googleMap.getUiSettings().setMapToolbarEnabled(false);

        enableMyLocationLayer();
        centreCamera();
        plotMarkers();

        googleMap.setOnInfoWindowClickListener(new GoogleMap.OnInfoWindowClickListener() {

            /** Opens the detail screen for the node behind the tapped info window. */
            @Override
            public void onInfoWindowClick(@NonNull Marker marker) {
                // markerStations is what turns the Maps SDK's Marker back into our model.
                StationModel station = markerStations.get(marker);
                if (station != null) {
                    openStationDetail(station);
                }
            }
        });
    }

    /** Turns on the blue "my location" dot when the permission is held. */
    private void enableMyLocationLayer() {
        // setMyLocationEnabled is annotated as requiring a permission, so the call is wrapped
        // in try/catch as well as guarded: a revoked permission must never crash the screen.
        if (googleMap == null || !hasLocationPermission()) {
            return;
        }
        try {
            googleMap.setMyLocationEnabled(true);
        } catch (SecurityException ex) {
            Log.w(TAG, "Location permission was revoked before the map layer was enabled.", ex);
        }
    }

    /** Moves the camera onto the current centre at the overview zoom level. */
    private void centreCamera() {
        // Called from the map callback and again once a device position is resolved.
        if (googleMap == null) {
            return;
        }
        LatLng centre = new LatLng(centreLat, centreLng);
        googleMap.moveCamera(CameraUpdateFactory.newLatLngZoom(centre, OVERVIEW_ZOOM));
    }

    /**
     * Draws one marker per visible node.
     *
     * NO-OP UNTIL BOTH SIDES ARE READY. The map arrives from getMapAsync and the
     * nodes arrive from the API, in whichever order the device happens to produce
     * them, so this method is called from both callbacks and simply returns until
     * the pair is complete.
     */
    private void plotMarkers() {
        // Guard first: this is the whole reason the two asynchronous callbacks can be
        // written independently of one another.
        if (!mapReady || googleMap == null || !stationsLoaded) {
            return;
        }

        googleMap.clear();
        markerStations.clear();
        markersByStationId.clear();

        // Latitude and longitude are validated by the API. Zero is a valid value for each
        // coordinate, including the real intersection at 0,0, so it is never a missing-data sentinel.
        for (StationModel station : visibleStations) {
            // Amber for a node in service, blue for one that is out of service, so the map
            // reads the same way as the dimmed cards in the list below it.
            float hue = station.isActive()
                    ? BitmapDescriptorFactory.HUE_ORANGE
                    : BitmapDescriptorFactory.HUE_AZURE;

            MarkerOptions options = new MarkerOptions()
                    .position(new LatLng(station.getLatitude(), station.getLongitude()))
                    .title(station.getStationCode())
                    .snippet(station.getName() + " - " + station.getSlotsLabel())
                    .icon(BitmapDescriptorFactory.defaultMarker(hue));

            Marker marker = googleMap.addMarker(options);
            if (marker != null) {
                markerStations.put(marker, station);
                if (station.getId() != null) {
                    markersByStationId.put(station.getId(), marker);
                }
            }
        }
    }

    /** Animates the camera onto one node and opens its info window. */
    private void focusStation(StationModel station) {
        // Called when a list row is tapped, so the map and the list stay in step.
        if (station == null || googleMap == null) {
            return;
        }

        LatLng position = new LatLng(station.getLatitude(), station.getLongitude());
        googleMap.animateCamera(CameraUpdateFactory.newLatLngZoom(position, FOCUS_ZOOM));

        Marker marker = station.getId() == null ? null : markersByStationId.get(station.getId());
        if (marker != null) {
            marker.showInfoWindow();
        }
    }

    // =====================================================================
    // Location permission
    // =====================================================================

    /** True when the user has already granted fine location access. */
    private boolean hasLocationPermission() {
        // ContextCompat keeps this correct on the minSdk 24 devices as well as on 35.
        return ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION)
                == PackageManager.PERMISSION_GRANTED;
    }

    /** Requests location access when it is missing, then loads the nodes either way. */
    private void ensureLocationPermission() {
        // The screen is useful without location: it simply centres on Malabe instead.
        if (hasLocationPermission()) {
            resolveCentreThenLoad();
            return;
        }

        if (ActivityCompat.shouldShowRequestPermissionRationale(this,
                Manifest.permission.ACCESS_FINE_LOCATION)) {
            // The user refused once before, so the reason is explained before asking again.
            // The dialog is not modal to the system prompt; it stays behind it and can be
            // read afterwards, which is enough to justify the second request.
            info(getString(R.string.action_nearby_nodes),
                    getString(R.string.permission_location_rationale));
        }

        ActivityCompat.requestPermissions(this,
                new String[]{Manifest.permission.ACCESS_FINE_LOCATION},
                Constants.REQ_LOCATION_PERMISSION);
    }

    /** Continues after the system permission prompt, whichever answer was given. */
    @Override
    public void onRequestPermissionsResult(int requestCode, @NonNull String[] permissions,
                                           @NonNull int[] grantResults) {
        // A refusal is not an error here - the map simply falls back to the default centre.
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == Constants.REQ_LOCATION_PERMISSION) {
            resolveCentreThenLoad();
        }
    }

    /**
     * Works out the map centre and then asks the API for the nodes around it.
     *
     * With permission the device's last known position is used; without it, or when
     * Play services has no fix to offer, the centre falls back to Constants.FALLBACK_LAT
     * / FALLBACK_LNG (Malabe) so the screen is never empty.
     */
    private void resolveCentreThenLoad() {
        // Without the permission there is nothing to ask the location provider for.
        if (!hasLocationPermission()) {
            loadStations();
            return;
        }

        enableMyLocationLayer();

        try {
            LocationServices.getFusedLocationProviderClient(this).getLastLocation()
                    .addOnSuccessListener(new OnSuccessListener<Location>() {

                        /** Receives the last known fix, which may legitimately be null. */
                        @Override
                        public void onSuccess(Location location) {
                            // A null fix means the device has not located itself recently.
                            if (location != null) {
                                centreLat = location.getLatitude();
                                centreLng = location.getLongitude();
                                centreCamera();
                            }
                            loadStations();
                        }
                    })
                    .addOnFailureListener(new OnFailureListener() {

                        /** Falls back to the default centre when the provider errors. */
                        @Override
                        public void onFailure(@NonNull Exception ex) {
                            // The nodes are still worth showing, just ranked from Malabe.
                            Log.w(TAG, "Could not read the last known location.", ex);
                            loadStations();
                        }
                    });
        } catch (SecurityException ex) {
            // Covers a permission revoked between the check above and this call.
            Log.w(TAG, "Location permission was revoked while requesting a position.", ex);
            loadStations();
        }
    }

    // =====================================================================
    // Data loading
    // =====================================================================

    /**
     * Calls GET /stations/nearby for the current centre and radius.
     *
     * THE SERVER DOES THE GEOGRAPHY. The phone sends nothing but its coordinates,
     * a radius and a row limit. Which nodes fall inside that circle, how far each
     * one is and the order they come back in are all produced by the Haversine
     * calculation in the Web API's service layer. The distance shown on each card
     * is the server's distanceKm value, displayed verbatim.
     */
    private void loadStations() {
        final int requestGeneration = ++stationRequestGeneration;

        // The loading state is shown first so a stale list is never left on screen.
        showOnly(progressStations, recyclerStations, textEmpty);

        Map<String, String> params = new LinkedHashMap<>();
        params.put("lat", String.valueOf(centreLat));
        params.put("lng", String.valueOf(centreLng));
        params.put("radiusKm", String.valueOf(radiusKm));
        params.put("limit", RESULT_LIMIT);

        // withQuery URL encodes each value and drops any that is blank.
        String path = ApiClient.withQuery(Constants.EP_STATIONS_NEARBY, params);

        api.get(path, token(), new ApiClient.Callback() {

            /** Receives the node list on the main thread. */
            @Override
            public void onResult(ApiResult result) {
                // Radius changes can leave more than one request in flight. Only the newest
                // response represents the spinner selection currently visible to the user.
                if (requestGeneration != stationRequestGeneration
                        || isFinishing() || isDestroyed()) {
                    return;
                }

                // ApiClient posts callbacks to the main looper, so views are touched directly.
                if (result.isSuccess()) {
                    handleLoadSuccess(result);
                } else {
                    handleLoadFailure(result);
                }
            }
        });
    }

    /** Stores, caches and renders a successful reply. */
    private void handleLoadSuccess(ApiResult result) {
        // GET /stations/nearby answers with a bare array, so getDataArray is the right reader.
        JSONArray array = result.getDataArray();
        allStations = StationModel.fromJsonArray(array);
        keepRawJson(array);

        // The rows are written to SQLite so this screen still has something to show the next
        // time the phone has no connectivity. The cache is never authoritative.
        localStore.cacheStations(allStations);

        textOffline.setVisibility(View.GONE);
        stationsLoaded = true;
        applyFilter();
    }

    /**
     * Falls back to the SQLite cache on a transport failure, and otherwise shows the
     * server's own wording.
     */
    private void handleLoadFailure(ApiResult result) {
        // isNetworkError() is true only when the request never reached the API at all.
        if (result.isNetworkError()) {
            List<StationModel> cached = localStore.getCachedStations();
            if (!cached.isEmpty()) {
                // Cached rows carry no raw payload, so the detail screen will be handed an id
                // and will re-fetch the node once connectivity returns.
                allStations = cached;
                rawJsonByStationId.clear();
                textOffline.setText(getString(R.string.offline_cached_notice));
                textOffline.setVisibility(View.VISIBLE);
                stationsLoaded = true;
                applyFilter();
                return;
            }
        }

        // Nothing cached, or a genuine server refusal: show the API's message.
        allStations = new ArrayList<>();
        stationsLoaded = true;
        applyFilter();
        showApiError(result);
    }

    /** Remembers the untouched JSON of each node, keyed by its identifier. */
    private void keepRawJson(JSONArray array) {
        // The detail screen prefers the exact server payload over a re-serialised model.
        rawJsonByStationId.clear();
        if (array == null) {
            return;
        }
        for (int i = 0; i < array.length(); i++) {
            JSONObject item = array.optJSONObject(i);
            if (item == null) {
                continue;
            }
            String id = item.optString("id", "");
            if (!id.isEmpty()) {
                rawJsonByStationId.put(id, item.toString());
            }
        }
    }

    // =====================================================================
    // Rendering
    // =====================================================================

    /**
     * Applies the search box to the loaded list, then repaints the list and the map.
     *
     * The term is matched against the node name, its code and its city. This is a
     * cosmetic narrowing of rows the server already chose - see setupSearch().
     */
    private void applyFilter() {
        // An empty box means "show everything the API returned".
        String term = inputSearch.getText() == null
                ? "" : inputSearch.getText().toString().trim().toLowerCase(Locale.getDefault());

        List<StationModel> filtered = new ArrayList<>();
        for (StationModel station : allStations) {
            if (term.isEmpty() || matches(station, term)) {
                filtered.add(station);
            }
        }

        visibleStations = filtered;
        adapter.updateData(visibleStations);

        if (visibleStations.isEmpty()) {
            showOnly(textEmpty, progressStations, recyclerStations);
        } else {
            showOnly(recyclerStations, progressStations, textEmpty);
        }

        // The markers follow the list so the two halves of the screen always agree.
        plotMarkers();
    }

    /** True when a node's name, code or city contains the search term. */
    private boolean matches(StationModel station, String term) {
        // Every field is compared in lower case so the search is case insensitive.
        return contains(station.getName(), term)
                || contains(station.getStationCode(), term)
                || contains(station.getCity(), term);
    }

    /** Null safe "does this value contain the term" test. */
    private boolean contains(String value, String term) {
        // A node may legitimately have no city, so a null value simply does not match.
        return value != null && value.toLowerCase(Locale.getDefault()).contains(term);
    }

    /** Opens the detail screen for one node. */
    private void openStationDetail(StationModel station) {
        // The raw payload is preferred so the detail screen renders without a round trip;
        // a cached row has none, so its identifier travels instead and the screen re-fetches.
        if (station == null) {
            return;
        }

        Intent intent = new Intent(this, StationDetailActivity.class);
        String raw = station.getId() == null ? null : rawJsonByStationId.get(station.getId());

        if (raw != null) {
            intent.putExtra(Constants.EXTRA_STATION_JSON, raw);
        } else {
            intent.putExtra(Constants.EXTRA_STATION_ID, station.getId());
        }
        startActivity(intent);
    }
}
