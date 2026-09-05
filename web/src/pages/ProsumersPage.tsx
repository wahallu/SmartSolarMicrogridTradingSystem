/**
 * =============================================================================
 * File        : src/pages/ProsumersPage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : Solar prosumer management for the back office, keyed on the NIC
 *               exactly as the marking scheme requires: create, search, edit,
 *               activate, deactivate and delete an account by its NIC. Every
 *               rule behind those actions (R5 reactivation, R7 NIC uniqueness,
 *               the booking-history delete guard) is decided by the C# API and
 *               this screen only renders the verdict it is handed.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  AlertTriangle,
  CalendarClock,
  Pencil,
  Plus,
  Trash2,
  UserCheck,
  Users,
  UserX,
} from 'lucide-react';
import { prosumersApi } from '../api/services';
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
import { formatDate } from '../utils/format';
import type { AccountStatus, PagedResult, User } from '../types';

/** How many rows the list requests per page. */
const PAGE_SIZE = 10;

/** The account states the status filter offers, mirroring the AccountStatus union. */
const STATUS_OPTIONS: AccountStatus[] = ['Pending', 'Active', 'Deactivated'];

/** The two states a back office officer may give a brand new account. */
const CREATE_STATUS_OPTIONS: AccountStatus[] = ['Active', 'Pending'];

/** Fields the PUT /prosumers/{nic} endpoint accepts. */
interface EditFormState {
  fullName: string;
  email: string;
  phone: string;
  address: string;
}

/** Fields the POST /prosumers endpoint accepts. */
interface CreateFormState {
  nic: string;
  fullName: string;
  email: string;
  phone: string;
  address: string;
  password: string;
  status: AccountStatus;
}

/** The empty create form; a back office created account is Active by default. */
const EMPTY_CREATE: CreateFormState = {
  nic: '',
  fullName: '',
  email: '',
  phone: '',
  address: '',
  password: '',
  status: 'Active',
};

/** Picks one message out of the API's field error map, ignoring key casing. */
function fieldError(errors: Record<string, string[]> | null, field: string): string | undefined {
  // The C# model binder capitalises property names, so a case insensitive lookup
  // keeps this working whether the server sends "email" or "Email".
  if (!errors) return undefined;
  const key = Object.keys(errors).find((name) => name.toLowerCase() === field.toLowerCase());
  return key && errors[key].length > 0 ? errors[key][0] : undefined;
}

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

