/* =============================================================================
 * File        : src/pages/ReservationsPage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Pages / Reservations
 * Description : Lists, filters and manages energy reservations.
 * Author      : IT22228062 - Akash
 * Created     : 2026-09-17
 * =============================================================================
 */

import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { Ban, CalendarClock, Check, Eye, Plus, XCircle } from 'lucide-react';
import { prosumersApi, reservationsApi, slotsApi, stationsApi } from '../api/services';
import { ApiError } from '../api/client';
import { useAuth } from '../context/AuthContext';
import {
  Badge,
  ConfirmDialog,
  EmptyState,
  ErrorState,
  Modal,
  PageLoader,
  Pagination,
  SearchInput,
  Spinner,
  useToast,
} from '../components/ui';
import { formatDateTime, kwh } from '../utils/format';
import type { PagedResult, Reservation, ReservationStatus, Slot, Station, User } from '../types';

/** How many rows the list requests per page. */
const PAGE_SIZE = 10;

/** How many milliseconds to debounce text input in the search field to avoid excessive API requests. */
const SEARCH_DEBOUNCE_MS = 400;

/** Default starting page for paginated reservation queries. */
const INITIAL_PAGE = 1;

/** The status values the filter offers, mirroring the ReservationStatus union. */
const STATUS_OPTIONS: ReservationStatus[] = [
  'Pending',
  'Approved',
  'Completed',
  'Cancelled',
  'Rejected',
];

/** Recorded on the booking so the audit trail shows where the cancellation came from. */
const CANCEL_REASON = 'Cancelled by staff from the web console.';

/**
 * Filter parameters supported by the reservation query endpoints.
 * Note: Under rule R11, prosumer callers are scoped server-side to their own NIC
 * regardless of query criteria, while staff have access to multi-node filters.
 */
export interface ReservationFilterQuery {
  search?: string;
  status?: string;
  stationId?: string;
  fromDate?: string;
  toDate?: string;
  page: number;
  pageSize: number;
}

/**
 * Wording shown on a disabled Cancel button. The UI never works out whether the
 * 12 hour notice has passed - it simply explains the server's canCancel verdict.
 */
const CANCEL_BLOCKED_HINT = 'Cancellation requires at least 12 hours notice (enforced by the API).';

/** Shape of the create-on-behalf form while it is being filled in. */
interface CreateFormState {
  prosumerNic: string;
  stationId: string;
  slotId: string;
  energyKwh: string;
}

/** The empty create form. */
const EMPTY_FORM: CreateFormState = {
  prosumerNic: '',
  stationId: '',
  slotId: '',
  energyKwh: '',
};

/** Picks one message out of the API's field error map, ignoring key casing. */
function fieldError(errors: Record<string, string[]> | null, field: string): string | undefined {
  // The C# model binder capitalises property names, so a case insensitive lookup
  // keeps this working whether the server sends "slotId" or "SlotId".
  if (!errors) return undefined;
  const key = Object.keys(errors).find((name) => name.toLowerCase() === field.toLowerCase());
  return key && errors[key].length > 0 ? errors[key][0] : undefined;
}

/** Turns any thrown value into the sentence a user should read. */
function describeError(err: unknown): string {
  // An ApiError already carries the API's own wording, which is always preferred.
  return err instanceof ApiError ? err.message : 'Something went wrong';
}

