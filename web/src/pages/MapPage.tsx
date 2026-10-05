/* =============================================================================
 * File        : src/pages/MapPage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Pages / Map
 * Description : Plots microgrid stations and nearby results on an interactive
 *               map.
 * Author      : IT22207272 - Nisal
 * Created     : 2026-09-13
 * =============================================================================
 */

import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { MapContainer, TileLayer, Marker, Popup, useMap } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import { MapPin, Navigation, RefreshCw, Zap } from 'lucide-react';
import { stationsApi } from '../api/services';
import { ApiError } from '../api/client';
import { Badge, EmptyState, ErrorState, PageLoader, SearchInput } from '../components/ui';
import { kwh } from '../utils/format';
import type { Station } from '../types';

/** Fallback centre used when the browser will not share a position - Malabe. */
const FALLBACK_CENTRE: [number, number] = [6.9061, 79.9696];

/** Radii the nearby query offers, in kilometres. */
const RADIUS_OPTIONS: number[] = [5, 10, 25, 50, 100];

/** The radius the screen opens with. */
const DEFAULT_RADIUS_KM = 25;

/** How many nodes are fetched in one go; the network is far smaller than this. */
const MAX_NODES = 100;

/**
 * A node as this screen renders it. NearbyStation already extends Station with a
 * required distanceKm, so a nearby result drops straight into this shape while a
 * plain list result simply leaves the distance undefined.
 */
type MapStation = Station & { distanceKm?: number };

/** Whether the browser position request has finished, and how it finished. */
type GeoStatus = 'pending' | 'granted' | 'unavailable';

/**
 * Builds the HTML for a teardrop pin.
 *
 * Leaflet's default marker is an image loaded by a relative URL, which a bundler
 * rewrites and Leaflet then cannot find - the classic "broken marker" bug. Using
 * a divIcon made of plain HTML avoids the problem completely: no image asset is
 * involved at all. The empty className replaces Leaflet's own leaflet-div-icon
 * class, which would otherwise draw a white box behind the pin.
 */
function pinHtml(fill: string): string {
  // A square with three rounded corners, rotated so the square corner points down.
  return (
    '<span style="display:block;width:20px;height:20px;margin:3px;' +
    'border-radius:50% 50% 50% 0;transform:rotate(-45deg);background:' +
    fill +
    ';border:2px solid #ffffff;box-shadow:0 1px 4px rgba(15,23,42,0.45);"></span>'
  );
}

/**
 * Amber pin for a node that is in service. The explicit iconSize / iconAnchor
 * matter: without them Leaflet cannot work out where the tip of the pin sits and
 * the marker drifts away from its coordinates as the map is zoomed.
 */
const ACTIVE_PIN = L.divIcon({
  className: '',
  html: pinHtml('#f59e0b'),
  iconSize: [26, 32],
  iconAnchor: [13, 27],
  popupAnchor: [0, -26],
});

/** Slate pin for a node that is out of service. */
const INACTIVE_PIN = L.divIcon({
  className: '',
  html: pinHtml('#64748b'),
  iconSize: [26, 32],
  iconAnchor: [13, 27],
  popupAnchor: [0, -26],
});

/** Small blue dot marking where the browser says the user is. */
const USER_DOT = L.divIcon({
  className: '',
  html:
    '<span style="display:block;width:14px;height:14px;border-radius:9999px;' +
    'background:#2563eb;border:3px solid #ffffff;' +
    'box-shadow:0 0 0 3px rgba(37,99,235,0.30);"></span>',
  iconSize: [20, 20],
  iconAnchor: [10, 10],
  popupAnchor: [0, -12],
});

/** Turns any thrown value into the sentence a user should read. */
function describeError(err: unknown): string {
  // An ApiError already carries the API's own wording, which is always preferred.
  return err instanceof ApiError ? err.message : 'Something went wrong';
}