/** The prosumer register: search, create, edit and the account life cycle actions. */
export default function ProsumersPage() {
  // Grid Operators may read this register, but only a Backoffice officer sees the
  // write actions - rule R10 is enforced by the API as well as hidden here.
  const navigate = useNavigate();
  const toast = useToast();
  const { isBackoffice } = useAuth();

  /* ---- filter state ---- */
  const [searchInput, setSearchInput] = useState<string>('');
  const [search, setSearch] = useState<string>('');
  const [status, setStatus] = useState<string>('');
  const [page, setPage] = useState<number>(1);

  /* ---- list state ---- */
  const [result, setResult] = useState<PagedResult<User> | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState<number>(0);

  /* ---- row action state ---- */
  const [busyNic, setBusyNic] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<User | null>(null);
  const [deactivateTarget, setDeactivateTarget] = useState<User | null>(null);
  const [deactivateReason, setDeactivateReason] = useState<string>('');

  /* ---- edit dialog state ---- */
  const [editTarget, setEditTarget] = useState<User | null>(null);
  const [editForm, setEditForm] = useState<EditFormState>({
    fullName: '',
    email: '',
    phone: '',
    address: '',
  });
  const [editError, setEditError] = useState<string | null>(null);
  const [editFieldErrors, setEditFieldErrors] = useState<Record<string, string[]> | null>(null);
  const [editSaving, setEditSaving] = useState<boolean>(false);

  /* ---- create dialog state ---- */
  const [createOpen, setCreateOpen] = useState<boolean>(false);
  const [createForm, setCreateForm] = useState<CreateFormState>(EMPTY_CREATE);
  const [createError, setCreateError] = useState<string | null>(null);
  const [createFieldErrors, setCreateFieldErrors] = useState<Record<string, string[]> | null>(null);
  const [createNicError, setCreateNicError] = useState<string | null>(null);
  const [creating, setCreating] = useState<boolean>(false);

  // Asks the list effect below to run again after an action has changed a row.
  function reload() {
    setReloadKey((current) => current + 1);
  }

  // Debounces the search box so a NIC is looked up once the officer stops typing
  // rather than on every keystroke. The page returns to 1 as the result set changes.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSearch(searchInput.trim());
      setPage(1);
    }, 400);

    return () => window.clearTimeout(timer);
  }, [searchInput]);

  // Loads a page of prosumers. The cancelled guard stops a slow reply from
  // writing into a component that has already been unmounted.
  useEffect(() => {
    let cancelled = false;

    async function loadProsumers() {
      setLoading(true);
      setError(null);

      try {
        // Searching and paging happen on the server; the browser never filters a
        // page of rows it has already been given.
        const paged = await prosumersApi.list({ search, status, page, pageSize: PAGE_SIZE });
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

    void loadProsumers();
    return () => {
      cancelled = true;
    };
  }, [search, status, page, reloadKey]);

  // Returns every filter to its default and goes back to the first page.
  function clearFilters() {
    setSearchInput('');
    setSearch('');
    setStatus('');
    setPage(1);
  }

  // Opens the edit dialog pre-filled with the row that was clicked.
  function openEdit(prosumer: User) {
    setEditTarget(prosumer);
    setEditForm({
      fullName: prosumer.fullName,
      email: prosumer.email,
      phone: prosumer.phone,
      address: prosumer.address,
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

  // Saves the profile changes against PUT /prosumers/{nic}.
  async function handleEdit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editTarget) return;

    setEditSaving(true);
    setEditError(null);
    setEditFieldErrors(null);

    try {
      const { message } = await prosumersApi.update(nicOf(editTarget), {
        fullName: editForm.fullName,
        email: editForm.email,
        phone: editForm.phone,
        address: editForm.address,
      });
      toast.success(message);
      closeEdit();
      reload();
    } catch (err) {
      setEditError(describeError(err));
      setEditFieldErrors(err instanceof ApiError ? err.fieldErrors : null);
    } finally {
      setEditSaving(false);
    }
  }

  // Reactivates an account. Rule R5 says only a Backoffice officer may do this;
  // the button is hidden for other roles and the API refuses them as well, so the
  // rule is never duplicated in the browser.
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

  // Deactivates an account with the optional reason typed into the dialog. The
  // API records the reason and decides whether the caller is allowed to act.
  async function handleDeactivate() {
    if (!deactivateTarget) return;

    const nic = nicOf(deactivateTarget);
    const reason = deactivateReason.trim();
    setBusyNic(nic);

    try {
      const { message } = await prosumersApi.deactivate(nic, reason === '' ? undefined : reason);
      toast.success(message);
      setDeactivateTarget(null);
      setDeactivateReason('');
      reload();
    } catch (err) {
      toast.error(describeError(err));
    } finally {
      setBusyNic(null);
    }
  }

  // Deletes an account. Whether a prosumer with booking history may be removed is
  // an API decision: a 409 comes back with the server's own explanation, which is
  // shown verbatim rather than second guessed here.
  async function handleDelete() {
    if (!deleteTarget) return;

    const nic = nicOf(deleteTarget);
    setBusyNic(nic);

    try {
      const { message } = await prosumersApi.remove(nic);
      toast.success(message);
      setDeleteTarget(null);
      reload();
    } catch (err) {
      toast.error(describeError(err));
      setDeleteTarget(null);
    } finally {
      setBusyNic(null);
    }
  }

  // Closes and resets the create dialog.
  function closeCreate() {
    setCreateOpen(false);
    setCreateForm(EMPTY_CREATE);
    setCreateError(null);
    setCreateFieldErrors(null);
    setCreateNicError(null);
  }

  // Registers a prosumer from the back office. Rule R7 (NIC uniqueness) belongs to
  // the API: a duplicate returns 409 NIC_ALREADY_EXISTS and that message is placed
  // on the NIC field so the officer can correct it without losing the form.
  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setCreating(true);
    setCreateError(null);
    setCreateFieldErrors(null);
    setCreateNicError(null);

    try {
      const { message } = await prosumersApi.create({
        nic: createForm.nic.trim(),
        fullName: createForm.fullName,
        email: createForm.email,
        phone: createForm.phone,
        address: createForm.address,
        password: createForm.password,
        status: createForm.status,
      });
      toast.success(message);
      closeCreate();
      setPage(1);
      reload();
    } catch (err) {
      setCreateError(describeError(err));
      setCreateFieldErrors(err instanceof ApiError ? err.fieldErrors : null);
      setCreateNicError(
        err instanceof ApiError && err.errorCode === 'NIC_ALREADY_EXISTS' ? err.message : null,
      );
    }finally {
      setCreating(false);
    }
  }

  const rows: User[] = result?.items ?? [];
  const hasFilters = search !== '' || status !== '';
  const nicMessage = createNicError ?? fieldError(createFieldErrors, 'nic');

  return (
    <div className="space-y-5">
      {/* ---- Page header ---- */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-xl font-bold text-slate-900 sm:text-2xl">Solar prosumers</h1>
          <p className="mt-1 text-sm text-slate-500">
            Every prosumer account is identified by its NIC. Search, edit and manage the account life
            cycle from here.
          </p>
        </div>
        {isBackoffice && (
          <button type="button" className="btn-primary" onClick={() => setCreateOpen(true)}>
            <Plus className="h-4 w-4" aria-hidden="true" />
            New prosumer
          </button>
        )}
      </div>

      {/* ---- Filter criteria ---- */}
      <div className="card">
        <div className="card-body grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <div className="sm:col-span-2">
            {/* SearchInput renders its own input element, so a plain caption is
                used here rather than a label pointing at an id it does not own. */}
            <p className="label">Search</p>
            <SearchInput
              value={searchInput}
              onChange={setSearchInput}
              placeholder="NIC, name, e-mail or phone"
            />
          </div>

          <div>
            <label className="label" htmlFor="prosumer-status">
              Account status
            </label>
            <select
              id="prosumer-status"
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
        </div>

        <div className="flex flex-col items-start justify-between gap-3 border-t border-slate-200 px-5 py-3 sm:flex-row sm:items-center">
          <p className="text-xs font-medium text-slate-500">
            {result ? `${result.totalCount} matching prosumer(s)` : 'Applying filters...'}
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
              icon={Users}
              title="No prosumers found"
              message={
                hasFilters
                  ? 'No prosumer matches the current filter criteria. Try a different NIC or clear the filters.'
                  : 'No solar prosumer has been registered yet. Create the first account to get started.'
              }
              action={
                hasFilters ? (
                  <button type="button" className="btn-secondary" onClick={clearFilters}>
                    Clear filters
                  </button>
                ) : isBackoffice ? (
                  <button type="button" className="btn-primary" onClick={() => setCreateOpen(true)}>
                    <Plus className="h-4 w-4" aria-hidden="true" />
                    New prosumer
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
                      <th>NIC</th>
                      <th>Full name</th>
                      <th>Contact</th>
                      <th>Status</th>
                      <th>Deactivation requested</th>
                      <th>Created</th>
                      <th className="text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((prosumer) => {
                      // status and deactivationRequested are both server state; the
                      // UI only chooses which buttons to draw from them.
                      const nic = nicOf(prosumer);
                      const busy = busyNic === nic;
                      const isActive = prosumer.status === 'Active';

                      return (
                        <tr key={prosumer.id}>
                          <td className="whitespace-nowrap font-mono text-sm font-semibold text-slate-900">
                            {nic}
                          </td>
                          <td className="font-medium text-slate-800">{prosumer.fullName}</td>
                          <td>
                            <p className="text-slate-800">{prosumer.email}</p>
                            <p className="text-xs text-slate-500">{prosumer.phone}</p>
                          </td>
                          <td>
                            <Badge status={prosumer.status} kind="account" />
                          </td>
                          <td>
                            {prosumer.deactivationRequested ? (
                              <span className="badge-pending">
                                <AlertTriangle className="h-3 w-3" aria-hidden="true" />
                                Requested
                              </span>
                            ) : (
                              <span className="text-xs text-slate-400">-</span>
                            )}
                          </td>
                          <td className="whitespace-nowrap text-slate-600">
                            {formatDate(prosumer.createdAt)}
                          </td>
                          <td>
                            <div className="flex flex-wrap items-center justify-end gap-2">
                              {isBackoffice && (
                                <button
                                  type="button"
                                  className="btn-secondary btn-sm"
                                  onClick={() => openEdit(prosumer)}
                                  disabled={busy}
                                >
                                  <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                                  Edit
                                </button>
                              )}

                              {/* Rule R5: only a Backoffice officer may reactivate an
                                  account. The button is hidden for every other role and
                                  the API enforces the same restriction server side. */}
                              {isBackoffice && !isActive && (
                                <button
                                  type="button"
                                  className="btn-success btn-sm"
                                  onClick={() => void handleActivate(prosumer)}
                                  disabled={busy}
                                >
                                  {busy ? (
                                    <Spinner size="sm" />
                                  ) : (
                                    <UserCheck className="h-3.5 w-3.5" aria-hidden="true" />
                                  )}
                                  Activate
                                </button>
                              )}

                              {isBackoffice && isActive && (
                                <button
                                  type="button"
                                  className="btn-secondary btn-sm"
                                  onClick={() => {
                                    setDeactivateTarget(prosumer);
                                    setDeactivateReason(prosumer.deactivationReason ?? '');
                                  }}
                                  disabled={busy}
                                >
                                  <UserX className="h-3.5 w-3.5" aria-hidden="true" />
                                  Deactivate
                                </button>
                              )}

                              {isBackoffice && (
                                <button
                                  type="button"
                                  className="btn-danger btn-sm"
                                  onClick={() => setDeleteTarget(prosumer)}
                                  disabled={busy}
                                >
                                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                                  Delete
                                </button>
                              )}

                              <button
                                type="button"
                                className="btn-secondary btn-sm"
                                onClick={() =>
                                  navigate(`/reservations?nic=${encodeURIComponent(nic)}`)
                                }
                              >
                                <CalendarClock className="h-3.5 w-3.5" aria-hidden="true" />
                                Bookings
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

      {/* ---- Edit profile ---- */}
      <Modal
        open={editTarget !== null}
        title={`Edit ${editTarget?.fullName ?? 'prosumer'}`}
        onClose={closeEdit}
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={closeEdit}
              disabled={editSaving}
            >
              Cancel
            </button>
            <button
              type="submit"
              form="edit-prosumer-form"
              className="btn-primary"
              disabled={editSaving}
            >
              {editSaving && <Spinner size="sm" />}
              Save changes
            </button>
          </>
        }
      >
        <form id="edit-prosumer-form" onSubmit={(event) => void handleEdit(event)}>
          {editError && (
            <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-800">
              {editError}
            </div>
          )}

          <p className="mb-4 text-xs text-slate-500">
            NIC{' '}
            <span className="font-mono font-semibold text-slate-700">
              {editTarget ? nicOf(editTarget) : '-'}
            </span>{' '}
            is the account key and cannot be changed.
          </p>

          <div className="space-y-4">
            <div>
              <label className="label" htmlFor="edit-full-name">
                Full name
              </label>
              <input
                id="edit-full-name"
                type="text"
                className={fieldError(editFieldErrors, 'fullName') ? 'input input-error' : 'input'}
                value={editForm.fullName}
                required
                onChange={(event) => setEditForm({ ...editForm, fullName: event.target.value })}
              />
              {fieldError(editFieldErrors, 'fullName') && (
                <p className="field-error">{fieldError(editFieldErrors, 'fullName')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="edit-email">
                E-mail
              </label>
              <input
                id="edit-email"
                type="email"
                className={fieldError(editFieldErrors, 'email') ? 'input input-error' : 'input'}
                value={editForm.email}
                required
                onChange={(event) => setEditForm({ ...editForm, email: event.target.value })}
              />
              {fieldError(editFieldErrors, 'email') && (
                <p className="field-error">{fieldError(editFieldErrors, 'email')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="edit-phone">
                Phone
              </label>
              <input
                id="edit-phone"
                type="tel"
                className={fieldError(editFieldErrors, 'phone') ? 'input input-error' : 'input'}
                value={editForm.phone}
                required
                onChange={(event) => setEditForm({ ...editForm, phone: event.target.value })}
              />
              {fieldError(editFieldErrors, 'phone') && (
                <p className="field-error">{fieldError(editFieldErrors, 'phone')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="edit-address">
                Address
              </label>
              <textarea
                id="edit-address"
                rows={2}
                className={fieldError(editFieldErrors, 'address') ? 'input input-error' : 'input'}
                value={editForm.address}
                required
                onChange={(event) => setEditForm({ ...editForm, address: event.target.value })}
              />
              {fieldError(editFieldErrors, 'address') && (
                <p className="field-error">{fieldError(editFieldErrors, 'address')}</p>
              )}
            </div>
          </div>
        </form>
      </Modal>

      {/* ---- Deactivate with an optional reason ----
          A plain ConfirmDialog cannot carry an input, so the same confirm shape is
          built from a small Modal in order to collect the reason. */}
      <Modal
        open={deactivateTarget !== null}
        title="Deactivate prosumer"
        size="sm"
        onClose={() => setDeactivateTarget(null)}
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={() => setDeactivateTarget(null)}
              disabled={busyNic !== null}
            >
              Cancel
            </button>
            <button
              type="button"
              className="btn-danger"
              onClick={() => void handleDeactivate()}
              disabled={busyNic !== null}
            >
              {busyNic !== null && <Spinner size="sm" />}
              Deactivate account
            </button>
          </>
        }
      >
        <p className="text-sm text-slate-600">
          {deactivateTarget?.fullName ?? 'This prosumer'} will no longer be able to sign in to the
          mobile app. Only a back office officer can reactivate the account afterwards.
        </p>

        <div className="mt-4">
          <label className="label" htmlFor="deactivate-reason">
            Reason (optional)
          </label>
          <textarea
            id="deactivate-reason"
            rows={3}
            className="input"
            value={deactivateReason}
            placeholder="Recorded on the account for the audit trail."
            onChange={(event) => setDeactivateReason(event.target.value)}
          />
        </div>
      </Modal>

      {/* ---- Delete confirmation ---- */}
      <ConfirmDialog
        open={deleteTarget !== null}
        title="Delete prosumer"
        message={`Permanently remove ${deleteTarget?.fullName ?? 'this prosumer'} (${
          deleteTarget ? nicOf(deleteTarget) : ''
        })? The API refuses this if the account carries booking history.`}
        confirmLabel="Delete account"
        tone="danger"
        busy={busyNic !== null}
        onConfirm={() => void handleDelete()}
        onCancel={() => setDeleteTarget(null)}
      />

      {/* ---- New prosumer ---- */}
      <Modal
        open={createOpen}
        title="New prosumer"
        onClose={closeCreate}
        footer={
          <>
            <button type="button" className="btn-secondary" onClick={closeCreate} disabled={creating}>
              Cancel
            </button>
            <button
              type="submit"
              form="create-prosumer-form"
              className="btn-primary"
              disabled={creating}
            >
              {creating && <Spinner size="sm" />}
              Create prosumer
            </button>
          </>
        }
      >
        <form id="create-prosumer-form" onSubmit={(event) => void handleCreate(event)}>
          {createError && (
            <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-800">
              {createError}
            </div>
          )}

          <div className="space-y-4">
            <div>
              <label className="label" htmlFor="create-nic">
                NIC
              </label>
              <input
                id="create-nic"
                type="text"
                className={nicMessage ? 'input input-error font-mono' : 'input font-mono'}
                value={createForm.nic}
                required
                placeholder="e.g. 200112301234"
                onChange={(event) => {
                  setCreateForm({ ...createForm, nic: event.target.value });
                  setCreateNicError(null);
                }}
              />
              {/* Uniqueness is rule R7 and lives in the API - a 409 lands here. */}
              {nicMessage && <p className="field-error">{nicMessage}</p>}
            </div>

            <div>
              <label className="label" htmlFor="create-full-name">
                Full name
              </label>
              <input
                id="create-full-name"
                type="text"
                className={fieldError(createFieldErrors, 'fullName') ? 'input input-error' : 'input'}
                value={createForm.fullName}
                required
                onChange={(event) => setCreateForm({ ...createForm, fullName: event.target.value })}
              />
              {fieldError(createFieldErrors, 'fullName') && (
                <p className="field-error">{fieldError(createFieldErrors, 'fullName')}</p>
              )}
            </div>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label className="label" htmlFor="create-email">
                  E-mail
                </label>
                <input
                  id="create-email"
                  type="email"
                  className={fieldError(createFieldErrors, 'email') ? 'input input-error' : 'input'}
                  value={createForm.email}
                  required
                  onChange={(event) => setCreateForm({ ...createForm, email: event.target.value })}
                />
                {fieldError(createFieldErrors, 'email') && (
                  <p className="field-error">{fieldError(createFieldErrors, 'email')}</p>
                )}
              </div>

              <div>
                <label className="label" htmlFor="create-phone">
                  Phone
                </label>
                <input
                  id="create-phone"
                  type="tel"
                  className={fieldError(createFieldErrors, 'phone') ? 'input input-error' : 'input'}
                  value={createForm.phone}
                  required
                  onChange={(event) => setCreateForm({ ...createForm, phone: event.target.value })}
                />
                {fieldError(createFieldErrors, 'phone') && (
                  <p className="field-error">{fieldError(createFieldErrors, 'phone')}</p>
                )}
              </div>
            </div>

            <div>
              <label className="label" htmlFor="create-address">
                Address
              </label>
              <textarea
                id="create-address"
                rows={2}
                className={fieldError(createFieldErrors, 'address') ? 'input input-error' : 'input'}
                value={createForm.address}
                required
                onChange={(event) => setCreateForm({ ...createForm, address: event.target.value })}
              />
              {fieldError(createFieldErrors, 'address') && (
                <p className="field-error">{fieldError(createFieldErrors, 'address')}</p>
              )}
            </div>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label className="label" htmlFor="create-password">
                  Temporary password
                </label>
                <input
                  id="create-password"
                  type="password"
                  className={
                    fieldError(createFieldErrors, 'password') ? 'input input-error' : 'input'
                  }
                  value={createForm.password}
                  required
                  autoComplete="new-password"
                  onChange={(event) =>
                    setCreateForm({ ...createForm, password: event.target.value })
                  }
                />
                {fieldError(createFieldErrors, 'password') && (
                  <p className="field-error">{fieldError(createFieldErrors, 'password')}</p>
                )}
              </div>

              <div>
                <label className="label" htmlFor="create-status">
                  Initial status
                </label>
                <select
                  id="create-status"
                  className="select"
                  value={createForm.status}
                  onChange={(event) =>
                    setCreateForm({ ...createForm, status: event.target.value as AccountStatus })
                  }
                >
                  {CREATE_STATUS_OPTIONS.map((option) => (
                    <option key={option} value={option}>
                      {option}
                    </option>
                  ))}
                </select>
                {fieldError(createFieldErrors, 'status') && (
                  <p className="field-error">{fieldError(createFieldErrors, 'status')}</p>
                )}
              </div>
            </div>
          </div>

          <p className="mt-4 text-xs text-slate-500">
            A Pending account joins the activation queue instead of signing in straight away, which is
            how a self-registered prosumer arrives (rule R6).
          </p>
        </form>
      </Modal>
    </div>
  );
}
