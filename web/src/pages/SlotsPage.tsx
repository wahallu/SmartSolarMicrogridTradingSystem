/**
 * =============================================================================
 * File        : src/pages/SlotsPage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : Energy booking slot management for Backoffice officers and Grid
 *               Operators. Lists a node's slots for a chosen day with server
 *               side filters, and offers create, bulk generate, edit, block and
 *               delete. This is the "manage booking slots" marking requirement.
 *               Availability (rule R8) and the delete guard are decided by the
 *               C# API - this screen only renders and obeys those verdicts.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Ban, CalendarDays, Check, Layers, Minus, Pencil, Plus, Trash2 } from 'lucide-react';
import { slotsApi, stationsApi } from '../api/services';
import { ApiError } from '../api/client';
import { useAuth } from '../context/AuthContext';
import {
  ConfirmDialog,
  EmptyState,
  ErrorState,
  Modal,
  PageLoader,
  Pagination,
  Spinner,
  useToast,
} from '../components/ui';
import { daysFromToday, formatDate, slotTypeLabel, today } from '../utils/format';
import type {
  GenerateSlotsResult,
  PagedResult,
  Slot,
  SlotStatus,
  SlotType,
  Station,
} from '../types';

/** How many slot rows the list requests per page. */
const PAGE_SIZE = 20;

/** The slot states the filter and the edit form offer. */
const STATUS_OPTIONS: SlotStatus[] = ['Available', 'Full', 'Blocked'];

/** The two directions of energy transfer a slot can be published for. */
const TYPE_OPTIONS: SlotType[] = ['Charging', 'DropOff'];

/** Shape of the create / edit slot form while it is being filled in. */
interface SlotFormState {
  slotDate: string;
  startTime: string;
  endTime: string;
  slotType: SlotType;
  energyCapacityKwh: string;
  batterySlotNumber: string;
  maxBookings: string;
  status: SlotStatus;
}

/** Shape of the bulk generation form. */
interface GenerateFormState {
  fromDate: string;
  toDate: string;
  startHour: string;
  endHour: string;
  slotDurationHours: string;
  slotType: SlotType;
  energyCapacityKwh: string;
  batterySlots: string;
  skipClosedDays: boolean;
}

/** Builds the blank create form, defaulted to the day currently being viewed. */
function emptySlotForm(date: string): SlotFormState {
  // Publishing a slot for the day already on screen is by far the common case.
  return {
    slotDate: date === '' ? today() : date,
    startTime: '08:00',
    endTime: '10:00',
    slotType: 'Charging',
    energyCapacityKwh: '25',
    batterySlotNumber: '1',
    maxBookings: '1',
    status: 'Available',
  };
}

/** Builds the blank generation form covering the coming week. */
function emptyGenerateForm(): GenerateFormState {
  // Seven days matches the booking window the API accepts reservations inside.
  return {
    fromDate: today(),
    toDate: daysFromToday(6),
    startHour: '8',
    endHour: '18',
    slotDurationHours: '2',
    slotType: 'Charging',
    energyCapacityKwh: '25',
    batterySlots: '2',
    skipClosedDays: true,
  };
}

/** Tailwind pill class for a slot status: green, amber or slate. */
function slotBadgeClass(status: SlotStatus | string): string {
  // Presentation only - the status itself is calculated and stored by the API.
  switch (status) {
    case 'Available':
      return 'badge-active';
    case 'Full':
      return 'badge-pending';
    default:
      return 'badge-cancelled';
  }
}

/** Picks one message out of the API's field error map, ignoring key casing. */
function fieldError(errors: Record<string, string[]> | null, field: string): string | undefined {
  // The C# model binder capitalises property names, so the lookup is case blind.
  if (!errors) return undefined;
  const key = Object.keys(errors).find((name) => name.toLowerCase() === field.toLowerCase());
  return key && errors[key].length > 0 ? errors[key][0] : undefined;
}

/** Turns any thrown value into the sentence a user should read. */
function describeError(err: unknown): string {
  // An ApiError already carries the API's own wording, which is always preferred.
  return err instanceof ApiError ? err.message : 'Something went wrong';
}

