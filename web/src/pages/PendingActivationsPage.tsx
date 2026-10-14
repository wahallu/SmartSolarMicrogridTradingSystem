/* =============================================================================
 * File        : src/pages/PendingActivationsPage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Pages / Pending Activations
 * Description : Handles pending registrations and deactivation requests.
 * Author      : IT22049872 - Chathushka
 * Created     : 2026-09-08
 * =============================================================================
 */

import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  AlertTriangle,
  CalendarClock,
  CheckCircle2,
  Info,
  Mail,
  MapPin,
  Phone,
  RefreshCw,
  UserCheck,
  UserX,
} from 'lucide-react';
import { prosumersApi } from '../api/services';
import { ApiError } from '../api/client';
import {
  Badge,
  EmptyState,
  ErrorState,
  Modal,
  PageLoader,
  Spinner,
  useToast,
} from '../components/ui';
import { formatDate, formatDateTime } from '../utils/format';
import type { User } from '../types';

/** Turns any thrown value into the sentence a user should read. */
function describeError(err: unknown): string {
  // An ApiError already carries the API's own wording, which is always preferred.
  return err instanceof ApiError ? err.message : 'Something went wrong';
}

/** The NIC that identifies a prosumer, falling back to the mirrored username. */
function nicOf(prosumer: User): string {
  // The contract stores the NIC in username as well, so this is never empty for
  // a Prosumer account.
  return prosumer.nic ?? prosumer.username;
}

/** First letter of a name, used for the avatar circle. */
function initialOf(fullName: string): string {
  // A blank name would otherwise render an empty circle.
  const trimmed = fullName.trim();
  return trimmed.length > 0 ? trimmed.charAt(0).toUpperCase() : '?';
}

