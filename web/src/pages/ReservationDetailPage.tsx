/* =============================================================================
 * File        : src/pages/ReservationDetailPage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Pages / Reservation Detail
 * Description : Shows one reservation with its history, QR code and actions.
 * Author      : IT22228062 - Akash
 * Created     : 2026-09-17
 * =============================================================================
 */

import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  ArrowLeft,
  Ban,
  CheckCircle2,
  Download,
  Pencil,
  QrCode as QrCodeIcon,
  XCircle,
} from 'lucide-react';
import { ApiError } from '../api/client';
import { reservationsApi, slotsApi, stationsApi } from '../api/services';
import { Badge, ErrorState, Modal, PageLoader, Spinner, useToast } from '../components/ui';
import { useAuth } from '../context/AuthContext';
import {
  describeHoursUntil,
  formatDate,
  formatDateTime,
  kwh,
  slotTypeLabel,
} from '../utils/format';
import type { QrCode, Reservation, Slot, Station } from '../types';

/** Which action is currently in flight, so only that button shows a spinner. */
type ActionKey = 'approve' | 'reject' | 'edit' | 'cancel';

/** One dot on the audit trail. */
interface TimelineEntry {
  key: string;
  title: string;
  timestamp: string | null;
  detail: string | null;
}

/** Wording shown on a button the API would refuse because of the notice period. */
const NOTICE_HINT =
  'Not available. The API enforces the 12 hour notice period and has returned false for this booking.';

/** Turns any thrown value into the message a screen should display. */
function errorMessage(err: unknown): string {
  // An ApiError already carries the server's own wording, including the rule that fired.
  return err instanceof ApiError ? err.message : 'Something went wrong';
}

/** One label / value pair inside a details card. */
function DetailRow({ label, value }: { label: string; value: ReactNode }) {
  // Stacks on a phone and becomes two columns from the sm breakpoint upwards.
  return (
    <div className="flex flex-col gap-0.5 sm:flex-row sm:items-baseline sm:gap-4">
      <dt className="w-44 shrink-0 text-xs font-semibold text-slate-500">
        {label}
      </dt>
      <dd className="min-w-0 break-words text-sm font-medium text-slate-800">{value}</dd>
    </div>
  );
}

/** Collects whichever audit fields the server actually populated. */
function buildTimeline(reservation: Reservation): TimelineEntry[] {
  // Nothing is inferred here - an entry appears only when the API sent its field.
  const entries: TimelineEntry[] = [
    {
      key: 'created',
      title: 'Reservation created',
      timestamp: reservation.createdAt,
      detail: `Requested ${kwh(reservation.energyKwh)}`,
    },
  ];

  if (reservation.approvedAt || reservation.approvedBy) {
    entries.push({
      key: 'approved',
      title: 'Approved',
      timestamp: reservation.approvedAt,
      detail: reservation.approvedBy ? `By ${reservation.approvedBy}` : null,
    });
  }

  if (reservation.completedAt || reservation.completedBy) {
    entries.push({
      key: 'completed',
      title: 'Energy transfer completed',
      timestamp: reservation.completedAt,
      detail: [
        reservation.completedBy ? `By ${reservation.completedBy}` : null,
        reservation.operatorNotes ? `Notes: ${reservation.operatorNotes}` : null,
      ]
        .filter(Boolean)
        .join(' - ') || null,
    });
  }

  if (reservation.cancelledAt || reservation.cancelledBy) {
    entries.push({
      key: 'cancelled',
      title: 'Cancelled',
      timestamp: reservation.cancelledAt,
      detail: [
        reservation.cancelledBy ? `By ${reservation.cancelledBy}` : null,
        reservation.cancelReason ? `Reason: ${reservation.cancelReason}` : null,
      ]
        .filter(Boolean)
        .join(' - ') || null,
    });
  }

  if (reservation.rejectedReason) {
    entries.push({
      key: 'rejected',
      title: 'Rejected',
      timestamp: null,
      detail: `Reason: ${reservation.rejectedReason}`,
    });
  }

  return entries;
}

