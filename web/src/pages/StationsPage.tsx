/**
 * =============================================================================
 * File        : src/pages/StationsPage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : Microgrid node (solar grid hub) management - the create, edit,
 *               schedule, availability, activate / deactivate and delete screen
 *               that the "station management" marking criterion asks for.
 *               Every rule stays on the server: rule R4 (a node holding future
 *               bookings may not be deactivated) is never pre-checked here, the
 *               API is simply called and its 409 wording is displayed.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

import { useEffect, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import {
  AlertTriangle,
  BatteryCharging,
  Building2,
  CalendarCheck,
  CalendarClock,
  Clock,
  LayoutGrid,
  MapPin,
  Pencil,
  Plus,
  Power,
  PowerOff,
  Trash2,
  Zap,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { stationsApi } from '../api/services';
import { ApiError } from '../api/client';
import { useAuth } from '../context/AuthContext';
import {
  Badge,
  ConfirmDialog,
  EmptyState,
  ErrorState,
  Modal,
  Pagination,
  SearchInput,
  Spinner,
  useToast,
} from '../components/ui';
import type { DaySchedule, Station, StationStatus, StationType } from '../types';

/** Nodes per page - a multiple of the 3 column grid so the last row stays full. */
const PAGE_SIZE = 9;

/** How long the search box waits before it asks the API again. */
const SEARCH_DEBOUNCE_MS = 350;

/** Day names for index 0..6, matching the API's dayOfWeek numbering. */
const DAY_NAMES: string[] = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
];

/** The three node classifications the contract allows. */
const STATION_TYPES: StationType[] = ['Solar', 'Hybrid', 'Battery'];

/** Malabe, the campus the seeded nodes sit around - a sensible map default. */
const DEFAULT_LATITUDE = '6.9061';
const DEFAULT_LONGITUDE = '79.9696';

/** Default trading window used for a brand new node. */
const DEFAULT_OPEN_TIME = '06:00';
const DEFAULT_CLOSE_TIME = '22:00';

/** Which dialog, if any, is currently on screen. */
type DialogKind = 'none' | 'create' | 'edit' | 'schedule' | 'availability' | 'toggle' | 'delete';

/** Every field of the create / edit form, held as strings while being typed. */
interface StationFormState {
  stationCode: string;
  name: string;
  type: StationType;
  address: string;
  city: string;
  latitude: string;
  longitude: string;
  capacityKwh: string;
  totalBatterySlots: string;
  availableBatterySlots: string;
  contactNumber: string;
  openTime: string;
  closeTime: string;
}

/** A blank form pre-filled with the Malabe coordinates and the 06:00-22:00 window. */
function emptyForm(): StationFormState {
  // A new node starts from the defaults the assignment scenario describes.
  return {
    stationCode: '',
    name: '',
    type: 'Solar',
    address: '',
    city: '',
    latitude: DEFAULT_LATITUDE,
    longitude: DEFAULT_LONGITUDE,
    capacityKwh: '',
    totalBatterySlots: '',
    availableBatterySlots: '',
    contactNumber: '',
    openTime: DEFAULT_OPEN_TIME,
    closeTime: DEFAULT_CLOSE_TIME,
  };
}

/** Copies a node from the API into the editable form shape. */
function formFromStation(station: Station): StationFormState {
  // Numbers become strings so a half typed value does not fight the input.
  return {
    stationCode: station.stationCode,
    name: station.name,
    type: station.type,
    address: station.address,
    city: station.city,
    latitude: String(station.latitude),
    longitude: String(station.longitude),
    capacityKwh: String(station.capacityKwh),
    totalBatterySlots: String(station.totalBatterySlots),
    availableBatterySlots: String(station.availableBatterySlots),
    contactNumber: station.contactNumber,
    openTime: station.operatingHours.openTime,
    closeTime: station.operatingHours.closeTime,
  };
}

/** Builds seven editable rows from whatever schedule the node already publishes. */
function scheduleRowsFrom(station: Station): DaySchedule[] {
  // A node whose stored schedule is short or out of order still gets all 7 days.
  return DAY_NAMES.map((dayName, index) => {
    const existing = station.weeklySchedule.find((day) => day.dayOfWeek === index);
    return {
      dayOfWeek: index,
      dayName,
      isOpen: existing ? existing.isOpen : true,
      openTime: existing ? existing.openTime : station.operatingHours.openTime,
      closeTime: existing ? existing.closeTime : station.operatingHours.closeTime,
    };
  });
}