/** The back office queue of accounts waiting for an activation decision. */
export default function PendingActivationsPage() {
  // Backoffice only - App.tsx guards the route and the API refuses every other
  // role as well, so the restriction is never re-implemented in the browser.
  const navigate = useNavigate();
  const toast = useToast();

  /* ---- queue state ---- */
  const [queue, setQueue] = useState<User[]>([]);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState<number>(0);

  /* ---- card action state ---- */
  const [busyNic, setBusyNic] = useState<string | null>(null);
  const [approveTarget, setApproveTarget] = useState<User | null>(null);
  const [approveReason, setApproveReason] = useState<string>('');

  // Asks the effect below to run again, both for the refresh button and after an
  // action has changed an entry.
  function reload() {
    setReloadKey((current) => current + 1);
  }

  // Loads the queue. The cancelled guard stops a slow reply from writing into a
  // component that has already been unmounted.
  useEffect(() => {
    let cancelled = false;

    async function loadQueue() {
      setLoading(true);
      setError(null);

      try {
        // One call returns both kinds of entry - the server decides what belongs
        // in the queue, the browser never assembles it from separate lists.
        const pending = await prosumersApi.pending();
        if (!cancelled) setQueue(pending);
      } catch (err) {
        if (!cancelled) {
          setQueue([]);
          setError(describeError(err));
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void loadQueue();
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  // Activates an account (rule R5, Backoffice only). The same call also clears
  // the deactivationRequested flag on the server, which is why this doubles as
  // the "Dismiss request" action further down.
  async function handleActivate(prosumer: User) {
    const nic = nicOf(prosumer);
    setBusyNic(nic);

    try {
      const { message } = await prosumersApi.activate(nic);
      toast.success(message);
      reload();
    } catch (err) {
      toast.error(describeError(err));
    } finally {
      setBusyNic(null);
    }
  }

  // Approves a prosumer's own deactivation request, passing on the optional
  // reason. The API records it and decides whether the caller may act at all.
  async function handleApproveDeactivation() {
    if (!approveTarget) return;

    const nic = nicOf(approveTarget);
    const reason = approveReason.trim();
    setBusyNic(nic);

    try {
      const { message } = await prosumersApi.deactivate(nic, reason === '' ? undefined : reason);
      toast.success(message);
      setApproveTarget(null);
      setApproveReason('');
      reload();
    } catch (err) {
      toast.error(describeError(err));
    } finally {
      setBusyNic(null);
    }
  }

  // Both counters are read off the loaded entries; neither is a business rule.
  const awaitingCount = queue.filter((entry) => entry.status === 'Pending').length;
  const deactivationCount = queue.filter((entry) => entry.deactivationRequested).length;

  return (
    <div className="space-y-5">
      {/* ---- Page header ---- */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-xl font-semibold text-slate-900 sm:text-2xl">Pending activations</h1>
          <p className="mt-1 text-sm text-slate-500">
            The back office work queue. Every account below is waiting for an activation decision.
          </p>
        </div>
        <button type="button" className="btn-secondary" onClick={reload} disabled={loading}>
          {loading ? (
            <Spinner size="sm" />
          ) : (
            <RefreshCw className="h-4 w-4" aria-hidden="true" />
          )}
          Refresh
        </button>
      </div>

      {/* ---- What this queue contains ---- */}
      <div className="rounded-xl border border-slate-200 bg-slate-50">
        <div className="card-body flex flex-col items-start gap-3 sm:flex-row">
          <Info className="mt-0.5 h-5 w-5 shrink-0 text-slate-500" aria-hidden="true" />
          <div className="text-sm text-slate-600">
            <p>Two kinds of request wait in this queue:</p>
            <ul className="mt-2 list-disc space-y-1 pl-5">
              <li>
                Prosumers who registered themselves from the mobile app. A self-registered account
                starts <span className="font-medium text-slate-700">Pending</span> and cannot sign
                in until a back office officer activates it.
              </li>
              <li>
                Active prosumers who have requested deactivation from the mobile app. Approving the
                request closes the account; dismissing it leaves the prosumer active.
              </li>
            </ul>
          </div>
        </div>
      </div>

      {/* ---- Summary chips ---- */}
      <div className="flex flex-wrap items-center gap-3">
        <span className="inline-flex items-center gap-2 rounded-full border border-amber-200 bg-amber-50 px-3.5 py-1.5 text-sm font-semibold text-amber-800">
          <UserCheck className="h-4 w-4" aria-hidden="true" />
          {awaitingCount} awaiting activation
        </span>
        <span className="inline-flex items-center gap-2 rounded-full border border-red-200 bg-red-50 px-3.5 py-1.5 text-sm font-semibold text-red-800">
          <UserX className="h-4 w-4" aria-hidden="true" />
          {deactivationCount} deactivation request{deactivationCount === 1 ? '' : 's'}
        </span>
      </div>

      {/* ---- Queue ---- */}
      {error && <ErrorState message={error} onRetry={reload} />}

      {!error && loading && (
        <div className="card">
          <PageLoader />
        </div>
      )}

      {!error && !loading && queue.length === 0 && (
        <div className="card">
          <EmptyState
            icon={CheckCircle2}
            title="Nothing waiting"
            message="No prosumer is waiting for activation and no deactivation request is open. New self-registrations and mobile app requests will appear here."
            action={
              <button type="button" className="btn-secondary" onClick={reload}>
                <RefreshCw className="h-4 w-4" aria-hidden="true" />
                Check again
              </button>
            }
          />
        </div>
      )}

      {!error && !loading && queue.length > 0 && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {queue.map((entry) => {
            // status and deactivationRequested are server state; the card only
            // decides which buttons to draw from them.
            const nic = nicOf(entry);
            const busy = busyNic === nic;
            const isPending = entry.status === 'Pending';

            return (
              <div key={entry.id} className="card flex flex-col">
                <div className="card-body flex-1 space-y-4">
                  {/* ---- Identity ---- */}
                  <div className="flex items-start gap-3">
                    <div
                      className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-amber-100 text-base font-semibold text-amber-800"
                      aria-hidden="true"
                    >
                      {initialOf(entry.fullName)}
                    </div>
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-semibold text-slate-900">
                        {entry.fullName}
                      </p>
                      <p className="mt-0.5 truncate font-mono text-xs font-medium text-slate-600">
                        {nic}
                      </p>
                    </div>
                    <Badge status={entry.status} kind="account" />
                  </div>

                  {/* ---- Contact details ---- */}
                  <div className="space-y-1.5 text-sm text-slate-600">
                    <p className="flex items-start gap-2">
                      <Mail className="mt-0.5 h-4 w-4 shrink-0 text-slate-500" aria-hidden="true" />
                      <span className="min-w-0 break-words">{entry.email}</span>
                    </p>
                    <p className="flex items-start gap-2">
                      <Phone className="mt-0.5 h-4 w-4 shrink-0 text-slate-500" aria-hidden="true" />
                      <span className="min-w-0 break-words">{entry.phone}</span>
                    </p>
                    <p className="flex items-start gap-2">
                      <MapPin className="mt-0.5 h-4 w-4 shrink-0 text-slate-500" aria-hidden="true" />
                      <span className="min-w-0 break-words">{entry.address}</span>
                    </p>
                  </div>

                  <p className="text-xs font-medium text-slate-500">
                    Registered {formatDate(entry.createdAt)}
                  </p>

                  {/* ---- The prosumer's own deactivation request ---- */}
                  {entry.deactivationRequested && (
                    <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2.5">
                      <p className="flex items-center gap-2 text-xs font-semibold text-amber-800">
                        <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
                        Deactivation requested
                      </p>
                      <p className="mt-1.5 text-sm text-amber-900">
                        {entry.deactivationReason ?? 'No reason given'}
                      </p>
                      <p className="mt-1 text-xs text-amber-700">
                        Asked on {formatDateTime(entry.deactivationRequestedAt)}
                      </p>
                    </div>
                  )}
                </div>

                {/* ---- Actions ---- */}
                <div className="flex flex-wrap items-center gap-2 border-t border-slate-200 px-5 py-3">
                  {isPending && (
                    <button
                      type="button"
                      className="btn-success btn-sm"
                      onClick={() => void handleActivate(entry)}
                      disabled={busy}
                    >
                      {busy ? (
                        <Spinner size="sm" />
                      ) : (
                        <UserCheck className="h-3.5 w-3.5" aria-hidden="true" />
                      )}
                      Activate account
                    </button>
                  )}

                  {entry.deactivationRequested && (
                    <>
                      <button
                        type="button"
                        className="btn-danger-quiet btn-sm"
                        onClick={() => {
                          setApproveTarget(entry);
                          setApproveReason(entry.deactivationReason ?? '');
                        }}
                        disabled={busy}
                      >
                        <UserX className="h-3.5 w-3.5" aria-hidden="true" />
                        Approve deactivation
                      </button>

                      {/* Activating clears deactivationRequested on the server,
                          so the same endpoint is what "dismiss" means here. */}
                      <button
                        type="button"
                        className="btn-secondary btn-sm"
                        onClick={() => void handleActivate(entry)}
                        disabled={busy}
                      >
                        {busy ? (
                          <Spinner size="sm" />
                        ) : (
                          <UserCheck className="h-3.5 w-3.5" aria-hidden="true" />
                        )}
                        Dismiss request
                      </button>
                    </>
                  )}

                  <button
                    type="button"
                    className="btn-ghost btn-sm"
                    onClick={() => navigate(`/reservations?nic=${encodeURIComponent(nic)}`)}
                  >
                    <CalendarClock className="h-3.5 w-3.5" aria-hidden="true" />
                    View bookings
                  </button>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* ---- Approve deactivation ----
          Shaped exactly like a ConfirmDialog, but built from a Modal because a
          ConfirmDialog cannot carry the optional reason field. */}
      <Modal
        open={approveTarget !== null}
        title="Approve deactivation"
        size="sm"
        onClose={() => setApproveTarget(null)}
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={() => setApproveTarget(null)}
              disabled={busyNic !== null}
            >
              Cancel
            </button>
            <button
              type="button"
              className="btn-danger"
              onClick={() => void handleApproveDeactivation()}
              disabled={busyNic !== null}
            >
              {busyNic !== null && <Spinner size="sm" />}
              Approve deactivation
            </button>
          </>
        }
      >
        <p className="text-sm text-slate-600">
          {approveTarget?.fullName ?? 'This prosumer'} asked to close their account. Once approved
          they can no longer sign in to the mobile app, and only a back office officer can
          reactivate the account afterwards.
        </p>

        <div className="mt-4">
          <label className="label" htmlFor="approve-deactivation-reason">
            Reason (optional)
          </label>
          <textarea
            id="approve-deactivation-reason"
            rows={3}
            className="input"
            value={approveReason}
            placeholder="Recorded on the account for the audit trail."
            onChange={(event) => setApproveReason(event.target.value)}
          />
        </div>
      </Modal>
    </div>
  );
}