/** Full detail screen for a single reservation. */
export default function ReservationDetailPage() {
  // Role gating below mirrors the [Authorize] attributes on the C# controllers;
  // the API remains the real enforcement point for every one of these calls.
  const { id = '' } = useParams<{ id: string }>();
  const { hasRole } = useAuth();
  const toast = useToast();
  const isStaff = hasRole('Backoffice', 'GridOperator');

  const [reservation, setReservation] = useState<Reservation | null>(null);
  const [station, setStation] = useState<Station | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState<number>(0);

  const [qr, setQr] = useState<QrCode | null>(null);
  const [qrLoading, setQrLoading] = useState<boolean>(false);
  const [qrError, setQrError] = useState<string | null>(null);

  const [busy, setBusy] = useState<ActionKey | null>(null);

  const [rejectOpen, setRejectOpen] = useState<boolean>(false);
  const [rejectReason, setRejectReason] = useState<string>('');
  const [rejectError, setRejectError] = useState<string | null>(null);

  const [cancelOpen, setCancelOpen] = useState<boolean>(false);
  const [cancelReason, setCancelReason] = useState<string>('');

  const [editOpen, setEditOpen] = useState<boolean>(false);
  const [editSlotId, setEditSlotId] = useState<string>('');
  const [editEnergy, setEditEnergy] = useState<string>('');
  const [editError, setEditError] = useState<string | null>(null);
  const [slots, setSlots] = useState<Slot[]>([]);
  const [slotsLoading, setSlotsLoading] = useState<boolean>(false);
  const [slotsError, setSlotsError] = useState<string | null>(null);

  // Loads the reservation, and the node behind it for the postal address, which
  // the ReservationDto itself does not carry.
  useEffect(() => {
    let cancelled = false;

    async function run() {
      setLoading(true);
      setError(null);
      try {
        const found = await reservationsApi.get(id);
        if (cancelled) return;
        setReservation(found);

        try {
          const node = await stationsApi.get(found.stationId);
          if (!cancelled) setStation(node);
        } catch {
          // The address is a nicety; losing it must not break the screen.
          if (!cancelled) setStation(null);
        }
      } catch (err) {
        if (!cancelled) {
          setError(errorMessage(err));
          setReservation(null);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void run();
    return () => {
      cancelled = true;
    };
  }, [id, reloadKey]);

  // The QR image exists only once the API has approved the booking and issued a token.
  useEffect(() => {
    if (!reservation || reservation.status !== 'Approved') {
      setQr(null);
      setQrError(null);
      return;
    }

    let cancelled = false;
    const reservationId = reservation.id;

    async function run() {
      setQrLoading(true);
      setQrError(null);
      try {
        const code = await reservationsApi.qr(reservationId);
        if (!cancelled) setQr(code);
      } catch (err) {
        if (!cancelled) {
          setQrError(errorMessage(err));
          setQr(null);
        }
      } finally {
        if (!cancelled) setQrLoading(false);
      }
    }

    void run();
    return () => {
      cancelled = true;
    };
  }, [reservation]);

  // The edit dialog offers the slots the API reports as bookable (rule R8).
  useEffect(() => {
    if (!editOpen || !reservation) return;

    let cancelled = false;
    const stationId = reservation.stationId;

    async function run() {
      setSlotsLoading(true);
      setSlotsError(null);
      try {
        const rows = await slotsApi.available(stationId);
        // isBookable is the server's verdict; the UI never works out availability itself.
        if (!cancelled) setSlots(rows.filter((row) => row.isBookable));
      } catch (err) {
        if (!cancelled) {
          setSlotsError(errorMessage(err));
          setSlots([]);
        }
      } finally {
        if (!cancelled) setSlotsLoading(false);
      }
    }

    void run();
    return () => {
      cancelled = true;
    };
  }, [editOpen, reservation]);

  // Asks the API to approve, then re-reads the record so the QR card appears.
  async function handleApprove() {
    if (!reservation) return;
    setBusy('approve');
    try {
      const result = await reservationsApi.approve(reservation.id);
      toast.success(result.message);
      setReloadKey((key) => key + 1);
    } catch (err) {
      toast.error(errorMessage(err));
    } finally {
      setBusy(null);
    }
  }

  // Refuses the booking; the API requires a reason, so the dialog does too.
  async function handleReject() {
    if (!reservation) return;
    if (!rejectReason.trim()) {
      setRejectError('Please give the prosumer a reason for the refusal.');
      return;
    }

    setBusy('reject');
    try {
      const result = await reservationsApi.reject(reservation.id, rejectReason.trim());
      toast.success(result.message);
      setRejectOpen(false);
      setRejectReason('');
      setRejectError(null);
      setReloadKey((key) => key + 1);
    } catch (err) {
      toast.error(errorMessage(err));
    } finally {
      setBusy(null);
    }
  }

  // Sends the changed slot and / or energy amount; rule R2 is checked server side.
  async function handleEdit() {
    if (!reservation) return;

    const payload: { slotId?: string; energyKwh?: number } = {};
    if (editSlotId && editSlotId !== reservation.slotId) {
      payload.slotId = editSlotId;
    }

    const parsed = Number(editEnergy);
    if (editEnergy.trim() === '' || Number.isNaN(parsed) || parsed <= 0) {
      setEditError('Enter the energy amount in kWh as a number greater than zero.');
      return;
    }
    if (parsed !== reservation.energyKwh) {
      payload.energyKwh = parsed;
    }

    if (payload.slotId === undefined && payload.energyKwh === undefined) {
      setEditError('Change the slot or the energy amount before saving.');
      return;
    }

    setBusy('edit');
    try {
      const result = await reservationsApi.update(reservation.id, payload);
      toast.success(result.message);
      setEditOpen(false);
      setEditError(null);
      setReloadKey((key) => key + 1);
    } catch (err) {
      toast.error(errorMessage(err));
    } finally {
      setBusy(null);
    }
  }

  // Cancels the booking with an optional reason; rule R3 is checked server side.
  async function handleCancel() {
    if (!reservation) return;
    setBusy('cancel');
    try {
      const result = await reservationsApi.cancel(reservation.id, cancelReason.trim() || undefined);
      toast.success(result.message);
      setCancelOpen(false);
      setCancelReason('');
      setReloadKey((key) => key + 1);
    } catch (err) {
      toast.error(errorMessage(err));
    } finally {
      setBusy(null);
    }
  }

  // Prefills the edit dialog from the record currently on screen.
  function openEdit() {
    if (!reservation) return;
    setEditSlotId('');
    setEditEnergy(String(reservation.energyKwh));
    setEditError(null);
    setEditOpen(true);
  }

  if (loading && !reservation) {
    return <PageLoader />;
  }

  if (error) {
    return (
      <div className="space-y-4">
        <Link to="/reservations" className="btn-ghost btn-sm w-fit">
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Back to reservations
        </Link>
        <ErrorState message={error} onRetry={() => setReloadKey((key) => key + 1)} />
      </div>
    );
  }

  if (!reservation) {
    return (
      <div className="space-y-4">
        <Link to="/reservations" className="btn-ghost btn-sm w-fit">
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Back to reservations
        </Link>
        <ErrorState message="That reservation could not be found." />
      </div>
    );
  }

  const timeline = buildTimeline(reservation);
  // These four flags are read straight from the API response. The 12 hour notice
  // period (rules R2 and R3) lives in the C# service layer and is never
  // recomputed here - the UI only obeys canModify and canCancel.
  const isOpenStatus = reservation.status === 'Pending' || reservation.status === 'Approved';
  const showApprove = isStaff && reservation.status === 'Pending';
  const showEdit = reservation.canModify || isOpenStatus;
  const showCancel = reservation.canCancel || isOpenStatus;
  const hasActions = showApprove || showEdit || showCancel;
  const qrDataUri = qr ? `data:image/png;base64,${qr.qrImageBase64}` : '';

  return (
    <div className="space-y-6">
      {/* ---- Header ---- */}
      <div className="space-y-3">
        <Link to="/reservations" className="btn-ghost btn-sm w-fit">
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          Back to reservations
        </Link>

        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-2xl font-semibold tracking-tight text-slate-900">
              {reservation.reservationNo}
            </h1>
            <Badge status={reservation.status} kind="reservation" />
            {loading && <Spinner size="sm" />}
          </div>
          {/* hoursUntilStart is calculated by the API; this only phrases it. */}
          <p className="text-sm font-medium text-slate-500">
            Starts {describeHoursUntil(reservation.hoursUntilStart)}
          </p>
        </div>
      </div>

      {/* ---- Two column body ---- */}
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <div className="space-y-6 lg:col-span-2">
          {/* ---- Booking details ---- */}
          <section className="card">
            <div className="card-header">
              <h2 className="text-base font-semibold text-slate-900">Booking details</h2>
            </div>
            <dl className="card-body space-y-3">
              <DetailRow
                label="Microgrid node"
                value={
                  <span className="block">
                    <span className="font-semibold">{reservation.stationCode}</span>
                    {' - '}
                    {reservation.stationName}
                    <span className="mt-0.5 block text-xs font-normal text-slate-500">
                      {station ? `${station.address}, ${station.city}` : 'Address unavailable'}
                    </span>
                  </span>
                }
              />
              <DetailRow label="Slot code" value={reservation.slotCode} />
              <DetailRow label="Date" value={formatDate(reservation.reservationStartUtc)} />
              <DetailRow
                label="Time"
                value={`${reservation.startTime} - ${reservation.endTime}`}
              />
              <DetailRow label="Transfer type" value={slotTypeLabel(reservation.slotType)} />
              <DetailRow label="Requested energy" value={kwh(reservation.energyKwh)} />
              <DetailRow label="Delivered energy" value={kwh(reservation.deliveredKwh)} />
              <DetailRow label="Created" value={formatDateTime(reservation.createdAt)} />
            </dl>
          </section>

          {/* ---- Prosumer ---- */}
          <section className="card">
            <div className="card-header">
              <h2 className="text-base font-semibold text-slate-900">Prosumer</h2>
            </div>
            <dl className="card-body space-y-3">
              <DetailRow label="Full name" value={reservation.prosumerName} />
              <DetailRow label="NIC" value={reservation.prosumerNic} />
              <DetailRow label="Phone" value={reservation.prosumerPhone || '-'} />
            </dl>
          </section>

          {/* ---- Audit trail ---- */}
          <section className="card">
            <div className="card-header">
              <h2 className="text-base font-semibold text-slate-900">Audit trail</h2>
            </div>
            <div className="card-body">
              <ol className="relative space-y-6 border-l border-slate-200 pl-6">
                {timeline.map((entry) => (
                  <li key={entry.key} className="relative">
                    <span
                      className="absolute -left-[29px] top-1.5 h-2.5 w-2.5 rounded-full border-2 border-white bg-solar-500"
                      aria-hidden="true"
                    />
                    <p className="text-sm font-semibold text-slate-800">{entry.title}</p>
                    <p className="mt-0.5 text-xs font-medium text-slate-500">
                      {entry.timestamp ? formatDateTime(entry.timestamp) : 'Time not recorded'}
                    </p>
                    {entry.detail && (
                      <p className="mt-1 break-words text-sm text-slate-600">{entry.detail}</p>
                    )}
                  </li>
                ))}
              </ol>
            </div>
          </section>
        </div>

        {/* ---- Right rail ---- */}
        <div className="space-y-6">
          {/* ---- Actions ---- */}
          <section className="card">
            <div className="card-header">
              <h2 className="text-base font-semibold text-slate-900">Actions</h2>
            </div>
            <div className="card-body space-y-2.5">
              {!hasActions && (
                <p className="text-sm text-slate-500">
                  This reservation is {reservation.status.toLowerCase()}, so the API offers no
                  further actions on it.
                </p>
              )}

              {/* Approve and Reject appear only while the server still reports Pending. */}
              {showApprove && (
                <>
                  <button
                    type="button"
                    className="btn-success w-full"
                    onClick={() => void handleApprove()}
                    disabled={busy !== null}
                  >
                    {busy === 'approve' ? (
                      <Spinner size="sm" />
                    ) : (
                      <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
                    )}
                    Approve booking
                  </button>
                  <button
                    type="button"
                    className="btn-secondary w-full"
                    onClick={() => {
                      setRejectError(null);
                      setRejectOpen(true);
                    }}
                    disabled={busy !== null}
                  >
                    <XCircle className="h-4 w-4" aria-hidden="true" />
                    Reject booking
                  </button>
                </>
              )}

              {/* Enabled strictly from reservation.canModify - rule R2 lives in the API. */}
              {showEdit && (
                <button
                  type="button"
                  className="btn-primary w-full"
                  onClick={openEdit}
                  disabled={!reservation.canModify || busy !== null}
                  title={reservation.canModify ? undefined : NOTICE_HINT}
                >
                  <Pencil className="h-4 w-4" aria-hidden="true" />
                  Edit reservation
                </button>
              )}

              {/* Enabled strictly from reservation.canCancel - rule R3 lives in the API. */}
              {showCancel && (
                <button
                  type="button"
                  className="btn-danger w-full"
                  onClick={() => setCancelOpen(true)}
                  disabled={!reservation.canCancel || busy !== null}
                  title={reservation.canCancel ? undefined : NOTICE_HINT}
                >
                  <Ban className="h-4 w-4" aria-hidden="true" />
                  Cancel reservation
                </button>
              )}

              {isOpenStatus && (!reservation.canModify || !reservation.canCancel) && (
                <p className="pt-1 text-xs text-slate-500">
                  A greyed out button means the API returned false for it. Changes and
                  cancellations must reach the service at least 12 hours before the slot starts.
                </p>
              )}
            </div>
          </section>

          {/* ---- QR code (approved bookings only) ---- */}
          {reservation.status === 'Approved' && (
            <section className="card">
              <div className="card-header">
                <h2 className="flex items-center gap-2 text-base font-semibold text-slate-900">
                  <QrCodeIcon className="h-4 w-4 text-slate-500" aria-hidden="true" />
                  QR code
                </h2>
              </div>
              <div className="card-body">
                {qrLoading && (
                  <div className="flex justify-center py-8">
                    <Spinner size="lg" />
                  </div>
                )}

                {!qrLoading && qrError && <ErrorState message={qrError} />}

                {!qrLoading && !qrError && !qr && (
                  <p className="text-sm text-slate-500">
                    No QR code has been issued for this reservation yet.
                  </p>
                )}

                {!qrLoading && !qrError && qr && (
                  <div className="space-y-3">
                    <img
                      src={qrDataUri}
                      alt={`QR code for reservation ${qr.reservationNo}`}
                      className="w-full max-w-[220px] mx-auto"
                    />
                    <p className="text-center font-mono text-sm font-semibold text-slate-800">
                      {qr.reservationNo}
                    </p>
                    <a className="btn-secondary btn-sm w-full" href={qrDataUri} download={`${qr.reservationNo}.png`}>
                      <Download className="h-3.5 w-3.5" aria-hidden="true" />
                      Download PNG
                    </a>
                    {/* The token means nothing on its own: the mobile scan is posted to
                        /reservations/verify-qr and the API decides whether it is valid. */}
                    <p className="text-xs text-slate-500">
                      A grid operator scans this in the mobile app. The scanned token is sent back
                      to the API, which verifies it and finalises the energy transfer.
                    </p>
                  </div>
                )}
              </div>
            </section>
          )}
        </div>
      </div>

      {/* ---- Reject dialog ---- */}
      <Modal
        open={rejectOpen}
        title="Reject reservation"
        size="sm"
        onClose={() => setRejectOpen(false)}
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={() => setRejectOpen(false)}
              disabled={busy === 'reject'}
            >
              Keep pending
            </button>
            <button
              type="button"
              className="btn-danger"
              onClick={() => void handleReject()}
              disabled={busy === 'reject'}
            >
              {busy === 'reject' && <Spinner size="sm" />}
              Reject booking
            </button>
          </>
        }
      >
        <label className="label" htmlFor="reject-reason">
          Reason for refusal
        </label>
        <textarea
          id="reject-reason"
          rows={3}
          className={rejectError ? 'input input-error' : 'input'}
          value={rejectReason}
          placeholder="For example: the node is offline for maintenance that morning."
          onChange={(event) => {
            setRejectReason(event.target.value);
            setRejectError(null);
          }}
        />
        {rejectError && <p className="field-error">{rejectError}</p>}
        <p className="mt-2 text-xs text-slate-500">
          The prosumer sees this wording on {reservation.reservationNo} in the mobile app.
        </p>
      </Modal>

      {/* ---- Edit dialog ---- */}
      <Modal
        open={editOpen}
        title="Edit reservation"
        onClose={() => setEditOpen(false)}
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={() => setEditOpen(false)}
              disabled={busy === 'edit'}
            >
              Discard
            </button>
            <button
              type="button"
              className="btn-primary"
              onClick={() => void handleEdit()}
              disabled={busy === 'edit' || slotsLoading}
            >
              {busy === 'edit' && <Spinner size="sm" />}
              Save changes
            </button>
          </>
        }
      >
        <div className="space-y-4">
          <div>
            <label className="label" htmlFor="edit-slot">
              Booking slot
            </label>
            {slotsLoading ? (
              <div className="flex items-center gap-2 py-2 text-sm text-slate-500">
                <Spinner size="sm" />
                Loading the slots the API reports as bookable...
              </div>
            ) : slotsError ? (
              <ErrorState message={slotsError} />
            ) : (
              <select
                id="edit-slot"
                className="select"
                value={editSlotId}
                onChange={(event) => {
                  setEditSlotId(event.target.value);
                  setEditError(null);
                }}
              >
                <option value="">Keep the current slot ({reservation.slotCode})</option>
                {/* Only slots the API flagged isBookable are offered (rule R8). */}
                {slots.map((slot) => (
                  <option key={slot.id} value={slot.id}>
                    {slot.slotCode} - {formatDate(slot.startUtc)} {slot.startTime} to {slot.endTime}
                    {' - '}
                    {slot.remainingCapacity} free
                  </option>
                ))}
              </select>
            )}
            {!slotsLoading && !slotsError && slots.length === 0 && (
              <p className="mt-1 text-xs text-slate-500">
                The API is not offering any other bookable slot at {reservation.stationCode} right
                now, so only the energy amount can be changed.
              </p>
            )}
          </div>

          <div>
            <label className="label" htmlFor="edit-energy">
              Energy amount (kWh)
            </label>
            <input
              id="edit-energy"
              type="number"
              min="0.1"
              step="0.1"
              className={editError ? 'input input-error' : 'input'}
              value={editEnergy}
              onChange={(event) => {
                setEditEnergy(event.target.value);
                setEditError(null);
              }}
            />
            {editError && <p className="field-error">{editError}</p>}
          </div>

          {/* The dialog only assembles a request; the API re-checks rule R2 and the
              slot capacity before it accepts the change. */}
          <p className="text-xs text-slate-500">
            The service checks the 12 hour notice period and the slot capacity again when this is
            saved, and will refuse the change with its own message if either rule blocks it.
          </p>
        </div>
      </Modal>

      {/* ---- Cancel confirmation ---- */}
      {/* A plain confirm dialog cannot carry the optional reason the API accepts, so the
          same confirm layout is built on Modal instead. */}
      <Modal
        open={cancelOpen}
        title="Cancel reservation"
        size="sm"
        onClose={() => setCancelOpen(false)}
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={() => setCancelOpen(false)}
              disabled={busy === 'cancel'}
            >
              Keep reservation
            </button>
            <button
              type="button"
              className="btn-danger"
              onClick={() => void handleCancel()}
              disabled={busy === 'cancel'}
            >
              {busy === 'cancel' && <Spinner size="sm" />}
              Cancel reservation
            </button>
          </>
        }
      >
        <p className="text-sm text-slate-600">
          Cancel {reservation.reservationNo} at {reservation.stationCode} on{' '}
          {formatDate(reservation.reservationStartUtc)}? The slot is released back to the node.
        </p>
        <div className="mt-4">
          <label className="label" htmlFor="cancel-reason">
            Reason (optional)
          </label>
          <textarea
            id="cancel-reason"
            rows={3}
            className="input"
            value={cancelReason}
            placeholder="Recorded on the booking for the audit trail."
            onChange={(event) => setCancelReason(event.target.value)}
          />
        </div>
      </Modal>
    </div>
  );
}