/** The reservation list, filters and staff actions. */
export default function ReservationsPage() {
  // Prosumers may open this route too; the API scopes their rows and refuses the
  // staff only actions, so the buttons below are simply hidden for them.
  const navigate = useNavigate();
  const toast = useToast();
  const { isProsumer } = useAuth();

  /* ---- filter state ---- */
  const [searchInput, setSearchInput] = useState<string>('');
  const [search, setSearch] = useState<string>('');
  const [status, setStatus] = useState<string>('');
  const [stationId, setStationId] = useState<string>('');
  const [fromDate, setFromDate] = useState<string>('');
  const [toDate, setToDate] = useState<string>('');
  const [page, setPage] = useState<number>(1);

  /* ---- list state ---- */
  const [result, setResult] = useState<PagedResult<Reservation> | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState<number>(0);

  /* ---- lookup data ---- */
  const [stations, setStations] = useState<Station[]>([]);

  /* ---- row action state ---- */
  const [busyId, setBusyId] = useState<string | null>(null);
  const [rejectTarget, setRejectTarget] = useState<Reservation | null>(null);
  const [rejectReason, setRejectReason] = useState<string>('');
  const [rejectError, setRejectError] = useState<string | null>(null);
  const [cancelTarget, setCancelTarget] = useState<Reservation | null>(null);

  /* ---- create dialog state ---- */
  const [createOpen, setCreateOpen] = useState<boolean>(false);
  const [form, setForm] = useState<CreateFormState>(EMPTY_FORM);
  const [prosumers, setProsumers] = useState<User[]>([]);
  const [slots, setSlots] = useState<Slot[]>([]);
  const [slotsLoading, setSlotsLoading] = useState<boolean>(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [createFieldErrors, setCreateFieldErrors] = useState<Record<string, string[]> | null>(null);
  const [submitting, setSubmitting] = useState<boolean>(false);

  // Asks the list effect below to run again after an action has changed a row.
  function reload() {
    setReloadKey((current) => current + 1);
  }

  // Debounces the search box: without this every keystroke would fire its own
  // request. The cleanup clears the pending timer, so only the last SEARCH_DEBOUNCE_MS of
  // typing reaches the API. The page returns to INITIAL_PAGE because the result set changes.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSearch(searchInput.trim());
      setPage(INITIAL_PAGE);
    }, SEARCH_DEBOUNCE_MS);

    return () => window.clearTimeout(timer);
  }, [searchInput]);

  // Loads the node list once so the filter and the create dialog can offer it.
  useEffect(() => {
    let cancelled = false;

    async function loadStations() {
      try {
        const paged = await stationsApi.list({ pageSize: 100 });
        if (!cancelled) setStations(paged.items);
      } catch {
        // A missing node list only costs the filter its options, so it is not
        // promoted to a page level error.
      }
    }

    void loadStations();
    return () => {
      cancelled = true;
    };
  }, []);

  // Re-runs whenever a filter or the page changes. The guard stops a slow reply
  // from writing into a component that has already been unmounted.
  useEffect(() => {
    let cancelled = false;

    async function loadReservations() {
      setLoading(true);
      setError(null);

      try {
        // Every filter is applied by the API - the browser never filters a page
        // of rows it has already been given.
        const paged = await reservationsApi.list({
          search,
          status,
          stationId,
          fromDate,
          toDate,
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

    void loadReservations();
    return () => {
      cancelled = true;
    };
  }, [search, status, stationId, fromDate, toDate, page, reloadKey]);

  // Fills the prosumer picker the first time the create dialog is opened.
  useEffect(() => {
    if (!createOpen || isProsumer) return;
    let cancelled = false;

    async function loadProsumers() {
      try {
        // Enforce business rules R5 & R6: only prosumers with status 'Active'
        // are eligible to hold slot reservations. Pending and Deactivated accounts
        // are excluded by the backend query filter.
        const paged = await prosumersApi.list({ status: 'Active', pageSize: 100 });
        if (!cancelled) setProsumers(paged.items);
      } catch (err) {
        if (!cancelled) setCreateError(describeError(err));
      }
    }

    void loadProsumers();
    return () => {
      cancelled = true;
    };
  }, [createOpen, isProsumer]);

  // Reloads the bookable slots whenever the chosen node changes.
  useEffect(() => {
    if (!createOpen || form.stationId === '') {
      setSlots([]);
      return;
    }

    let cancelled = false;

    async function loadSlots(node: string) {
      setSlotsLoading(true);
      try {
        const available = await slotsApi.available(node);
        // Under business rule R8, the backend DomainMapper flags isBookable = true only when:
        // node is active, slot status is Available, remaining capacity > 0, and startUtc > now.
        // The UI filters on isBookable without duplicating scheduling rules in the browser.
        if (!cancelled) setSlots(available.filter((slot) => slot.isBookable));
      } catch (err) {
        if (!cancelled) {
          setSlots([]);
          setCreateError(describeError(err));
        }
      } finally {
        if (!cancelled) setSlotsLoading(false);
      }
    }

    void loadSlots(form.stationId);
    return () => {
      cancelled = true;
    };
  }, [createOpen, form.stationId]);

  // Returns every filter to its default and goes back to the first page.
  function clearFilters() {
    setSearchInput('');
    setSearch('');
    setStatus('');
    setStationId('');
    setFromDate('');
    setToDate('');
    setPage(1);
  }

  // Approves a Pending booking; the API issues the cryptographically secure QR token
  // and returns confirmation wording. Only Backoffice and GridOperator may invoke this.
  async function handleApprove(reservation: Reservation) {
    setBusyId(reservation.id);
    try {
      const { message } = await reservationsApi.approve(reservation.id);
      toast.success(message);
      reload();
    } catch (err) {
      toast.error(describeError(err));
    } finally {
      setBusyId(null);
    }
  }

  // Rejects a Pending booking with the reason typed into the dialog.
  async function handleReject() {
    if (!rejectTarget) return;

    const reason = rejectReason.trim();
    if (reason.length === 0) {
      setRejectError('Please give a reason for rejecting this booking.');
      return;
    }

    setBusyId(rejectTarget.id);
    try {
      const { message } = await reservationsApi.reject(rejectTarget.id, reason);
      toast.success(message);
      setRejectTarget(null);
      setRejectReason('');
      setRejectError(null);
      reload();
    } catch (err) {
      toast.error(describeError(err));
    } finally {
      setBusyId(null);
    }
  }

  // Cancels a booking. Business rule R3 (requiring >= 12 hours notice before slot
  // start) is enforced strictly by the backend ReservationService. If the notice
  // period has expired, the API returns HTTP 400 with error code RES_CANCEL_12H.
  async function handleCancel() {
    if (!cancelTarget) return;

    setBusyId(cancelTarget.id);
    try {
      const { message } = await reservationsApi.cancel(cancelTarget.id, CANCEL_REASON);
      toast.success(message);
      setCancelTarget(null);
      reload();
    } catch (err) {
      toast.error(describeError(err));
      setCancelTarget(null);
    } finally {
      setBusyId(null);
    }
  }

  // Closes and resets the create dialog.
  function closeCreate() {
    setCreateOpen(false);
    setForm(EMPTY_FORM);
    setSlots([]);
    setCreateError(null);
    setCreateFieldErrors(null);
  }

  // Books a slot on a prosumer's behalf. The 7 day window, the duplicate check
  // and the capacity check are all applied by the API, which answers with either
  // the new booking or the rule that stopped it.
  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setCreateError(null);
    setCreateFieldErrors(null);

    try {
      const { message } = await reservationsApi.create({
        prosumerNic: form.prosumerNic === '' ? undefined : form.prosumerNic,
        stationId: form.stationId,
        slotId: form.slotId,
        energyKwh: Number(form.energyKwh),
      });
      toast.success(message);
      closeCreate();
      setPage(1);
      reload();
    } catch (err) {
      setCreateError(describeError(err));
      setCreateFieldErrors(err instanceof ApiError ? err.fieldErrors : null);
    } finally {
      setSubmitting(false);
    }
  }

  const rows: Reservation[] = result?.items ?? [];
  const hasFilters =
    search !== '' || status !== '' || stationId !== '' || fromDate !== '' || toDate !== '';

  return (
    <div className="space-y-5">
      {/* ---- Page header ---- */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-xl font-semibold text-slate-900 sm:text-2xl">Reservations</h1>
          <p className="mt-1 text-sm text-slate-500">
            Search, approve and manage energy slot bookings across every microgrid node.
          </p>
        </div>
        {!isProsumer && (
          <button type="button" className="btn-primary" onClick={() => setCreateOpen(true)}>
            <Plus className="h-4 w-4" aria-hidden="true" />
            New reservation
          </button>
        )}
      </div>

      {/* ---- Filter criteria ---- */}
      <div className="card">
        <div className="card-body grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
          <div className="sm:col-span-2 lg:col-span-1">
            {/* SearchInput renders its own input element, so a plain caption is
                used here rather than a label pointing at an id it does not own. */}
            <p className="label">Search</p>
            <SearchInput
              value={searchInput}
              onChange={setSearchInput}
              placeholder="Reservation no, name, NIC or node"
            />
          </div>

          <div>
            <label className="label" htmlFor="reservation-status">
              Status
            </label>
            <select
              id="reservation-status"
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
            <label className="label" htmlFor="reservation-station">
              Node
            </label>
            <select
              id="reservation-station"
              className="select"
              value={stationId}
              onChange={(event) => {
                setStationId(event.target.value);
                setPage(1);
              }}
            >
              <option value="">All nodes</option>
              {stations.map((station) => (
                <option key={station.id} value={station.id}>
                  {station.stationCode} - {station.name}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="label" htmlFor="reservation-from">
              From
            </label>
            <input
              id="reservation-from"
              type="date"
              className="input"
              value={fromDate}
              onChange={(event) => {
                setFromDate(event.target.value);
                setPage(1);
              }}
            />
          </div>

          <div>
            <label className="label" htmlFor="reservation-to">
              To
            </label>
            <input
              id="reservation-to"
              type="date"
              className="input"
              value={toDate}
              onChange={(event) => {
                setToDate(event.target.value);
                setPage(1);
              }}
            />
          </div>
        </div>

        <div className="flex items-center justify-between gap-3 border-t border-slate-200 px-5 py-3">
          <p className="text-xs font-medium text-slate-500">
            {result ? `${result.totalCount} matching reservation(s)` : 'Applying filters...'}
          </p>
          <button type="button" className="btn-secondary btn-sm" onClick={clearFilters}>
            Clear filters
          </button>
        </div>
      </div>

      {/* ---- Results ---- */}
      {error && <ErrorState message={error} onRetry={reload} />}

      {!error && (
        <div className="card">
          {loading && <PageLoader />}

          {!loading && rows.length === 0 && (
            <EmptyState
              icon={CalendarClock}
              title="No reservations found"
              message={
                hasFilters
                  ? 'No booking matches the current filter criteria. Try widening the date range or clearing the filters.'
                  : 'There are no energy slot bookings yet. New reservations will appear here as soon as they are made.'
              }
              action={
                hasFilters ? (
                  <button type="button" className="btn-secondary" onClick={clearFilters}>
                    Clear filters
                  </button>
                ) : undefined
              }
            />
          )}

          {!loading && rows.length > 0 && (
            <>
              <div className="overflow-x-auto">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Reservation No</th>
                      <th>Prosumer</th>
                      <th>Node</th>
                      <th>Date &amp; time</th>
                      <th>Energy</th>
                      <th>Status</th>
                      <th className="text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((reservation) => {
                      // status, canCancel and canModify are all server verdicts.
                      const isPending = reservation.status === 'Pending';
                      const isOpenBooking = isPending || reservation.status === 'Approved';
                      const busy = busyId === reservation.id;

                      return (
                        <tr key={reservation.id}>
                          <td className="whitespace-nowrap font-semibold text-slate-900">
                            {reservation.reservationNo}
                          </td>
                          <td>
                            <p className="font-medium text-slate-800">{reservation.prosumerName}</p>
                            <p className="text-xs text-slate-500">{reservation.prosumerNic}</p>
                          </td>
                          <td>
                            <p className="font-medium text-slate-800">{reservation.stationCode}</p>
                            <p className="text-xs text-slate-500">{reservation.stationName}</p>
                          </td>
                          <td className="whitespace-nowrap">
                            <p className="text-slate-800">
                              {formatDateTime(reservation.reservationStartUtc)}
                            </p>
                            <p className="text-xs text-slate-500">
                              {reservation.startTime} - {reservation.endTime}
                            </p>
                          </td>
                          <td className="whitespace-nowrap">{kwh(reservation.energyKwh)}</td>
                          <td>
                            <Badge status={reservation.status} kind="reservation" />
                          </td>
                          <td>
                            <div className="flex flex-wrap items-center justify-end gap-2">
                              {!isProsumer && isPending && (
                                <button
                                  type="button"
                                  className="btn-success btn-sm"
                                  onClick={() => void handleApprove(reservation)}
                                  disabled={busy}
                                  aria-label={`Approve reservation ${reservation.reservationNo}`}
                                  aria-busy={busy}
                                >
                                  {busy ? (
                                    <Spinner size="sm" />
                                  ) : (
                                    <Check className="h-3.5 w-3.5" aria-hidden="true" />
                                  )}
                                  Approve
                                </button>
                              )}

                              {!isProsumer && isPending && (
                                <button
                                  type="button"
                                  className="btn-danger-quiet btn-sm"
                                  onClick={() => {
                                    setRejectTarget(reservation);
                                    setRejectReason('');
                                    setRejectError(null);
                                  }}
                                  disabled={busy}
                                >
                                  <XCircle className="h-3.5 w-3.5" aria-hidden="true" />
                                  Reject
                                </button>
                              )}

                              {/* canCancel is the API's rule R3 verdict. The button is
                                  enabled or disabled from it and the hours are never
                                  recomputed in the browser. */}
                              {reservation.canCancel && (
                                <button
                                  type="button"
                                  className="btn-secondary btn-sm"
                                  onClick={() => setCancelTarget(reservation)}
                                  disabled={busy}
                                >
                                  <Ban className="h-3.5 w-3.5" aria-hidden="true" />
                                  Cancel
                                </button>
                              )}

                              {!reservation.canCancel && isOpenBooking && (
                                <button
                                  type="button"
                                  className="btn-secondary btn-sm"
                                  disabled
                                  title={CANCEL_BLOCKED_HINT}
                                >
                                  <Ban className="h-3.5 w-3.5" aria-hidden="true" />
                                  Cancel
                                </button>
                              )}

                              <button
                                type="button"
                                className="btn-secondary btn-sm"
                                onClick={() => navigate(`/reservations/${reservation.id}`)}
                              >
                                <Eye className="h-3.5 w-3.5" aria-hidden="true" />
                                View
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

      {/* ---- Reject dialog ---- */}
      <Modal
        open={rejectTarget !== null}
        title={`Reject ${rejectTarget?.reservationNo ?? 'reservation'}`}
        onClose={() => {
          setRejectTarget(null);
          setRejectError(null);
        }}
        size="sm"
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={() => {
                setRejectTarget(null);
                setRejectError(null);
              }}
              disabled={busyId !== null}
            >
              Close
            </button>
            <button
              type="button"
              className="btn-danger"
              onClick={() => void handleReject()}
              disabled={busyId !== null}
            >
              {busyId !== null && <Spinner size="sm" />}
              Reject booking
            </button>
          </>
        }
      >
        <label className="label" htmlFor="reject-reason">
          Reason <span className="text-red-500">*</span>
        </label>
        <textarea
          id="reject-reason"
          rows={3}
          aria-required="true"
          aria-invalid={rejectError !== null}
          className={rejectError ? 'input input-error' : 'input'}
          value={rejectReason}
          placeholder="Explain why this booking cannot be honoured."
          onChange={(event) => {
            setRejectReason(event.target.value);
            setRejectError(null);
          }}
        />
        {rejectError && <p className="field-error">{rejectError}</p>}
        <p className="mt-2 text-xs text-slate-500">
          The prosumer sees this reason on their booking, so keep it clear and polite.
        </p>
      </Modal>

      {/* ---- Cancel confirmation ---- */}
      <ConfirmDialog
        open={cancelTarget !== null}
        title="Cancel reservation"
        message={`Cancel ${cancelTarget?.reservationNo ?? ''} for ${cancelTarget?.prosumerName ?? ''}? Note: Rule R3 requires at least 12 hours notice prior to slot start. Upon cancellation, the slot is released back to the node immediately.`}
        confirmLabel="Cancel booking"
        tone="danger"
        busy={busyId !== null}
        onConfirm={() => void handleCancel()}
        onCancel={() => setCancelTarget(null)}
      />

      {/* ---- Create on a prosumer's behalf ---- */}
      <Modal
        open={createOpen}
        title="New reservation"
        onClose={closeCreate}
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={closeCreate}
              disabled={submitting}
            >
              Cancel
            </button>
            <button
              type="submit"
              form="create-reservation-form"
              className="btn-primary"
              disabled={submitting}
            >
              {submitting && <Spinner size="sm" />}
              Create booking
            </button>
          </>
        }
      >
        <form id="create-reservation-form" onSubmit={(event) => void handleCreate(event)}>
          {createError && (
            <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-800">
              {createError}
            </div>
          )}

          <div className="space-y-4">
            <div>
              <label className="label" htmlFor="create-prosumer">
                Prosumer <span className="text-red-500">*</span>
              </label>
              <select
                id="create-prosumer"
                className="select"
                value={form.prosumerNic}
                required
                aria-label="Select registered active prosumer"
                onChange={(event) => setForm({ ...form, prosumerNic: event.target.value })}
              >
                <option value="">Select an active prosumer</option>
                {prosumers.map((prosumer) => (
                  <option key={prosumer.id} value={prosumer.nic ?? ''}>
                    {prosumer.fullName} - {prosumer.nic}
                  </option>
                ))}
              </select>
              {fieldError(createFieldErrors, 'prosumerNic') && (
                <p className="field-error">{fieldError(createFieldErrors, 'prosumerNic')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="create-station">
                Microgrid node
              </label>
              <select
                id="create-station"
                className="select"
                value={form.stationId}
                required
                onChange={(event) =>
                  setForm({ ...form, stationId: event.target.value, slotId: '' })
                }
              >
                <option value="">Select a node</option>
                {stations.map((station) => (
                  <option key={station.id} value={station.id}>
                    {station.stationCode} - {station.name}
                  </option>
                ))}
              </select>
              {fieldError(createFieldErrors, 'stationId') && (
                <p className="field-error">{fieldError(createFieldErrors, 'stationId')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="create-slot">
                Energy slot <span className="text-red-500">*</span>
              </label>
              <select
                id="create-slot"
                className="select"
                value={form.slotId}
                required
                aria-label="Select bookable energy slot"
                disabled={form.stationId === '' || slotsLoading}
                onChange={(event) => setForm({ ...form, slotId: event.target.value })}
              >
                <option value="">
                  {form.stationId === ''
                    ? 'Choose a node first'
                    : slotsLoading
                      ? 'Loading slots...'
                      : 'Select a bookable slot'}
                </option>
                {/* Only slots the API marked isBookable are listed - rule R8 is
                    evaluated on the server, never here. */}
                {slots.map((slot) => (
                  <option key={slot.id} value={slot.id}>
                    {slot.slotCode} - {formatDateTime(slot.startUtc)} ({slot.startTime} -{' '}
                    {slot.endTime})
                  </option>
                ))}
              </select>
              {!slotsLoading && form.stationId !== '' && slots.length === 0 && (
                <p className="mt-1 text-xs text-slate-500">
                  This node has no bookable slots at the moment.
                </p>
              )}
              {fieldError(createFieldErrors, 'slotId') && (
                <p className="field-error">{fieldError(createFieldErrors, 'slotId')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="create-energy">
                Energy (kWh)
              </label>
              <input
                id="create-energy"
                type="number"
                min="0.1"
                step="0.1"
                className="input"
                value={form.energyKwh}
                required
                placeholder="e.g. 12.5"
                onChange={(event) => setForm({ ...form, energyKwh: event.target.value })}
              />
              {fieldError(createFieldErrors, 'energyKwh') && (
                <p className="field-error">{fieldError(createFieldErrors, 'energyKwh')}</p>
              )}
            </div>
          </div>

          <p className="mt-4 text-xs text-slate-500">
            The booking window and the slot capacity are validated by the API when this form is
            submitted.
          </p>
        </form>
      </Modal>
    </div>
  );
}