/** Looks a validation message up by field name, tolerating the server's casing. */
function fieldErrorFor(errors: Record<string, string[]> | null, field: string): string | undefined {
  // The API may answer with "Name" or "name", so the match is case insensitive.
  if (!errors) return undefined;
  const key = Object.keys(errors).find((name) => name.toLowerCase() === field.toLowerCase());
  return key ? errors[key][0] : undefined;
}

/** Turns any thrown value into a sentence that can be shown to the user. */
function describeError(err: unknown): string {
  // An ApiError already carries the server's own wording, which is always better.
  return err instanceof ApiError ? err.message : 'Something went wrong';
}

/** Label, control and validation message for one form input. */
function FormField({
  label,
  htmlFor,
  error,
  children,
}: {
  label: string;
  htmlFor: string;
  error?: string;
  children: ReactNode;
}) {
  // Keeps every dialog field aligned without repeating the same three elements.
  return (
    <div>
      <label className="label" htmlFor={htmlFor}>
        {label}
      </label>
      {children}
      {error && <p className="field-error">{error}</p>}
    </div>
  );
}

/** One figure of the 2x2 strip on a node card. */
function StatChip({ icon: Icon, label, value }: { icon: LucideIcon; label: string; value: string }) {
  // Every number shown here is read straight from the node payload.
  return (
    <div className="rounded-lg bg-slate-50 px-3 py-2">
      <p className="flex items-center gap-1.5 text-xs font-medium text-slate-500">
        <Icon className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
        {label}
      </p>
      <p className="mt-0.5 text-sm font-bold text-slate-900">{value}</p>
    </div>
  );
}

/** Grey placeholder card shown while the first page of nodes is loading. */
function StationCardSkeleton() {
  // Mirrors the real card's height so the grid does not jump when data lands.
  return (
    <div className="card animate-pulse">
      <div className="card-header">
        <span className="h-4 w-20 rounded bg-slate-200" />
        <span className="h-5 w-16 rounded-full bg-slate-200" />
      </div>
      <div className="card-body space-y-4">
        <div className="space-y-2">
          <span className="block h-5 w-3/4 rounded bg-slate-200" />
          <span className="block h-4 w-2/3 rounded bg-slate-100" />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <span className="block h-12 rounded-lg bg-slate-100" />
          <span className="block h-12 rounded-lg bg-slate-100" />
          <span className="block h-12 rounded-lg bg-slate-100" />
          <span className="block h-12 rounded-lg bg-slate-100" />
        </div>
      </div>
    </div>
  );
}