/**
 * The distance caption for a row, or null when no position was shared.
 *
 * The number itself is never worked out in the browser - it arrives on the DTO
 * from GET /stations/nearby, where the C# service does the Haversine maths.
 */
function distanceLabel(station: MapStation): string | null {
  // Undefined means the plain /stations list was used, so there is no distance.
  return station.distanceKm === undefined ? null : `${station.distanceKm.toFixed(1)} km away`;
}

/**
 * Recentres the map whenever a node is picked in the left hand list.
 *
 * useMap only resolves inside a child of MapContainer, so this tiny component is
 * rendered within the map rather than beside it.
 */
function FlyTo({ position }: { position: [number, number] | null }) {
  // The map instance comes from the surrounding MapContainer context.
  const map = useMap();

  useEffect(() => {
    if (position) map.flyTo(position, 14);
  }, [position, map]);

  return null;
}

/** The node map: a searchable node list beside an OpenStreetMap view. */
export default function MapPage() {
  // A read-only screen: it fetches nodes, plots them and links out to bookings.
  const [stations, setStations] = useState<MapStation[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState<number>(0);

  /* ---- browser position ---- */
  const [geoStatus, setGeoStatus] = useState<GeoStatus>('pending');
  const [geoMessage, setGeoMessage] = useState<string | null>(null);
  const [userPosition, setUserPosition] = useState<[number, number] | null>(null);

  /* ---- controls ---- */
  const [radiusKm, setRadiusKm] = useState<number>(DEFAULT_RADIUS_KM);
  const [search, setSearch] = useState<string>('');
  const [selectedId, setSelectedId] = useState<string | null>(null);

  // Asks the load effect below to run again.
  function reload() {
    setReloadKey((current) => current + 1);
  }

  // Asks the browser for a position once, on mount. Permission is the user's to
  // give: a refusal is a normal outcome, not an error, and the screen simply
  // falls back to showing the whole network centred on Malabe.
  useEffect(() => {
    let cancelled = false;

    if (!('geolocation' in navigator)) {
      setGeoStatus('unavailable');
      setGeoMessage('This browser cannot share a location, so every node is listed.');
      return;
    }

    navigator.geolocation.getCurrentPosition(
      (position) => {
        if (cancelled) return;
        setUserPosition([position.coords.latitude, position.coords.longitude]);
        setGeoStatus('granted');
      },
      (positionError) => {
        if (cancelled) return;
        setGeoStatus('unavailable');
        setGeoMessage(
          positionError.message
            ? `Location unavailable (${positionError.message}). Showing every node.`
            : 'Location unavailable, so every node is listed.',
        );
      },
      { enableHighAccuracy: false, timeout: 10000, maximumAge: 300000 },
    );

    return () => {
      cancelled = true;
    };
  }, []);

  // Loads the nodes once the position question has been settled, so the screen
  // never fetches the same data twice. With a position the nearby endpoint is
  // used, because only that endpoint returns the server computed distanceKm.
  useEffect(() => {
    if (geoStatus === 'pending') return;

    let cancelled = false;

    async function loadStations() {
      setLoading(true);
      setError(null);

      try {
        if (userPosition) {
          // Radius filtering and distance ordering are both done by the API.
          const nearby = await stationsApi.nearby(
            userPosition[0],
            userPosition[1],
            radiusKm,
            MAX_NODES,
          );
          if (!cancelled) setStations(nearby);
        } else {
          const paged = await stationsApi.list({ pageSize: MAX_NODES });
          if (!cancelled) setStations(paged.items);
        }
      } catch (err) {
        if (!cancelled) {
          setStations([]);
          setError(describeError(err));
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void loadStations();
    return () => {
      cancelled = true;
    };
  }, [geoStatus, userPosition, radiusKm, reloadKey]);

  // Narrows the rows already on screen by name, code or city. This is a display
  // convenience over a list the API has already scoped - no rule is applied here.
  const visible = useMemo<MapStation[]>(() => {
    const query = search.trim().toLowerCase();
    if (query === '') return stations;
    return stations.filter(
      (station) =>
        station.name.toLowerCase().includes(query) ||
        station.stationCode.toLowerCase().includes(query) ||
        station.city.toLowerCase().includes(query),
    );
  }, [stations, search]);

  // The selected node, and the coordinates FlyTo should move the map to. The
  // tuple is memoised so the map is not re-flown on every unrelated re-render.
  const selectedStation = useMemo<MapStation | null>(
    () => stations.find((station) => station.id === selectedId) ?? null,
    [stations, selectedId],
  );

  const focusPosition = useMemo<[number, number] | null>(
    () => (selectedStation ? [selectedStation.latitude, selectedStation.longitude] : null),
    [selectedStation],
  );

  const mapCentre: [number, number] = userPosition ?? FALLBACK_CENTRE;

  const locationHint = userPosition
    ? 'Ordered by the distance the API calculates from your current position.'
    : (geoMessage ?? 'No position shared, so the map is centred on Malabe.');

  return (
    <div className="space-y-5">
      {/* ---- Page header ---- */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-xl font-semibold text-slate-900 sm:text-2xl">Microgrid node map</h1>
          <p className="mt-1 text-sm text-slate-500">
            Every solar node plotted on OpenStreetMap. Share your location to have the API list the
            nodes nearest to you first.
          </p>
        </div>
        <button type="button" className="btn-secondary" onClick={reload}>
          <RefreshCw className="h-4 w-4" aria-hidden="true" />
          Refresh
        </button>
      </div>

      {error && <ErrorState message={error} onRetry={reload} />}

      {loading && !error && (
        <div className="card">
          <PageLoader />
        </div>
      )}

      {!loading && !error && (
        <div className="flex flex-col gap-5 lg:flex-row lg:items-start">
          {/* ---- Left panel: controls and the node list ---- */}
          <div className="w-full space-y-5 lg:w-96 lg:shrink-0">
            <div className="card">
              <div className="card-body space-y-4">
                <div>
                  {/* SearchInput renders its own input element, so a plain caption
                      is used rather than a label pointing at an id it does not own. */}
                  <p className="label">Find a node</p>
                  <SearchInput value={search} onChange={setSearch} placeholder="Name, code or city" />
                </div>

                <div>
                  <label className="label" htmlFor="map-radius">
                    Search radius
                  </label>
                  <select
                    id="map-radius"
                    className="select"
                    value={radiusKm}
                    disabled={userPosition === null}
                    onChange={(event) => setRadiusKm(Number(event.target.value))}
                  >
                    {RADIUS_OPTIONS.map((option) => (
                      <option key={option} value={option}>
                        Within {option} km
                      </option>
                    ))}
                  </select>
                  <p className="mt-1.5 flex items-start gap-1.5 text-xs text-slate-500">
                    <Navigation className="mt-0.5 h-3 w-3 shrink-0" aria-hidden="true" />
                    <span>{locationHint}</span>
                  </p>
                </div>
              </div>
            </div>

            <div className="card">
              <div className="card-header">
                <h2 className="text-sm font-semibold text-slate-900">
                  Nodes ({visible.length})
                </h2>
                {selectedStation && (
                  <button
                    type="button"
                    className="btn-ghost btn-sm"
                    onClick={() => setSelectedId(null)}
                  >
                    Clear selection
                  </button>
                )}
              </div>

              {visible.length === 0 ? (
                <EmptyState
                  icon={MapPin}
                  title="No nodes to show"
                  message={
                    search.trim() !== ''
                      ? 'No node matches that name, code or city. Try a different term.'
                      : userPosition
                        ? 'The API found no node inside this radius. Choose a wider radius.'
                        : 'No microgrid node has been registered yet.'
                  }
                  action={
                    search.trim() !== '' ? (
                      <button type="button" className="btn-secondary" onClick={() => setSearch('')}>
                        Clear search
                      </button>
                    ) : undefined
                  }
                />
              ) : (
                <ul className="max-h-[440px] divide-y divide-slate-100 overflow-y-auto">
                  {visible.map((station) => {
                    // status is server state and the distance was calculated by the
                    // API; the row only decides how to draw them.
                    const isSelected = station.id === selectedId;
                    const distance = distanceLabel(station);

                    return (
                      <li key={station.id}>
                        <button
                          type="button"
                          onClick={() => setSelectedId(station.id)}
                          aria-pressed={isSelected}
                          className={`block w-full px-4 py-3 text-left transition-colors hover:bg-slate-50 ${
                            isSelected ? 'bg-solar-50 ring-2 ring-inset ring-solar-500' : ''
                          }`}
                        >
                          <div className="flex items-start justify-between gap-3">
                            <div className="min-w-0">
                              <p className="truncate text-sm font-semibold text-slate-900">
                                {station.name}
                              </p>
                              <p className="mt-0.5 font-mono text-xs text-slate-500">
                                {station.stationCode} - {station.city}
                              </p>
                            </div>
                            <Badge status={station.status} kind="station" />
                          </div>

                          <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-500">
                            <span>
                              {station.availableBatterySlots} / {station.totalBatterySlots} bays free
                            </span>
                            <span>{kwh(station.capacityKwh)}</span>
                            {distance && (
                              <span className="font-semibold text-solar-700">{distance}</span>
                            )}
                          </div>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}
            </div>
          </div>

          {/* ---- Map ----
              MapContainer sizes itself to its parent, and a parent with no height
              collapses it to a zero height box, so the wrapper below fixes 560px
              and the map is told to fill it. */}
          <div className="card min-w-0 flex-1 overflow-hidden">
            <div className="h-[560px]">
              <MapContainer
                center={mapCentre}
                zoom={userPosition ? 12 : 11}
                scrollWheelZoom
                style={{ height: '100%', width: '100%' }}
              >
                {/* OpenStreetMap tiles need no API key or billing account, so the
                    map renders on any machine. The Android client uses Google Maps. */}
                <TileLayer
                  url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                  attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
                />

                <FlyTo position={focusPosition} />

                {userPosition && (
                  <Marker position={userPosition} icon={USER_DOT}>
                    <Popup>
                      <span className="text-xs font-semibold text-slate-700">You are here</span>
                    </Popup>
                  </Marker>
                )}

                {visible.map((station) => {
                  const position: [number, number] = [station.latitude, station.longitude];
                  const distance = distanceLabel(station);

                  return (
                    <Marker
                      key={station.id}
                      position={position}
                      icon={station.status === 'Active' ? ACTIVE_PIN : INACTIVE_PIN}
                      eventHandlers={{ click: () => setSelectedId(station.id) }}
                    >
                      <Popup>
                        <div className="min-w-[200px] space-y-1">
                          <p className="font-mono text-xs font-medium text-slate-600">
                            {station.stationCode}
                          </p>
                          <p className="text-sm font-semibold text-slate-900">{station.name}</p>
                          <p className="text-xs text-slate-600">
                            {station.address}, {station.city}
                          </p>
                          <p className="flex items-center gap-1.5 text-xs text-slate-600">
                            <Zap className="h-3 w-3 text-solar-500" aria-hidden="true" />
                            {kwh(station.capacityKwh)} capacity
                          </p>
                          <p className="text-xs text-slate-600">
                            {station.availableBatterySlots} / {station.totalBatterySlots} bays free
                          </p>
                          {distance && (
                            <p className="text-xs font-semibold text-slate-700">{distance}</p>
                          )}
                          <div className="pt-1">
                            <Badge status={station.status} kind="station" />
                          </div>
                          <Link
                            to={`/reservations?stationId=${station.id}`}
                            className="mt-1 inline-block text-xs font-semibold text-solar-700 underline"
                          >
                            View bookings at this node
                          </Link>
                        </div>
                      </Popup>
                    </Marker>
                  );
                })}
              </MapContainer>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