/** The slot management screen: filters, table and the five slot actions. */
export default function SlotsPage() {
  // Grid Operators are pinned to their own node; Backoffice officers pick any.
  const toast = useToast();
  const { user, isGridOperator } = useAuth();
  const assignedStationId: string = user?.assignedStationId ?? '';

  /* ---- lookup data ---- */
  const [stations, setStations] = useState<Station[]>([]);
  const [stationsLoading, setStationsLoading] = useState<boolean>(true);
  const [stationsError, setStationsError] = useState<string | null>(null);

  /* ---- filter state ---- */
  const [stationId, setStationId] = useState<string>('');
  const [date, setDate] = useState<string>(today());
  const [status, setStatus] = useState<string>('');
  const [slotType, setSlotType] = useState<string>('');
  const [page, setPage] = useState<number>(1);

  /* ---- list state ---- */
  const [result, setResult] = useState<PagedResult<Slot> | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState<number>(0);

  /* ---- row action state ---- */
  const [busyId, setBusyId] = useState<string | null>(null);
  const [blockTarget, setBlockTarget] = useState<Slot | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Slot | null>(null);

  /* ---- create dialog state ---- */
  const [createOpen, setCreateOpen] = useState<boolean>(false);
  const [createForm, setCreateForm] = useState<SlotFormState>(() => emptySlotForm(today()));
  const [createError, setCreateError] = useState<string | null>(null);
  const [createFieldErrors, setCreateFieldErrors] = useState<Record<string, string[]> | null>(null);
  const [creating, setCreating] = useState<boolean>(false);

  /* ---- edit dialog state ---- */
  const [editTarget, setEditTarget] = useState<Slot | null>(null);
  const [editForm, setEditForm] = useState<SlotFormState>(() => emptySlotForm(today()));
  const [editError, setEditError] = useState<string | null>(null);
  const [editFieldErrors, setEditFieldErrors] = useState<Record<string, string[]> | null>(null);
  const [saving, setSaving] = useState<boolean>(false);

  /* ---- generate dialog state ---- */
  const [generateOpen, setGenerateOpen] = useState<boolean>(false);
  const [generateForm, setGenerateForm] = useState<GenerateFormState>(emptyGenerateForm);
  const [generateError, setGenerateError] = useState<string | null>(null);
  const [generating, setGenerating] = useState<boolean>(false);
  const [generateResult, setGenerateResult] = useState<GenerateSlotsResult | null>(null);

  // Asks the list effect below to run again after an action changed a row.
  function reload() {
    setReloadKey((current) => current + 1);
  }

  // A Grid Operator receives only their assigned node. Backoffice officers keep
  // the full selector because they are allowed to manage every node.
  useEffect(() => {
    let cancelled = false;

    async function loadStations() {
      setStationsLoading(true);
      setStationsError(null);

      try {
        if (isGridOperator) {
          if (assignedStationId === '') {
            setStations([]);
            setStationId('');
            setStationsError('This Grid Operator account is not assigned to a microgrid node.');
            return;
          }

          const assignedStation = await stationsApi.get(assignedStationId);
          if (cancelled) return;

          setStations([assignedStation]);
          setStationId(assignedStation.id);
          return;
        }

        const paged = await stationsApi.list({ pageSize: 100 });
        if (cancelled) return;

        setStations(paged.items);
        const fallback = paged.items.length > 0 ? paged.items[0].id : '';
        setStationId((current) =>
          paged.items.some((station) => station.id === current) ? current : fallback,
        );
      } catch (err) {
        if (!cancelled) {
          setStations([]);
          setStationId('');
          setStationsError(describeError(err));
        }
      } finally {
        if (!cancelled) setStationsLoading(false);
      }
    }

    void loadStations();
    return () => {
      cancelled = true;
    };
  }, [assignedStationId, isGridOperator]);

  // Re-runs whenever a filter or the page changes. The cancelled guard stops a
  // slow reply from writing into a component that has already been unmounted.
  useEffect(() => {
    if (stationId === '') {
      setResult(null);
      setLoading(false);
      return;
    }

    let cancelled = false;

    async function loadSlots(node: string) {
      setLoading(true);
      setError(null);

      try {
        // Every filter is applied by the API; the browser never sifts rows itself.
        const paged = await slotsApi.list({
          stationId: node,
          date,
          status,
          slotType,
          page,
          pageSize: PAGE_SIZE,
        });
        if (!cancelled) setResult(paged);
      } catch (err) {
        if (!cancelled) {
          setResult(null);
          setError(describeError(err));
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void loadSlots(stationId);
    return () => {
      cancelled = true;
    };
  }, [stationId, date, status, slotType, page, reloadKey]);

  // Opens the create dialog with a form defaulted to the day on screen.
  function openCreate() {
    setCreateForm(emptySlotForm(date));
    setCreateError(null);
    setCreateFieldErrors(null);
    setCreateOpen(true);
  }

  // Closes and resets the create dialog.
  function closeCreate() {
    setCreateOpen(false);
    setCreateError(null);
    setCreateFieldErrors(null);
  }

  // Publishes one slot. Overlaps and duplicate slot codes are rejected by the
  // API, whose message is shown verbatim rather than guessed at here.
  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setCreating(true);
    setCreateError(null);
    setCreateFieldErrors(null);

    try {
      const { message } = await slotsApi.create({
        stationId,
        slotDate: createForm.slotDate,
        startTime: createForm.startTime,
        endTime: createForm.endTime,
        slotType: createForm.slotType,
        energyCapacityKwh: Number(createForm.energyCapacityKwh),
        batterySlotNumber: Number(createForm.batterySlotNumber),
        maxBookings: Number(createForm.maxBookings),
      });
      toast.success(message);
      closeCreate();
      setPage(1);
      reload();
    } catch (err) {
      setCreateError(describeError(err));
      setCreateFieldErrors(err instanceof ApiError ? err.fieldErrors : null);
    } finally {
      setCreating(false);
    }
  }

  // Opens the edit dialog pre-filled from the row the operator clicked.
  function openEdit(slot: Slot) {
    setEditTarget(slot);
    setEditForm({
      slotDate: slot.slotDate.slice(0, 10),
      startTime: slot.startTime,
      endTime: slot.endTime,
      slotType: slot.slotType,
      energyCapacityKwh: String(slot.energyCapacityKwh),
      batterySlotNumber: String(slot.batterySlotNumber),
      maxBookings: String(slot.maxBookings),
      status: slot.status,
    });
    setEditError(null);
    setEditFieldErrors(null);
  }

  // Closes and resets the edit dialog.
  function closeEdit() {
    setEditTarget(null);
    setEditError(null);
    setEditFieldErrors(null);
  }

  // Saves an edited slot. Lowering maxBookings below the bookings already taken
  // is refused by the API - that check belongs to the service layer, not here.
  async function handleEdit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editTarget) return;

    setSaving(true);
    setEditError(null);
    setEditFieldErrors(null);

    try {
      const { message } = await slotsApi.update(editTarget.id, {
        startTime: editForm.startTime,
        endTime: editForm.endTime,
        slotType: editForm.slotType,
        energyCapacityKwh: Number(editForm.energyCapacityKwh),
        batterySlotNumber: Number(editForm.batterySlotNumber),
        maxBookings: Number(editForm.maxBookings),
        status: editForm.status,
      });
      toast.success(message);
      closeEdit();
      reload();
    } catch (err) {
      setEditError(describeError(err));
      setEditFieldErrors(err instanceof ApiError ? err.fieldErrors : null);
    } finally {
      setSaving(false);
    }
  }

  // Blocks a slot. The API stops new bookings on it and leaves the reservations
  // that were already accepted untouched.
  async function handleBlock() {
    if (!blockTarget) return;

    setBusyId(blockTarget.id);
    try {
      const { message } = await slotsApi.block(blockTarget.id);
      toast.success(message);
      setBlockTarget(null);
      reload();
    } catch (err) {
      toast.error(describeError(err));
      setBlockTarget(null);
    } finally {
      setBusyId(null);
    }
  }

  // Deletes a slot. The screen never pre-checks bookedCount: it simply asks and
  // displays the API's 409 SLOT_HAS_BOOKINGS wording when the rule refuses.
  async function handleDelete() {
    if (!deleteTarget) return;

    setBusyId(deleteTarget.id);
    try {
      const { message } = await slotsApi.remove(deleteTarget.id);
      toast.success(message);
      setDeleteTarget(null);
      reload();
    } catch (err) {
      toast.error(describeError(err));
      setDeleteTarget(null);
    } finally {
      setBusyId(null);
    }
  }

  // Opens the bulk generation dialog with a fresh week long form.
  function openGenerate() {
    setGenerateForm(emptyGenerateForm());
    setGenerateError(null);
    setGenerateResult(null);
    setGenerateOpen(true);
  }

  // Closes the generation dialog and clears its result panel.
  function closeGenerate() {
    setGenerateOpen(false);
    setGenerateError(null);
    setGenerateResult(null);
  }

  // Publishes a whole date range in one call. This is what saves an operator
  // creating dozens of slots one at a time. The API decides which windows are
  // skipped - closed days, clashes with existing slots - and reports the counts.
  async function handleGenerate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setGenerating(true);
    setGenerateError(null);
    setGenerateResult(null);

    try {
      const { data } = await slotsApi.generate({
        stationId,
        fromDate: generateForm.fromDate,
        toDate: generateForm.toDate,
        startHour: Number(generateForm.startHour),
        endHour: Number(generateForm.endHour),
        slotDurationHours: Number(generateForm.slotDurationHours),
        slotType: generateForm.slotType,
        energyCapacityKwh: Number(generateForm.energyCapacityKwh),
        batterySlots: Number(generateForm.batterySlots),
        skipClosedDays: generateForm.skipClosedDays,
      });
      setGenerateResult(data);
      toast.success(`${data.created} slot(s) published, ${data.skipped} skipped.`);
      setPage(1);
      reload();
    } catch (err) {
      setGenerateError(describeError(err));
    } finally {
      setGenerating(false);
    }
  }

  const rows: Slot[] = result?.items ?? [];
  const selectedStation: Station | undefined = stations.find(
    (station) => station.id === stationId,
  );
  const hasFilters = status !== '' || slotType !== '';
  const actionsDisabled = stationId === '';

  return (
    <div className="space-y-5">
      {/* ---- Page header ---- */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-xl font-bold text-slate-900 sm:text-2xl">Booking slots</h1>
          <p className="mt-1 text-sm text-slate-500">
            Publish and maintain the energy windows prosumers can reserve at each microgrid node.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            className="btn-secondary"
            onClick={openGenerate}
            disabled={actionsDisabled}
          >
            <Layers className="h-4 w-4" aria-hidden="true" />
            Generate slots
          </button>
          <button
            type="button"
            className="btn-primary"
            onClick={openCreate}
            disabled={actionsDisabled}
          >
            <Plus className="h-4 w-4" aria-hidden="true" />
            Add slot
          </button>
        </div>
      </div>

      {/* ---- Control bar ---- */}
      <div className="card">
        <div className="card-body grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <div>
            <label className="label" htmlFor="slot-station">
              Microgrid node
            </label>
            <select
              id="slot-station"
              className="select"
              value={stationId}
              disabled={stationsLoading || stations.length === 0 || isGridOperator}
              onChange={(event) => {
                setStationId(event.target.value);
                setPage(1);
              }}
            >
              <option value="">
                {stationsLoading ? 'Loading nodes...' : 'Select a node'}
              </option>
              {stations.map((station) => (
                <option key={station.id} value={station.id}>
                  {station.stationCode} - {station.name}
                </option>
              ))}
            </select>
            {stationsError && <p className="field-error">{stationsError}</p>}
          </div>

          <div>
            <label className="label" htmlFor="slot-date">
              Date
            </label>
            <input
              id="slot-date"
              type="date"
              className="input"
              value={date}
              onChange={(event) => {
                setDate(event.target.value);
                setPage(1);
              }}
            />
          </div>

          <div>
            <label className="label" htmlFor="slot-status">
              Status
            </label>
            <select
              id="slot-status"
              className="select"
              value={status}
              onChange={(event) => {
                setStatus(event.target.value);
                setPage(1);
              }}
            >
              <option value="">All statuses</option>
              {STATUS_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="label" htmlFor="slot-type">
              Slot type
            </label>
            <select
              id="slot-type"
              className="select"
              value={slotType}
              onChange={(event) => {
                setSlotType(event.target.value);
                setPage(1);
              }}
            >
              <option value="">All types</option>
              {TYPE_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {slotTypeLabel(option)}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="flex flex-col items-start justify-between gap-3 border-t border-slate-200 px-5 py-3 sm:flex-row sm:items-center">
          <p className="text-xs font-medium text-slate-500">
            {selectedStation
              ? `${selectedStation.stationCode} - ${selectedStation.name}, ${formatDate(date)}`
              : 'Choose a node to see its slots.'}
            {result ? ` - ${result.totalCount} slot(s)` : ''}
          </p>
          <button
            type="button"
            className="btn-secondary btn-sm"
            onClick={() => {
              setDate(today());
              setStatus('');
              setSlotType('');
              setPage(1);
            }}
          >
            Reset to today
          </button>
        </div>
      </div>

      {/* ---- Results ---- */}
      {error && <ErrorState message={error} onRetry={reload} />}

      {!error && (
        <div className="card">
          {(stationsLoading || loading) && <PageLoader />}

          {!stationsLoading && !loading && stations.length === 0 && (
            <EmptyState
              icon={CalendarDays}
              title="No microgrid nodes yet"
              message="Register a node first - booking slots always belong to a node."
            />
          )}

          {!stationsLoading && !loading && stations.length > 0 && rows.length === 0 && (
            <EmptyState
              icon={CalendarDays}
              title="No slots for this day"
              message={
                hasFilters
                  ? 'No slot matches the current status and type filters. Try clearing them or picking another date.'
                  : 'Nothing is published for this node on this date yet. Add a single slot or generate a whole week at once.'
              }
              action={
                <button
                  type="button"
                  className="btn-primary"
                  onClick={openGenerate}
                  disabled={actionsDisabled}
                >
                  <Layers className="h-4 w-4" aria-hidden="true" />
                  Generate slots
                </button>
              }
            />
          )}

          {!stationsLoading && !loading && rows.length > 0 && (
            <>
              <div className="overflow-x-auto">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Slot</th>
                      <th>Time</th>
                      <th>Type</th>
                      <th>Bay</th>
                      <th>Capacity kWh</th>
                      <th>Booked</th>
                      <th>Status</th>
                      <th>Bookable</th>
                      <th className="text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((slot) => {
                      const busy = busyId === slot.id;

                      return (
                        <tr key={slot.id}>
                          <td className="whitespace-nowrap font-semibold text-slate-900">
                            {slot.slotCode}
                            <span className="ml-2 text-xs font-normal text-slate-500">
                              {formatDate(slot.slotDate)}
                            </span>
                          </td>
                          <td className="whitespace-nowrap">
                            {slot.startTime} - {slot.endTime}
                          </td>
                          <td className="whitespace-nowrap">{slotTypeLabel(slot.slotType)}</td>
                          <td className="whitespace-nowrap">Bay {slot.batterySlotNumber}</td>
                          <td className="whitespace-nowrap">
                            {slot.energyCapacityKwh.toFixed(1)}
                          </td>
                          <td className="whitespace-nowrap">
                            {slot.bookedCount} / {slot.maxBookings}
                          </td>
                          <td>
                            <span className={slotBadgeClass(slot.status)}>{slot.status}</span>
                          </td>
                          <td>
                            {/* isBookable is the SERVER's rule R8 verdict. The UI only
                                renders it - it never works out capacity, status or
                                node state for itself. */}
                            {slot.isBookable ? (
                              <Check
                                className="h-4 w-4 text-grid-600"
                                aria-label="Bookable"
                              />
                            ) : (
                              <Minus
                                className="h-4 w-4 text-slate-300"
                                aria-label="Not bookable"
                              />
                            )}
                          </td>
                          <td>
                            <div className="flex flex-wrap items-center justify-end gap-2">
                              <button
                                type="button"
                                className="btn-secondary btn-sm"
                                onClick={() => openEdit(slot)}
                                disabled={busy}
                              >
                                <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                                Edit
                              </button>

                              <button
                                type="button"
                                className="btn-secondary btn-sm"
                                onClick={() => setBlockTarget(slot)}
                                disabled={busy || slot.status === 'Blocked'}
                              >
                                <Ban className="h-3.5 w-3.5" aria-hidden="true" />
                                Block
                              </button>

                              <button
                                type="button"
                                className="btn-danger btn-sm"
                                onClick={() => setDeleteTarget(slot)}
                                disabled={busy}
                              >
                                {busy ? (
                                  <Spinner size="sm" />
                                ) : (
                                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                                )}
                                Delete
                              </button>
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>

              {result && (
                <Pagination
                  page={result.page}
                  totalPages={result.totalPages}
                  totalCount={result.totalCount}
                  onChange={setPage}
                />
              )}
            </>
          )}
        </div>
      )}

      {/* ---- Add slot ---- */}
      <Modal
        open={createOpen}
        title="Add booking slot"
        onClose={closeCreate}
        footer={
          <>
            <button type="button" className="btn-secondary" onClick={closeCreate} disabled={creating}>
              Cancel
            </button>
            <button type="submit" form="create-slot-form" className="btn-primary" disabled={creating}>
              {creating && <Spinner size="sm" />}
              Publish slot
            </button>
          </>
        }
      >
        <form id="create-slot-form" onSubmit={(event) => void handleCreate(event)}>
          {createError && (
            <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-800">
              {createError}
            </div>
          )}

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <label className="label" htmlFor="create-slot-date">
                Date
              </label>
              <input
                id="create-slot-date"
                type="date"
                className="input"
                required
                value={createForm.slotDate}
                onChange={(event) =>
                  setCreateForm({ ...createForm, slotDate: event.target.value })
                }
              />
              {fieldError(createFieldErrors, 'slotDate') && (
                <p className="field-error">{fieldError(createFieldErrors, 'slotDate')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="create-slot-start">
                Start time
              </label>
              <input
                id="create-slot-start"
                type="time"
                className="input"
                required
                value={createForm.startTime}
                onChange={(event) =>
                  setCreateForm({ ...createForm, startTime: event.target.value })
                }
              />
              {fieldError(createFieldErrors, 'startTime') && (
                <p className="field-error">{fieldError(createFieldErrors, 'startTime')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="create-slot-end">
                End time
              </label>
              <input
                id="create-slot-end"
                type="time"
                className="input"
                required
                value={createForm.endTime}
                onChange={(event) => setCreateForm({ ...createForm, endTime: event.target.value })}
              />
              {fieldError(createFieldErrors, 'endTime') && (
                <p className="field-error">{fieldError(createFieldErrors, 'endTime')}</p>
              )}
            </div>

            <div className="sm:col-span-2">
              <label className="label" htmlFor="create-slot-kind">
                Slot type
              </label>
              <select
                id="create-slot-kind"
                className="select"
                value={createForm.slotType}
                onChange={(event) =>
                  setCreateForm({ ...createForm, slotType: event.target.value as SlotType })
                }
              >
                {TYPE_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {slotTypeLabel(option)}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="label" htmlFor="create-slot-capacity">
                Energy capacity (kWh)
              </label>
              <input
                id="create-slot-capacity"
                type="number"
                min="0.1"
                step="0.1"
                className="input"
                required
                value={createForm.energyCapacityKwh}
                onChange={(event) =>
                  setCreateForm({ ...createForm, energyCapacityKwh: event.target.value })
                }
              />
              {fieldError(createFieldErrors, 'energyCapacityKwh') && (
                <p className="field-error">{fieldError(createFieldErrors, 'energyCapacityKwh')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="create-slot-bay">
                Battery bay number
              </label>
              <input
                id="create-slot-bay"
                type="number"
                min="1"
                step="1"
                className="input"
                required
                value={createForm.batterySlotNumber}
                onChange={(event) =>
                  setCreateForm({ ...createForm, batterySlotNumber: event.target.value })
                }
              />
              {fieldError(createFieldErrors, 'batterySlotNumber') && (
                <p className="field-error">{fieldError(createFieldErrors, 'batterySlotNumber')}</p>
              )}
            </div>

            <div className="sm:col-span-2">
              <label className="label" htmlFor="create-slot-max">
                Maximum bookings
              </label>
              <input
                id="create-slot-max"
                type="number"
                min="1"
                step="1"
                className="input"
                required
                value={createForm.maxBookings}
                onChange={(event) =>
                  setCreateForm({ ...createForm, maxBookings: event.target.value })
                }
              />
              {fieldError(createFieldErrors, 'maxBookings') && (
                <p className="field-error">{fieldError(createFieldErrors, 'maxBookings')}</p>
              )}
            </div>
          </div>

          <p className="mt-4 text-xs text-slate-500">
            The slot is published at{' '}
            {selectedStation ? `${selectedStation.stationCode} - ${selectedStation.name}` : 'the selected node'}.
            The API assigns the slot code and rejects a window that clashes with an existing one.
          </p>
        </form>
      </Modal>

      {/* ---- Edit slot ---- */}
      <Modal
        open={editTarget !== null}
        title={`Edit slot ${editTarget?.slotCode ?? ''}`}
        onClose={closeEdit}
        footer={
          <>
            <button type="button" className="btn-secondary" onClick={closeEdit} disabled={saving}>
              Cancel
            </button>
            <button type="submit" form="edit-slot-form" className="btn-primary" disabled={saving}>
              {saving && <Spinner size="sm" />}
              Save changes
            </button>
          </>
        }
      >
        <form id="edit-slot-form" onSubmit={(event) => void handleEdit(event)}>
          {editError && (
            <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-800">
              {editError}
            </div>
          )}

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label className="label" htmlFor="edit-slot-start">
                Start time
              </label>
              <input
                id="edit-slot-start"
                type="time"
                className="input"
                required
                value={editForm.startTime}
                onChange={(event) => setEditForm({ ...editForm, startTime: event.target.value })}
              />
              {fieldError(editFieldErrors, 'startTime') && (
                <p className="field-error">{fieldError(editFieldErrors, 'startTime')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="edit-slot-end">
                End time
              </label>
              <input
                id="edit-slot-end"
                type="time"
                className="input"
                required
                value={editForm.endTime}
                onChange={(event) => setEditForm({ ...editForm, endTime: event.target.value })}
              />
              {fieldError(editFieldErrors, 'endTime') && (
                <p className="field-error">{fieldError(editFieldErrors, 'endTime')}</p>
              )}
            </div>

            <div className="sm:col-span-2">
              <label className="label" htmlFor="edit-slot-kind">
                Slot type
              </label>
              <select
                id="edit-slot-kind"
                className="select"
                value={editForm.slotType}
                onChange={(event) =>
                  setEditForm({ ...editForm, slotType: event.target.value as SlotType })
                }
              >
                {TYPE_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {slotTypeLabel(option)}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="label" htmlFor="edit-slot-capacity">
                Energy capacity (kWh)
              </label>
              <input
                id="edit-slot-capacity"
                type="number"
                min="0.1"
                step="0.1"
                className="input"
                required
                value={editForm.energyCapacityKwh}
                onChange={(event) =>
                  setEditForm({ ...editForm, energyCapacityKwh: event.target.value })
                }
              />
              {fieldError(editFieldErrors, 'energyCapacityKwh') && (
                <p className="field-error">{fieldError(editFieldErrors, 'energyCapacityKwh')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="edit-slot-bay">
                Battery bay number
              </label>
              <input
                id="edit-slot-bay"
                type="number"
                min="1"
                step="1"
                className="input"
                required
                value={editForm.batterySlotNumber}
                onChange={(event) =>
                  setEditForm({ ...editForm, batterySlotNumber: event.target.value })
                }
              />
              {fieldError(editFieldErrors, 'batterySlotNumber') && (
                <p className="field-error">{fieldError(editFieldErrors, 'batterySlotNumber')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="edit-slot-max">
                Maximum bookings
              </label>
              <input
                id="edit-slot-max"
                type="number"
                min="1"
                step="1"
                className="input"
                required
                value={editForm.maxBookings}
                onChange={(event) => setEditForm({ ...editForm, maxBookings: event.target.value })}
              />
              {fieldError(editFieldErrors, 'maxBookings') && (
                <p className="field-error">{fieldError(editFieldErrors, 'maxBookings')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="edit-slot-status">
                Status
              </label>
              <select
                id="edit-slot-status"
                className="select"
                value={editForm.status}
                onChange={(event) =>
                  setEditForm({ ...editForm, status: event.target.value as SlotStatus })
                }
              >
                {STATUS_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
              {fieldError(editFieldErrors, 'status') && (
                <p className="field-error">{fieldError(editFieldErrors, 'status')}</p>
              )}
            </div>
          </div>

          <p className="mt-4 text-xs text-slate-500">
            This slot currently holds {editTarget?.bookedCount ?? 0} booking(s). The API refuses any
            change that would leave an accepted reservation without capacity.
          </p>
        </form>
      </Modal>

      {/* ---- Block confirmation ---- */}
      <ConfirmDialog
        open={blockTarget !== null}
        title="Block this slot"
        message={`Block ${blockTarget?.slotCode ?? 'this slot'} (${blockTarget?.startTime ?? ''} - ${blockTarget?.endTime ?? ''})? Blocking stops new bookings on the slot, and the ${blockTarget?.bookedCount ?? 0} reservation(s) already accepted on it are kept and must be handled separately.`}
        confirmLabel="Block slot"
        busy={busyId !== null}
        onConfirm={() => void handleBlock()}
        onCancel={() => setBlockTarget(null)}
      />

      {/* ---- Delete confirmation ---- */}
      <ConfirmDialog
        open={deleteTarget !== null}
        title="Delete this slot"
        message={`Permanently delete ${deleteTarget?.slotCode ?? 'this slot'} (${deleteTarget?.startTime ?? ''} - ${deleteTarget?.endTime ?? ''})? If the API finds bookings on it, the deletion is refused and the reason is shown.`}
        confirmLabel="Delete slot"
        tone="danger"
        busy={busyId !== null}
        onConfirm={() => void handleDelete()}
        onCancel={() => setDeleteTarget(null)}
      />

      {/* ---- Generate slots ---- */}
      <Modal
        open={generateOpen}
        title="Generate booking slots"
        size="lg"
        onClose={closeGenerate}
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={closeGenerate}
              disabled={generating}
            >
              Close
            </button>
            <button
              type="submit"
              form="generate-slots-form"
              className="btn-primary"
              disabled={generating}
            >
              {generating && <Spinner size="sm" />}
              Generate
            </button>
          </>
        }
      >
        <form id="generate-slots-form" onSubmit={(event) => void handleGenerate(event)}>
          {generateError && (
            <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-800">
              {generateError}
            </div>
          )}

          <p className="mb-4 text-sm text-slate-600">
            One call publishes every window in the range, which saves an operator creating slots one
            at a time for{' '}
            {selectedStation ? `${selectedStation.stationCode} - ${selectedStation.name}` : 'this node'}.
          </p>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <div>
              <label className="label" htmlFor="generate-from">
                From date
              </label>
              <input
                id="generate-from"
                type="date"
                className="input"
                required
                value={generateForm.fromDate}
                onChange={(event) =>
                  setGenerateForm({ ...generateForm, fromDate: event.target.value })
                }
              />
            </div>

            <div>
              <label className="label" htmlFor="generate-to">
                To date
              </label>
              <input
                id="generate-to"
                type="date"
                className="input"
                required
                value={generateForm.toDate}
                onChange={(event) =>
                  setGenerateForm({ ...generateForm, toDate: event.target.value })
                }
              />
            </div>

            <div>
              <label className="label" htmlFor="generate-duration">
                Slot length (hours)
              </label>
              <input
                id="generate-duration"
                type="number"
                min="1"
                max="12"
                step="1"
                className="input"
                required
                value={generateForm.slotDurationHours}
                onChange={(event) =>
                  setGenerateForm({ ...generateForm, slotDurationHours: event.target.value })
                }
              />
            </div>

            <div>
              <label className="label" htmlFor="generate-start-hour">
                Start hour (0-23)
              </label>
              <input
                id="generate-start-hour"
                type="number"
                min="0"
                max="23"
                step="1"
                className="input"
                required
                value={generateForm.startHour}
                onChange={(event) =>
                  setGenerateForm({ ...generateForm, startHour: event.target.value })
                }
              />
            </div>

            <div>
              <label className="label" htmlFor="generate-end-hour">
                End hour (1-24)
              </label>
              <input
                id="generate-end-hour"
                type="number"
                min="1"
                max="24"
                step="1"
                className="input"
                required
                value={generateForm.endHour}
                onChange={(event) =>
                  setGenerateForm({ ...generateForm, endHour: event.target.value })
                }
              />
            </div>

            <div>
              <label className="label" htmlFor="generate-kind">
                Slot type
              </label>
              <select
                id="generate-kind"
                className="select"
                value={generateForm.slotType}
                onChange={(event) =>
                  setGenerateForm({ ...generateForm, slotType: event.target.value as SlotType })
                }
              >
                {TYPE_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {slotTypeLabel(option)}
                  </option>
                ))}
              </select>
            </div>

            <div>
              <label className="label" htmlFor="generate-capacity">
                Energy capacity (kWh)
              </label>
              <input
                id="generate-capacity"
                type="number"
                min="0.1"
                step="0.1"
                className="input"
                required
                value={generateForm.energyCapacityKwh}
                onChange={(event) =>
                  setGenerateForm({ ...generateForm, energyCapacityKwh: event.target.value })
                }
              />
            </div>

            <div>
              <label className="label" htmlFor="generate-bays">
                Battery bays per window
              </label>
              <input
                id="generate-bays"
                type="number"
                min="1"
                step="1"
                className="input"
                required
                value={generateForm.batterySlots}
                onChange={(event) =>
                  setGenerateForm({ ...generateForm, batterySlots: event.target.value })
                }
              />
            </div>

            <div className="flex items-end">
              <label className="flex items-start gap-2 text-sm text-slate-700" htmlFor="generate-skip">
                <input
                  id="generate-skip"
                  type="checkbox"
                  className="mt-0.5 h-4 w-4 rounded border-slate-300 text-solar-500 focus:ring-solar-500"
                  checked={generateForm.skipClosedDays}
                  onChange={(event) =>
                    setGenerateForm({ ...generateForm, skipClosedDays: event.target.checked })
                  }
                />
                <span>Skip days the node is closed</span>
              </label>
            </div>
          </div>
        </form>

        {/* The counts and notes are produced entirely by the API - the browser
            does not decide which windows were created or skipped. */}
        {generateResult && (
          <div className="mt-5 rounded-lg border border-grid-200 bg-grid-50 px-4 py-3">
            <p className="text-sm font-semibold text-grid-800">Generation finished</p>
            <div className="mt-2 flex flex-wrap gap-6">
              <div>
                <p className="text-xl font-bold text-grid-800">{generateResult.created}</p>
                <p className="text-xs font-medium text-grid-700">Slots created</p>
              </div>
              <div>
                <p className="text-xl font-bold text-slate-700">{generateResult.skipped}</p>
                <p className="text-xs font-medium text-slate-600">Skipped</p>
              </div>
            </div>
            {generateResult.notes.length > 0 && (
              <ul className="mt-3 list-disc space-y-1 pl-5 text-xs text-slate-600">
                {generateResult.notes.map((note, index) => (
                  <li key={`${index}-${note}`}>{note}</li>
                ))}
              </ul>
            )}
          </div>
        )}
      </Modal>
    </div>
  );
}