/** The microgrid node management screen. */
export default function StationsPage() {
  // Backoffice owns the node register; a Grid Operator keeps the day to day
  // schedule and free bay count of the node they work at up to date.
  const { user, isBackoffice, isGridOperator } = useAuth();
  const toast = useToast();
  const canManageOperationsAt = (station: Station): boolean =>
    isBackoffice ||
    (isGridOperator &&
      Boolean(user?.assignedStationId) &&
      user?.assignedStationId === station.id);

  /* ---- list state ---- */
  const [stations, setStations] = useState<Station[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState<number>(1);
  const [totalPages, setTotalPages] = useState<number>(1);
  const [totalCount, setTotalCount] = useState<number>(0);
  const [reloadKey, setReloadKey] = useState<number>(0);

  /* ---- filters ---- */
  const [search, setSearch] = useState<string>('');
  const [debouncedSearch, setDebouncedSearch] = useState<string>('');
  const [statusFilter, setStatusFilter] = useState<'' | StationStatus>('');

  /* ---- dialog state ---- */
  const [dialog, setDialog] = useState<DialogKind>('none');
  const [selected, setSelected] = useState<Station | null>(null);
  const [saving, setSaving] = useState<boolean>(false);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]> | null>(null);
  const [form, setForm] = useState<StationFormState>(emptyForm);
  const [scheduleRows, setScheduleRows] = useState<DaySchedule[]>([]);
  const [scheduleOpenTime, setScheduleOpenTime] = useState<string>(DEFAULT_OPEN_TIME);
  const [scheduleCloseTime, setScheduleCloseTime] = useState<string>(DEFAULT_CLOSE_TIME);
  const [availabilityValue, setAvailabilityValue] = useState<string>('0');

  // Waits for typing to settle so one query is sent instead of one per keystroke.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setDebouncedSearch(search.trim());
      setPage(1);
    }, SEARCH_DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [search]);

  // Loads the current page of nodes; the API does all filtering and paging.
  useEffect(() => {
    let cancelled = false;

    async function load() {
      setLoading(true);
      setError(null);
      try {
        const result = await stationsApi.list({
          status: statusFilter,
          search: debouncedSearch,
          page,
          pageSize: PAGE_SIZE,
        });
        if (!cancelled) {
          setStations(result.items);
          setTotalPages(result.totalPages);
          setTotalCount(result.totalCount);
        }
      } catch (err) {
        if (!cancelled) {
          setStations([]);
          setError(describeError(err));
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, [statusFilter, debouncedSearch, page, reloadKey]);

  // Closes whichever dialog is open and clears its transient state.
  function closeDialog() {
    setDialog('none');
    setSelected(null);
    setFieldErrors(null);
    setSaving(false);
  }

  // Opens the blank "New node" form.
  function openCreate() {
    setSelected(null);
    setForm(emptyForm());
    setFieldErrors(null);
    setDialog('create');
  }

  // Opens the edit form pre-filled from the chosen node.
  function openEdit(station: Station) {
    setSelected(station);
    setForm(formFromStation(station));
    setFieldErrors(null);
    setDialog('edit');
  }

  // Opens the seven day schedule editor for the chosen node.
  function openSchedule(station: Station) {
    setSelected(station);
    setScheduleRows(scheduleRowsFrom(station));
    setScheduleOpenTime(station.operatingHours.openTime);
    setScheduleCloseTime(station.operatingHours.closeTime);
    setFieldErrors(null);
    setDialog('schedule');
  }

  // Opens the free bay counter for the chosen node.
  function openAvailability(station: Station) {
    setSelected(station);
    setAvailabilityValue(String(station.availableBatterySlots));
    setFieldErrors(null);
    setDialog('availability');
  }

  // Asks before flipping a node in or out of service.
  function openToggle(station: Station) {
    setSelected(station);
    setDialog('toggle');
  }

  // Asks before removing a node from the register.
  function openDelete(station: Station) {
    setSelected(station);
    setDialog('delete');
  }

  // Applies one edit to the create / edit form.
  function setFormField<K extends keyof StationFormState>(key: K, value: StationFormState[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  // Applies one edit to a single day of the schedule editor.
  function setScheduleField(dayOfWeek: number, patch: Partial<DaySchedule>) {
    setScheduleRows((current) =>
      current.map((row) => (row.dayOfWeek === dayOfWeek ? { ...row, ...patch } : row)),
    );
  }

  // Creates or updates a node. Validation is the API's job - anything it rejects
  // comes back as fieldErrors and is shown under the matching input.
  async function handleSubmitForm(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setFieldErrors(null);

    const payload: Record<string, unknown> = {
      name: form.name.trim(),
      type: form.type,
      address: form.address.trim(),
      city: form.city.trim(),
      latitude: Number(form.latitude),
      longitude: Number(form.longitude),
      capacityKwh: Number(form.capacityKwh),
      totalBatterySlots: Number(form.totalBatterySlots),
      availableBatterySlots: Number(form.availableBatterySlots),
      contactNumber: form.contactNumber.trim(),
      operatingHours: { openTime: form.openTime, closeTime: form.closeTime },
    };

    try {
      if (dialog === 'create') {
        const result = await stationsApi.create({
          ...payload,
          stationCode: form.stationCode.trim(),
          weeklySchedule: DAY_NAMES.map((dayName, index) => ({
            dayOfWeek: index,
            dayName,
            isOpen: true,
            openTime: form.openTime,
            closeTime: form.closeTime,
          })),
        });
        toast.success(result.message);
      } else if (selected) {
        const result = await stationsApi.update(selected.id, {
          ...payload,
          // The published week is edited from the Schedule dialog, so it is sent
          // back unchanged here.
          weeklySchedule: selected.weeklySchedule,
        });
        toast.success(result.message);
      }
      closeDialog();
      setReloadKey((key) => key + 1);
    } catch (err) {
      if (err instanceof ApiError) {
        setFieldErrors(err.fieldErrors);
      }
      toast.error(describeError(err));
      setSaving(false);
    }
  }

  // Replaces the published seven day schedule of the selected node.
  async function handleSubmitSchedule(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selected) return;
    setSaving(true);
    setFieldErrors(null);

    try {
      const result = await stationsApi.updateSchedule(selected.id, {
        operatingHours: { openTime: scheduleOpenTime, closeTime: scheduleCloseTime },
        weeklySchedule: scheduleRows,
      });
      toast.success(result.message);
      closeDialog();
      setReloadKey((key) => key + 1);
    } catch (err) {
      if (err instanceof ApiError) {
        setFieldErrors(err.fieldErrors);
      }
      toast.error(describeError(err));
      setSaving(false);
    }
  }

  // Updates the free battery bay count. Keeping this figure current is the grid
  // operator's day to day job in the assignment scenario, which is why the button
  // is offered to a Grid Operator as well as to the back office.
  async function handleSubmitAvailability(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!selected) return;
    setSaving(true);
    setFieldErrors(null);

    try {
      const result = await stationsApi.updateAvailability(selected.id, Number(availabilityValue));
      toast.success(result.message);
      closeDialog();
      setReloadKey((key) => key + 1);
    } catch (err) {
      if (err instanceof ApiError) {
        setFieldErrors(err.fieldErrors);
      }
      toast.error(describeError(err));
      setSaving(false);
    }
  }

  // Flips a node in or out of service.
  async function handleToggleStatus() {
    if (!selected) return;
    setSaving(true);

    try {
      // Rule R4 - a node holding future Pending or Approved bookings cannot be
      // deactivated - is enforced by the API. The UI deliberately does NOT check
      // activeReservationCount before calling: it asks, and reports the answer.
      const result =
        selected.status === 'Active'
          ? await stationsApi.deactivate(selected.id)
          : await stationsApi.activate(selected.id);
      toast.success(result.message);
      closeDialog();
      setReloadKey((key) => key + 1);
    } catch (err) {
      // A 409 carrying STATION_HAS_ACTIVE_RES lands here; the server's own wording
      // explains the rule far better than anything the browser could invent.
      toast.error(describeError(err));
      setSaving(false);
    }
  }

  // Deletes a node from the register.
  async function handleDelete() {
    if (!selected) return;
    setSaving(true);

    try {
      // Whether a node still has booking history is decided by the API, not here.
      const result = await stationsApi.remove(selected.id);
      toast.success(result.message);
      closeDialog();
      setReloadKey((key) => key + 1);
    } catch (err) {
      // Any 409 refusal is surfaced verbatim.
      toast.error(describeError(err));
      setSaving(false);
    }
  }

  const isFormDialog = dialog === 'create' || dialog === 'edit';

  return (
    <div className="space-y-6">
      {/* ---- Page header ---- */}
      <header className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold text-slate-900">Microgrid nodes</h1>
          <p className="mt-1 text-sm text-slate-500">
            Solar grid hubs, their capacity, battery bays and trading hours.
          </p>
        </div>
        {isBackoffice && (
          <button type="button" className="btn-primary" onClick={openCreate}>
            <Plus className="h-4 w-4" aria-hidden="true" />
            New node
          </button>
        )}
      </header>

      {/* ---- Filters ---- */}
      <div className="card">
        <div className="card-body grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <div className="sm:col-span-2 lg:col-span-3">
            <label className="label" htmlFor="station-search">
              Search
            </label>
            <SearchInput
              value={search}
              onChange={setSearch}
              placeholder="Search by code, name or city..."
            />
          </div>
          <div>
            <label className="label" htmlFor="station-status">
              Status
            </label>
            <select
              id="station-status"
              className="select"
              value={statusFilter}
              onChange={(event) => {
                setStatusFilter(event.target.value as '' | StationStatus);
                setPage(1);
              }}
            >
              <option value="">All statuses</option>
              <option value="Active">Active</option>
              <option value="Inactive">Inactive</option>
            </select>
          </div>
        </div>
      </div>

      {/* ---- Loading ---- */}
      {loading && (
        <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2, 3, 4, 5].map((key) => (
            <StationCardSkeleton key={key} />
          ))}
        </div>
      )}

      {/* ---- Error ---- */}
      {!loading && error && (
        <ErrorState message={error} onRetry={() => setReloadKey((key) => key + 1)} />
      )}

      {/* ---- Empty ---- */}
      {!loading && !error && stations.length === 0 && (
        <div className="card">
          <EmptyState
            icon={Building2}
            title="No microgrid nodes found"
            message={
              debouncedSearch || statusFilter
                ? 'No node matches the current search and status filter. Try widening them.'
                : 'The node register is empty. Add the first solar grid hub to get started.'
            }
            action={
              isBackoffice ? (
                <button type="button" className="btn-primary" onClick={openCreate}>
                  <Plus className="h-4 w-4" aria-hidden="true" />
                  New node
                </button>
              ) : undefined
            }
          />
        </div>
      )}

      {/* ---- The node grid ---- */}
      {!loading && !error && stations.length > 0 && (
        <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {stations.map((station) => (
            <article
              key={station.id}
              className="card flex flex-col transition-shadow hover:shadow-card-hover"
            >
              <div className="card-header">
                <span className="font-mono text-sm font-bold text-slate-900">
                  {station.stationCode}
                </span>
                {/* The status is a server verdict; the pill only renders it. */}
                <Badge status={station.status} kind="station" />
              </div>

              <div className="card-body flex flex-1 flex-col gap-4">
                <div>
                  <h2 className="text-base font-semibold leading-snug text-slate-900">
                    {station.name}
                  </h2>
                  <p className="mt-1.5 flex items-start gap-1.5 text-sm text-slate-500">
                    <MapPin className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" aria-hidden="true" />
                    <span>
                      {station.address}, {station.city}
                    </span>
                  </p>
                  <p className="mt-1 text-xs font-medium uppercase tracking-wide text-solar-700">
                    {station.type}
                  </p>
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <StatChip icon={Zap} label="Capacity" value={`${station.capacityKwh} kWh`} />
                  <StatChip
                    icon={LayoutGrid}
                    label="Total bays"
                    value={String(station.totalBatterySlots)}
                  />
                  <StatChip
                    icon={BatteryCharging}
                    label="Available bays"
                    value={String(station.availableBatterySlots)}
                  />
                  <StatChip
                    icon={CalendarCheck}
                    label="Active bookings"
                    value={String(station.activeReservationCount)}
                  />
                </div>

                <p className="flex items-center gap-1.5 text-sm text-slate-500">
                  <Clock className="h-4 w-4 shrink-0 text-slate-400" aria-hidden="true" />
                  Open {station.operatingHours.openTime} - {station.operatingHours.closeTime}
                </p>

                {station.activeReservationCount > 0 && (
                  /* A courtesy hint only. The deactivate button stays enabled because
                     rule R4 is the API's decision, not the browser's. */
                  <p className="flex items-start gap-1.5 rounded-lg bg-amber-50 px-3 py-2 text-xs font-medium text-amber-800">
                    <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                    <span>
                      {station.activeReservationCount} future booking
                      {station.activeReservationCount === 1 ? '' : 's'} held - the service may refuse
                      to deactivate this node.
                    </span>
                  </p>
                )}
              </div>

              <div className="flex flex-wrap items-center gap-2 border-t border-slate-200 px-5 py-3">
                {isBackoffice && (
                  <button
                    type="button"
                    className="btn-secondary btn-sm"
                    onClick={() => openEdit(station)}
                  >
                    <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                    Edit
                  </button>
                )}
                {canManageOperationsAt(station) && (
                  <button
                    type="button"
                    className="btn-secondary btn-sm"
                    onClick={() => openSchedule(station)}
                  >
                    <CalendarClock className="h-3.5 w-3.5" aria-hidden="true" />
                    Schedule
                  </button>
                )}
                {canManageOperationsAt(station) && (
                  <button
                    type="button"
                    className="btn-secondary btn-sm"
                    onClick={() => openAvailability(station)}
                  >
                    <BatteryCharging className="h-3.5 w-3.5" aria-hidden="true" />
                    Availability
                  </button>
                )}
                {isBackoffice && (
                  <button
                    type="button"
                    className={station.status === 'Active' ? 'btn-secondary btn-sm' : 'btn-success btn-sm'}
                    onClick={() => openToggle(station)}
                  >
                    {station.status === 'Active' ? (
                      <PowerOff className="h-3.5 w-3.5" aria-hidden="true" />
                    ) : (
                      <Power className="h-3.5 w-3.5" aria-hidden="true" />
                    )}
                    {station.status === 'Active' ? 'Deactivate' : 'Activate'}
                  </button>
                )}
                {isBackoffice && (
                  <button
                    type="button"
                    className="btn-danger btn-sm"
                    onClick={() => openDelete(station)}
                  >
                    <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                    Delete
                  </button>
                )}
              </div>
            </article>
          ))}
        </div>
      )}

      {/* ---- Pagination ---- */}
      {!loading && !error && totalPages > 1 && (
        <div className="card">
          <Pagination
            page={page}
            totalPages={totalPages}
            totalCount={totalCount}
            onChange={setPage}
          />
        </div>
      )}

      {/* ---- Create / edit dialog ---- */}
      <Modal
        open={isFormDialog}
        size="lg"
        title={dialog === 'create' ? 'New microgrid node' : `Edit ${selected?.stationCode ?? 'node'}`}
        onClose={saving ? () => undefined : closeDialog}
        footer={
          <>
            <button type="button" className="btn-secondary" onClick={closeDialog} disabled={saving}>
              Cancel
            </button>
            <button type="submit" form="station-form" className="btn-primary" disabled={saving}>
              {saving && <Spinner size="sm" />}
              {dialog === 'create' ? 'Create node' : 'Save changes'}
            </button>
          </>
        }
      >
        <form id="station-form" className="grid gap-4 sm:grid-cols-2" onSubmit={handleSubmitForm}>
          {dialog === 'create' && (
            <FormField
              label="Node code"
              htmlFor="field-stationCode"
              error={fieldErrorFor(fieldErrors, 'stationCode')}
            >
              <input
                id="field-stationCode"
                className={`input ${fieldErrorFor(fieldErrors, 'stationCode') ? 'input-error' : ''}`}
                value={form.stationCode}
                onChange={(event) => setFormField('stationCode', event.target.value)}
                placeholder="STN-007"
                required
              />
            </FormField>
          )}

          <FormField label="Name" htmlFor="field-name" error={fieldErrorFor(fieldErrors, 'name')}>
            <input
              id="field-name"
              className={`input ${fieldErrorFor(fieldErrors, 'name') ? 'input-error' : ''}`}
              value={form.name}
              onChange={(event) => setFormField('name', event.target.value)}
              placeholder="Malabe Solar Hub"
              required
            />
          </FormField>

          <FormField label="Type" htmlFor="field-type" error={fieldErrorFor(fieldErrors, 'type')}>
            <select
              id="field-type"
              className="select"
              value={form.type}
              onChange={(event) => setFormField('type', event.target.value as StationType)}
            >
              {STATION_TYPES.map((type) => (
                <option key={type} value={type}>
                  {type}
                </option>
              ))}
            </select>
          </FormField>

          <FormField
            label="Address"
            htmlFor="field-address"
            error={fieldErrorFor(fieldErrors, 'address')}
          >
            <input
              id="field-address"
              className={`input ${fieldErrorFor(fieldErrors, 'address') ? 'input-error' : ''}`}
              value={form.address}
              onChange={(event) => setFormField('address', event.target.value)}
              placeholder="New Kandy Road"
              required
            />
          </FormField>

          <FormField label="City" htmlFor="field-city" error={fieldErrorFor(fieldErrors, 'city')}>
            <input
              id="field-city"
              className={`input ${fieldErrorFor(fieldErrors, 'city') ? 'input-error' : ''}`}
              value={form.city}
              onChange={(event) => setFormField('city', event.target.value)}
              placeholder="Malabe"
              required
            />
          </FormField>

          <FormField
            label="Latitude"
            htmlFor="field-latitude"
            error={fieldErrorFor(fieldErrors, 'latitude')}
          >
            <input
              id="field-latitude"
              type="number"
              step="0.0001"
              className={`input ${fieldErrorFor(fieldErrors, 'latitude') ? 'input-error' : ''}`}
              value={form.latitude}
              onChange={(event) => setFormField('latitude', event.target.value)}
              required
            />
          </FormField>

          <FormField
            label="Longitude"
            htmlFor="field-longitude"
            error={fieldErrorFor(fieldErrors, 'longitude')}
          >
            <input
              id="field-longitude"
              type="number"
              step="0.0001"
              className={`input ${fieldErrorFor(fieldErrors, 'longitude') ? 'input-error' : ''}`}
              value={form.longitude}
              onChange={(event) => setFormField('longitude', event.target.value)}
              required
            />
          </FormField>

          <FormField
            label="Capacity (kWh)"
            htmlFor="field-capacityKwh"
            error={fieldErrorFor(fieldErrors, 'capacityKwh')}
          >
            <input
              id="field-capacityKwh"
              type="number"
              min="0"
              step="0.1"
              className={`input ${fieldErrorFor(fieldErrors, 'capacityKwh') ? 'input-error' : ''}`}
              value={form.capacityKwh}
              onChange={(event) => setFormField('capacityKwh', event.target.value)}
              required
            />
          </FormField>

          <FormField
            label="Contact number"
            htmlFor="field-contactNumber"
            error={fieldErrorFor(fieldErrors, 'contactNumber')}
          >
            <input
              id="field-contactNumber"
              className={`input ${fieldErrorFor(fieldErrors, 'contactNumber') ? 'input-error' : ''}`}
              value={form.contactNumber}
              onChange={(event) => setFormField('contactNumber', event.target.value)}
              placeholder="0112345678"
              required
            />
          </FormField>

          <FormField
            label="Total battery bays"
            htmlFor="field-totalBatterySlots"
            error={fieldErrorFor(fieldErrors, 'totalBatterySlots')}
          >
            <input
              id="field-totalBatterySlots"
              type="number"
              min="0"
              className={`input ${fieldErrorFor(fieldErrors, 'totalBatterySlots') ? 'input-error' : ''}`}
              value={form.totalBatterySlots}
              onChange={(event) => setFormField('totalBatterySlots', event.target.value)}
              required
            />
          </FormField>

          <FormField
            label="Available battery bays"
            htmlFor="field-availableBatterySlots"
            error={fieldErrorFor(fieldErrors, 'availableBatterySlots')}
          >
            <input
              id="field-availableBatterySlots"
              type="number"
              min="0"
              className={`input ${
                fieldErrorFor(fieldErrors, 'availableBatterySlots') ? 'input-error' : ''
              }`}
              value={form.availableBatterySlots}
              onChange={(event) => setFormField('availableBatterySlots', event.target.value)}
              required
            />
          </FormField>

          <FormField
            label="Opens at"
            htmlFor="field-openTime"
            error={fieldErrorFor(fieldErrors, 'openTime')}
          >
            <input
              id="field-openTime"
              type="time"
              className="input"
              value={form.openTime}
              onChange={(event) => setFormField('openTime', event.target.value)}
              required
            />
          </FormField>

          <FormField
            label="Closes at"
            htmlFor="field-closeTime"
            error={fieldErrorFor(fieldErrors, 'closeTime')}
          >
            <input
              id="field-closeTime"
              type="time"
              className="input"
              value={form.closeTime}
              onChange={(event) => setFormField('closeTime', event.target.value)}
              required
            />
          </FormField>
        </form>
      </Modal>

      {/* ---- Weekly schedule dialog ---- */}
      <Modal
        open={dialog === 'schedule'}
        size="lg"
        title={`Trading schedule - ${selected?.stationCode ?? ''}`}
        onClose={saving ? () => undefined : closeDialog}
        footer={
          <>
            <button type="button" className="btn-secondary" onClick={closeDialog} disabled={saving}>
              Cancel
            </button>
            <button type="submit" form="schedule-form" className="btn-primary" disabled={saving}>
              {saving && <Spinner size="sm" />}
              Save schedule
            </button>
          </>
        }
      >
        <form id="schedule-form" className="space-y-5" onSubmit={handleSubmitSchedule}>
          <p className="text-sm text-slate-500">
            Bookings are refused by the service on a day marked closed, so this schedule is what
            prosumers can actually trade against.
          </p>

          <div className="grid gap-4 sm:grid-cols-2">
            <FormField label="Default opening time" htmlFor="schedule-openTime">
              <input
                id="schedule-openTime"
                type="time"
                className="input"
                value={scheduleOpenTime}
                onChange={(event) => setScheduleOpenTime(event.target.value)}
                required
              />
            </FormField>
            <FormField label="Default closing time" htmlFor="schedule-closeTime">
              <input
                id="schedule-closeTime"
                type="time"
                className="input"
                value={scheduleCloseTime}
                onChange={(event) => setScheduleCloseTime(event.target.value)}
                required
              />
            </FormField>
          </div>

          <div className="space-y-2">
            {scheduleRows.map((row) => (
              <div
                key={row.dayOfWeek}
                className="grid items-center gap-3 rounded-lg border border-slate-200 px-3 py-2.5 sm:grid-cols-[10rem_1fr_1fr]"
              >
                <label className="flex items-center gap-2 text-sm font-medium text-slate-700">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-slate-300 text-solar-500 focus:ring-solar-500"
                    checked={row.isOpen}
                    onChange={(event) =>
                      setScheduleField(row.dayOfWeek, { isOpen: event.target.checked })
                    }
                  />
                  {row.dayName}
                </label>
                <div>
                  <label className="sr-only" htmlFor={`open-${row.dayOfWeek}`}>
                    {row.dayName} opening time
                  </label>
                  <input
                    id={`open-${row.dayOfWeek}`}
                    type="time"
                    className="input"
                    value={row.openTime}
                    disabled={!row.isOpen}
                    onChange={(event) =>
                      setScheduleField(row.dayOfWeek, { openTime: event.target.value })
                    }
                  />
                </div>
                <div>
                  <label className="sr-only" htmlFor={`close-${row.dayOfWeek}`}>
                    {row.dayName} closing time
                  </label>
                  <input
                    id={`close-${row.dayOfWeek}`}
                    type="time"
                    className="input"
                    value={row.closeTime}
                    disabled={!row.isOpen}
                    onChange={(event) =>
                      setScheduleField(row.dayOfWeek, { closeTime: event.target.value })
                    }
                  />
                </div>
              </div>
            ))}
          </div>

          {fieldErrorFor(fieldErrors, 'weeklySchedule') && (
            <p className="field-error">{fieldErrorFor(fieldErrors, 'weeklySchedule')}</p>
          )}
        </form>
      </Modal>

      {/* ---- Availability dialog ---- */}
      <Modal
        open={dialog === 'availability'}
        size="sm"
        title={`Battery bays - ${selected?.stationCode ?? ''}`}
        onClose={saving ? () => undefined : closeDialog}
        footer={
          <>
            <button type="button" className="btn-secondary" onClick={closeDialog} disabled={saving}>
              Cancel
            </button>
            <button type="submit" form="availability-form" className="btn-primary" disabled={saving}>
              {saving && <Spinner size="sm" />}
              Update
            </button>
          </>
        }
      >
        {/* Keeping the free bay count current is the grid operator's day to day job
            in the assignment scenario, so this dialog is deliberately tiny. */}
        <form id="availability-form" className="space-y-4" onSubmit={handleSubmitAvailability}>
          <p className="text-sm text-slate-500">
            {selected ? `${selected.totalBatterySlots} bays are installed at this node.` : ''}
          </p>
          <FormField
            label="Available battery bays"
            htmlFor="availability-value"
            error={fieldErrorFor(fieldErrors, 'availableBatterySlots')}
          >
            <input
              id="availability-value"
              type="number"
              min="0"
              className={`input ${
                fieldErrorFor(fieldErrors, 'availableBatterySlots') ? 'input-error' : ''
              }`}
              value={availabilityValue}
              onChange={(event) => setAvailabilityValue(event.target.value)}
              required
            />
          </FormField>
        </form>
      </Modal>

      {/* ---- Activate / deactivate confirmation ---- */}
      <ConfirmDialog
        open={dialog === 'toggle'}
        title={selected?.status === 'Active' ? 'Deactivate node' : 'Activate node'}
        message={
          selected?.status === 'Active'
            ? `Take ${selected?.name ?? 'this node'} out of service? The service refuses this while the node still holds future bookings, and its answer will be shown here.`
            : `Return ${selected?.name ?? 'this node'} to service so prosumers can trade energy at it again?`
        }
        confirmLabel={selected?.status === 'Active' ? 'Deactivate' : 'Activate'}
        tone={selected?.status === 'Active' ? 'danger' : 'primary'}
        busy={saving}
        onConfirm={handleToggleStatus}
        onCancel={closeDialog}
      />

      {/* ---- Delete confirmation ---- */}
      <ConfirmDialog
        open={dialog === 'delete'}
        title="Delete node"
        message={`Permanently remove ${selected?.name ?? 'this node'} from the register? The service blocks this if any reservation was ever made here.`}
        confirmLabel="Delete"
        tone="danger"
        busy={saving}
        onConfirm={handleDelete}
        onCancel={closeDialog}
      />
    </div>
  );
}
