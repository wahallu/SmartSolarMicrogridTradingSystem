/**
 * =============================================================================
 * File        : src/pages/UsersPage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : Back office web user management - the create, edit, activate,
 *               deactivate, reset password and delete screen for Backoffice and
 *               Grid Operator accounts. This serves the "EV Owner / web user
 *               management" marking criterion. Rule R10 (only Backoffice may
 *               touch these records) and the self delete guard live in the C#
 *               API; this screen only calls the endpoints and shows the verdict.
 * Author      : Chathushka - Identity, Access Control & Local Session (SE4040 EAD)
 * Created     : 2026-09-04
 * =============================================================================
 */

import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import {
  KeyRound,
  Pencil,
  Plus,
  ShieldCheck,
  ShieldOff,
  Trash2,
  Users as UsersIcon,
} from 'lucide-react';
import { stationsApi, usersApi } from '../api/services';
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
import { formatDateTime } from '../utils/format';
import type { AccountStatus, PagedResult, Station, User, UserRole } from '../types';

/** How many rows the list asks the API for at a time. */
const PAGE_SIZE = 10;

/**
 * The two roles this screen may create. Solar prosumers are deliberately absent:
 * they are keyed by NIC and are created on the Prosumers page (or by self
 * registration, which the API starts as Pending under rule R6), so mixing them
 * into this form would send a payload the /users endpoint does not accept.
 */
const WEB_ROLES: UserRole[] = ['Backoffice', 'GridOperator'];

/** The account states the status filter and the edit form offer. */
const STATUS_OPTIONS: AccountStatus[] = ['Pending', 'Active', 'Deactivated'];

/** Recorded against the account so the audit trail shows where the block came from. */
const DEACTIVATE_REASON = 'Deactivated by a back office officer from the web console.';

/** Chip colours per role, written out in full so Tailwind's scanner keeps them. */
const ROLE_CHIPS: Record<string, string> = {
  Backoffice: 'bg-violet-100 text-violet-700',
  GridOperator: 'bg-blue-100 text-blue-700',
  Prosumer: 'bg-solar-100 text-solar-700',
};

/** Readable wording for a role value. */
const ROLE_LABELS: Record<string, string> = {
  Backoffice: 'Back office',
  GridOperator: 'Grid operator',
  Prosumer: 'Solar prosumer',
};

/** Shape of the create form while it is being filled in. */
interface CreateFormState {
  username: string;
  email: string;
  fullName: string;
  phone: string;
  address: string;
  password: string;
  role: UserRole;
  assignedStationId: string;
}

/** Shape of the edit form while it is being filled in. */
interface EditFormState {
  email: string;
  fullName: string;
  phone: string;
  address: string;
  role: UserRole;
  assignedStationId: string;
  status: AccountStatus;
}

/** The blank create form. */
const EMPTY_CREATE_FORM: CreateFormState = {
  username: '',
  email: '',
  fullName: '',
  phone: '',
  address: '',
  password: '',
  role: 'GridOperator',
  assignedStationId: '',
};

/** Turns any thrown value into the sentence a user should read. */
function describeError(err: unknown): string {
  // An ApiError already carries the API's own wording, which is always preferred
  // because the API is the component that owns the rule that just fired.
  return err instanceof ApiError ? err.message : 'Something went wrong';
}

/** Picks one message out of the API's field error map, ignoring key casing. */
function fieldError(errors: Record<string, string[]> | null, field: string): string | undefined {
  // The C# model binder capitalises property names, so the lookup is case
  // insensitive and works whether the server sends "email" or "Email".
  if (!errors) return undefined;
  const key = Object.keys(errors).find((name) => name.toLowerCase() === field.toLowerCase());
  return key && errors[key].length > 0 ? errors[key][0] : undefined;
}

/** Renders a small coloured chip for a role value. */
function RoleChip({ role }: { role: UserRole }) {
  // The role is a server owned field; this only chooses a colour for it.
  const tone = ROLE_CHIPS[role] ?? 'bg-slate-100 text-slate-600';
  return (
    <span className={`badge ${tone}`}>{ROLE_LABELS[role] ?? role}</span>
  );
}

/** The web user list with its filters, dialogs and row actions. */
export default function UsersPage() {
  // Only a Backoffice officer reaches this route; the API re-checks rule R10 on
  // every call, so nothing here is trusted as a security control on its own.
  const toast = useToast();
  const { user: signedInUser } = useAuth();

  /* ---- filter state ---- */
  const [searchInput, setSearchInput] = useState<string>('');
  const [search, setSearch] = useState<string>('');
  const [role, setRole] = useState<string>('');
  const [status, setStatus] = useState<string>('');
  const [page, setPage] = useState<number>(1);

  /* ---- list state ---- */
  const [result, setResult] = useState<PagedResult<User> | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState<number>(0);

  /* ---- lookup data ---- */
  const [stations, setStations] = useState<Station[]>([]);

  /* ---- row action state ---- */
  const [busyId, setBusyId] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<User | null>(null);

  /* ---- create dialog state ---- */
  const [createOpen, setCreateOpen] = useState<boolean>(false);
  const [createForm, setCreateForm] = useState<CreateFormState>(EMPTY_CREATE_FORM);
  const [createError, setCreateError] = useState<string | null>(null);
  const [createFieldErrors, setCreateFieldErrors] = useState<Record<string, string[]> | null>(null);

  /* ---- edit dialog state ---- */
  const [editTarget, setEditTarget] = useState<User | null>(null);
  const [editForm, setEditForm] = useState<EditFormState | null>(null);
  const [editError, setEditError] = useState<string | null>(null);
  const [editFieldErrors, setEditFieldErrors] = useState<Record<string, string[]> | null>(null);

  /* ---- reset password dialog state ---- */
  const [resetTarget, setResetTarget] = useState<User | null>(null);
  const [newPassword, setNewPassword] = useState<string>('');
  const [resetError, setResetError] = useState<string | null>(null);

  /* ---- shared submit flag for the dialogs ---- */
  const [submitting, setSubmitting] = useState<boolean>(false);

  // Asks the list effect to run again after an action has changed a row.
  function reload() {
    setReloadKey((current) => current + 1);
  }

  // Debounces the search box so a request is not fired on every keystroke. The
  // page returns to 1 because a new search produces a different result set.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSearch(searchInput.trim());
      setPage(1);
    }, 400);

    return () => window.clearTimeout(timer);
  }, [searchInput]);

  // Loads the node list once so both dialogs can offer an assignment dropdown.
  useEffect(() => {
    let cancelled = false;

    async function loadStations() {
      try {
        const paged = await stationsApi.list({ pageSize: 100 });
        if (!cancelled) setStations(paged.items);
      } catch {
        // A missing node list only costs the dropdown its options, so it is not
        // promoted to a page level error.
      }
    }

    void loadStations();
    return () => {
      cancelled = true;
    };
  }, []);

  // Re-runs whenever a filter or the page changes. The cancelled guard stops a
  // slow reply from writing into a component that has already unmounted.
  useEffect(() => {
    let cancelled = false;

    async function loadUsers() {
      setLoading(true);
      setError(null);

      try {
        // Searching and filtering are done by the API - the browser never filters
        // a page of rows it has already been handed.
        const paged = await usersApi.list({ search, role, status, page, pageSize: PAGE_SIZE });
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

    void loadUsers();
    return () => {
      cancelled = true;
    };
  }, [search, role, status, page, reloadKey]);

  // Returns every filter to its default and goes back to the first page.
  function clearFilters() {
    setSearchInput('');
    setSearch('');
    setRole('');
    setStatus('');
    setPage(1);
  }

  // Opens the create dialog on a clean form.
  function openCreate() {
    setCreateForm(EMPTY_CREATE_FORM);
    setCreateError(null);
    setCreateFieldErrors(null);
    setCreateOpen(true);
  }

  // Fills the edit dialog from the row the officer clicked.
  function openEdit(target: User) {
    setEditTarget(target);
    setEditForm({
      email: target.email,
      fullName: target.fullName,
      phone: target.phone,
      address: target.address,
      role: target.role,
      assignedStationId: target.assignedStationId ?? '',
      status: target.status,
    });
    setEditError(null);
    setEditFieldErrors(null);
  }

  // Opens the reset password dialog for a row.
  function openReset(target: User) {
    setResetTarget(target);
    setNewPassword('');
    setResetError(null);
  }

  // Creates a Backoffice or Grid Operator account.
  async function handleCreate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setCreateError(null);
    setCreateFieldErrors(null);

    try {
      // The node is sent only for an operator; every other validation - unique
      // username, password policy, role permission - is the API's decision.
      const { message } = await usersApi.create({
        username: createForm.username.trim(),
        email: createForm.email.trim(),
        fullName: createForm.fullName.trim(),
        phone: createForm.phone.trim(),
        address: createForm.address.trim(),
        password: createForm.password,
        role: createForm.role,
        assignedStationId:
          createForm.role === 'GridOperator' && createForm.assignedStationId !== ''
            ? createForm.assignedStationId
            : undefined,
      });
      toast.success(message);
      setCreateOpen(false);
      setCreateForm(EMPTY_CREATE_FORM);
      reload();
    } catch (err) {
      setCreateError(describeError(err));
      if (err instanceof ApiError) setCreateFieldErrors(err.fieldErrors);
    } finally {
      setSubmitting(false);
    }
  }

  // Saves the edited account.
  async function handleEdit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editTarget || !editForm) return;

    setSubmitting(true);
    setEditError(null);
    setEditFieldErrors(null);

    try {
      const { message } = await usersApi.update(editTarget.id, {
        email: editForm.email.trim(),
        fullName: editForm.fullName.trim(),
        phone: editForm.phone.trim(),
        address: editForm.address.trim(),
        role: editForm.role,
        assignedStationId:
          editForm.role === 'GridOperator' && editForm.assignedStationId !== ''
            ? editForm.assignedStationId
            : null,
        status: editForm.status,
      });
      toast.success(message);
      setEditTarget(null);
      setEditForm(null);
      reload();
    } catch (err) {
      setEditError(describeError(err));
      if (err instanceof ApiError) setEditFieldErrors(err.fieldErrors);
    } finally {
      setSubmitting(false);
    }
  }

  // Sets a new password on the account's behalf.
  async function handleResetPassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!resetTarget) return;

    if (newPassword.trim().length === 0) {
      setResetError('Please type the new password.');
      return;
    }

    setSubmitting(true);
    setResetError(null);

    try {
      // The password strength policy is enforced by the API, so a rejection here
      // is displayed verbatim rather than second guessed in the browser.
      const { message } = await usersApi.resetPassword(resetTarget.id, newPassword);
      toast.success(message);
      setResetTarget(null);
      setNewPassword('');
    } catch (err) {
      setResetError(describeError(err));
    } finally {
      setSubmitting(false);
    }
  }

  // Returns a blocked account to Active (rule R5 - Backoffice only, checked server side).
  async function handleActivate(target: User) {
    setBusyId(target.id);
    try {
      const { message } = await usersApi.activate(target.id);
      toast.success(message);
      reload();
    } catch (err) {
      toast.error(describeError(err));
    } finally {
      setBusyId(null);
    }
  }

  // Blocks an account from signing in.
  async function handleDeactivate(target: User) {
    setBusyId(target.id);
    try {
      const { message } = await usersApi.deactivate(target.id, DEACTIVATE_REASON);
      toast.success(message);
      reload();
    } catch (err) {
      toast.error(describeError(err));
    } finally {
      setBusyId(null);
    }
  }

  // Permanently removes an account.
  async function handleDelete() {
    if (!deleteTarget) return;

    setBusyId(deleteTarget.id);
    try {
      const { message } = await usersApi.remove(deleteTarget.id);
      toast.success(message);
      setDeleteTarget(null);
      reload();
    } catch (err) {
      // The API owns the "an officer may not delete their own account" rule and
      // answers with CANNOT_DELETE_SELF. The button is therefore always offered
      // and the server's refusal is shown, rather than the UI guessing the rule.
      toast.error(describeError(err));
    } finally {
      setBusyId(null);
    }
  }

  const rows: User[] = result?.items ?? [];
  const hasFilters = search !== '' || role !== '' || status !== '';

  return (
    <div className="space-y-5">
      {/* ---- Page header ---- */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-xl font-bold text-slate-900 sm:text-2xl">Web users</h1>
          <p className="mt-1 text-sm text-slate-500">
            Back office officers and grid operators who sign in to the console.
          </p>
        </div>
        <button type="button" className="btn-primary" onClick={openCreate}>
          <Plus className="h-4 w-4" aria-hidden="true" />
          New user
        </button>
      </div>

      {/* ---- Filter criteria ---- */}
      <div className="card">
        <div className="card-body grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <div className="sm:col-span-2 lg:col-span-1">
            {/* SearchInput renders its own input, so a caption is used rather than
                a label pointing at an id this component does not own. */}
            <p className="label">Search</p>
            <SearchInput
              value={searchInput}
              onChange={setSearchInput}
              placeholder="Username, name or e-mail"
            />
          </div>

          <div>
            <label className="label" htmlFor="user-role">
              Role
            </label>
            <select
              id="user-role"
              className="select"
              value={role}
              onChange={(event) => {
                setRole(event.target.value);
                setPage(1);
              }}
            >
              <option value="">All roles</option>
              {WEB_ROLES.map((option) => (
                <option key={option} value={option}>
                  {ROLE_LABELS[option] ?? option}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="label" htmlFor="user-status">
              Status
            </label>
            <select
              id="user-status"
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

        <div className="flex items-center justify-between gap-3 border-t border-slate-200 px-5 py-3">
          <p className="text-xs font-medium text-slate-500">
            {result ? `${result.totalCount} matching user(s)` : 'Applying filters...'}
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
              icon={UsersIcon}
              title="No web users found"
              message={
                hasFilters
                  ? 'No account matches the current filter criteria. Try a different role or status.'
                  : 'No back office or grid operator accounts have been created yet.'
              }
              action={
                hasFilters ? (
                  <button type="button" className="btn-secondary" onClick={clearFilters}>
                    Clear filters
                  </button>
                ) : (
                  <button type="button" className="btn-primary" onClick={openCreate}>
                    <Plus className="h-4 w-4" aria-hidden="true" />
                    New user
                  </button>
                )
              }
            />
          )}

          {!loading && rows.length > 0 && (
            <>
              <div className="overflow-x-auto">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Username</th>
                      <th>Full name</th>
                      <th>E-mail</th>
                      <th>Role</th>
                      <th>Assigned node</th>
                      <th>Status</th>
                      <th>Last login</th>
                      <th className="text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((account) => {
                      // status is a server owned field, so the Activate / Deactivate
                      // pair is chosen from it rather than from any local guess.
                      const isSelf = signedInUser?.id === account.id;
                      const busy = busyId === account.id;

                      return (
                        <tr key={account.id}>
                          <td className="whitespace-nowrap font-semibold text-slate-900">
                            {account.username}
                            {isSelf && (
                              <span className="ml-2 text-xs font-medium text-slate-400">(you)</span>
                            )}
                          </td>
                          <td className="whitespace-nowrap">{account.fullName}</td>
                          <td className="whitespace-nowrap text-slate-600">{account.email}</td>
                          <td>
                            <RoleChip role={account.role} />
                          </td>
                          <td className="whitespace-nowrap">
                            {account.assignedStationCode ?? '-'}
                          </td>
                          <td>
                            <Badge status={account.status} kind="account" />
                          </td>
                          <td className="whitespace-nowrap text-slate-600">
                            {formatDateTime(account.lastLoginAt)}
                          </td>
                          <td>
                            <div className="flex flex-wrap items-center justify-end gap-2">
                              <button
                                type="button"
                                className="btn-secondary btn-sm"
                                onClick={() => openEdit(account)}
                                disabled={busy}
                              >
                                <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                                Edit
                              </button>

                              <button
                                type="button"
                                className="btn-secondary btn-sm"
                                onClick={() => openReset(account)}
                                disabled={busy}
                              >
                                <KeyRound className="h-3.5 w-3.5" aria-hidden="true" />
                                Reset password
                              </button>

                              {account.status === 'Active' ? (
                                <button
                                  type="button"
                                  className="btn-secondary btn-sm"
                                  onClick={() => handleDeactivate(account)}
                                  disabled={busy}
                                >
                                  {busy ? (
                                    <Spinner size="sm" />
                                  ) : (
                                    <ShieldOff className="h-3.5 w-3.5" aria-hidden="true" />
                                  )}
                                  Deactivate
                                </button>
                              ) : (
                                <button
                                  type="button"
                                  className="btn-success btn-sm"
                                  onClick={() => handleActivate(account)}
                                  disabled={busy}
                                >
                                  {busy ? (
                                    <Spinner size="sm" />
                                  ) : (
                                    <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
                                  )}
                                  Activate
                                </button>
                              )}

                              {/* Always offered: the API refuses a self delete with
                                  CANNOT_DELETE_SELF and that message is shown. */}
                              <button
                                type="button"
                                className="btn-danger btn-sm"
                                onClick={() => setDeleteTarget(account)}
                                disabled={busy}
                              >
                                <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
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

      {/* ---- Create dialog ---- */}
      <Modal
        open={createOpen}
        title="New web user"
        onClose={() => setCreateOpen(false)}
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={() => setCreateOpen(false)}
              disabled={submitting}
            >
              Cancel
            </button>
            <button type="submit" form="create-user-form" className="btn-primary" disabled={submitting}>
              {submitting && <Spinner size="sm" />}
              Create user
            </button>
          </>
        }
      >
        <form id="create-user-form" className="space-y-4" onSubmit={handleCreate}>
          {createError && (
            <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-700">
              {createError}
            </p>
          )}

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label className="label" htmlFor="create-username">
                Username
              </label>
              <input
                id="create-username"
                className={`input ${fieldError(createFieldErrors, 'username') ? 'input-error' : ''}`}
                value={createForm.username}
                onChange={(event) =>
                  setCreateForm({ ...createForm, username: event.target.value })
                }
                required
              />
              {fieldError(createFieldErrors, 'username') && (
                <p className="field-error">{fieldError(createFieldErrors, 'username')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="create-email">
                E-mail
              </label>
              <input
                id="create-email"
                type="email"
                className={`input ${fieldError(createFieldErrors, 'email') ? 'input-error' : ''}`}
                value={createForm.email}
                onChange={(event) => setCreateForm({ ...createForm, email: event.target.value })}
                required
              />
              {fieldError(createFieldErrors, 'email') && (
                <p className="field-error">{fieldError(createFieldErrors, 'email')}</p>
              )}
            </div>
          </div>

          <div>
            <label className="label" htmlFor="create-fullname">
              Full name
            </label>
            <input
              id="create-fullname"
              className={`input ${fieldError(createFieldErrors, 'fullName') ? 'input-error' : ''}`}
              value={createForm.fullName}
              onChange={(event) => setCreateForm({ ...createForm, fullName: event.target.value })}
              required
            />
            {fieldError(createFieldErrors, 'fullName') && (
              <p className="field-error">{fieldError(createFieldErrors, 'fullName')}</p>
            )}
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label className="label" htmlFor="create-phone">
                Phone
              </label>
              <input
                id="create-phone"
                className={`input ${fieldError(createFieldErrors, 'phone') ? 'input-error' : ''}`}
                value={createForm.phone}
                onChange={(event) => setCreateForm({ ...createForm, phone: event.target.value })}
                required
              />
              {fieldError(createFieldErrors, 'phone') && (
                <p className="field-error">{fieldError(createFieldErrors, 'phone')}</p>
              )}
            </div>

            <div>
              <label className="label" htmlFor="create-password">
                Password
              </label>
              <input
                id="create-password"
                type="password"
                className={`input ${fieldError(createFieldErrors, 'password') ? 'input-error' : ''}`}
                value={createForm.password}
                onChange={(event) => setCreateForm({ ...createForm, password: event.target.value })}
                required
              />
              {fieldError(createFieldErrors, 'password') && (
                <p className="field-error">{fieldError(createFieldErrors, 'password')}</p>
              )}
            </div>
          </div>

          <div>
            <label className="label" htmlFor="create-address">
              Address
            </label>
            <input
              id="create-address"
              className={`input ${fieldError(createFieldErrors, 'address') ? 'input-error' : ''}`}
              value={createForm.address}
              onChange={(event) => setCreateForm({ ...createForm, address: event.target.value })}
              required
            />
            {fieldError(createFieldErrors, 'address') && (
              <p className="field-error">{fieldError(createFieldErrors, 'address')}</p>
            )}
          </div>

          <div>
            <label className="label" htmlFor="create-role">
              Role
            </label>
            <select
              id="create-role"
              className="select"
              value={createForm.role}
              onChange={(event) =>
                setCreateForm({ ...createForm, role: event.target.value as UserRole })
              }
            >
              {WEB_ROLES.map((option) => (
                <option key={option} value={option}>
                  {ROLE_LABELS[option] ?? option}
                </option>
              ))}
            </select>
            <p className="mt-1 text-xs text-slate-500">
              Solar prosumers are keyed by NIC and are created on the Prosumers page.
            </p>
          </div>

          {/* The node assignment only applies to an operator, so it appears with the role. */}
          {createForm.role === 'GridOperator' && (
            <div>
              <label className="label" htmlFor="create-station">
                Assigned node
              </label>
              <select
                id="create-station"
                className="select"
                value={createForm.assignedStationId}
                onChange={(event) =>
                  setCreateForm({ ...createForm, assignedStationId: event.target.value })
                }
              >
                <option value="">No node assigned</option>
                {stations.map((station) => (
                  <option key={station.id} value={station.id}>
                    {station.stationCode} - {station.name}
                  </option>
                ))}
              </select>
            </div>
          )}
        </form>
      </Modal>

      {/* ---- Edit dialog ---- */}
      <Modal
        open={editTarget !== null && editForm !== null}
        title={editTarget ? `Edit ${editTarget.username}` : 'Edit user'}
        onClose={() => {
          setEditTarget(null);
          setEditForm(null);
        }}
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={() => {
                setEditTarget(null);
                setEditForm(null);
              }}
              disabled={submitting}
            >
              Cancel
            </button>
            <button type="submit" form="edit-user-form" className="btn-primary" disabled={submitting}>
              {submitting && <Spinner size="sm" />}
              Save changes
            </button>
          </>
        }
      >
        {editForm && (
          <form id="edit-user-form" className="space-y-4" onSubmit={handleEdit}>
            {editError && (
              <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-700">
                {editError}
              </p>
            )}

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label className="label" htmlFor="edit-email">
                  E-mail
                </label>
                <input
                  id="edit-email"
                  type="email"
                  className={`input ${fieldError(editFieldErrors, 'email') ? 'input-error' : ''}`}
                  value={editForm.email}
                  onChange={(event) => setEditForm({ ...editForm, email: event.target.value })}
                  required
                />
                {fieldError(editFieldErrors, 'email') && (
                  <p className="field-error">{fieldError(editFieldErrors, 'email')}</p>
                )}
              </div>

              <div>
                <label className="label" htmlFor="edit-fullname">
                  Full name
                </label>
                <input
                  id="edit-fullname"
                  className={`input ${fieldError(editFieldErrors, 'fullName') ? 'input-error' : ''}`}
                  value={editForm.fullName}
                  onChange={(event) => setEditForm({ ...editForm, fullName: event.target.value })}
                  required
                />
                {fieldError(editFieldErrors, 'fullName') && (
                  <p className="field-error">{fieldError(editFieldErrors, 'fullName')}</p>
                )}
              </div>
            </div>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label className="label" htmlFor="edit-phone">
                  Phone
                </label>
                <input
                  id="edit-phone"
                  className={`input ${fieldError(editFieldErrors, 'phone') ? 'input-error' : ''}`}
                  value={editForm.phone}
                  onChange={(event) => setEditForm({ ...editForm, phone: event.target.value })}
                  required
                />
                {fieldError(editFieldErrors, 'phone') && (
                  <p className="field-error">{fieldError(editFieldErrors, 'phone')}</p>
                )}
              </div>

              <div>
                <label className="label" htmlFor="edit-address">
                  Address
                </label>
                <input
                  id="edit-address"
                  className={`input ${fieldError(editFieldErrors, 'address') ? 'input-error' : ''}`}
                  value={editForm.address}
                  onChange={(event) => setEditForm({ ...editForm, address: event.target.value })}
                  required
                />
                {fieldError(editFieldErrors, 'address') && (
                  <p className="field-error">{fieldError(editFieldErrors, 'address')}</p>
                )}
              </div>
            </div>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label className="label" htmlFor="edit-role">
                  Role
                </label>
                <select
                  id="edit-role"
                  className="select"
                  value={editForm.role}
                  onChange={(event) =>
                    setEditForm({ ...editForm, role: event.target.value as UserRole })
                  }
                >
                  {WEB_ROLES.map((option) => (
                    <option key={option} value={option}>
                      {ROLE_LABELS[option] ?? option}
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="label" htmlFor="edit-status">
                  Status
                </label>
                <select
                  id="edit-status"
                  className="select"
                  value={editForm.status}
                  onChange={(event) =>
                    setEditForm({ ...editForm, status: event.target.value as AccountStatus })
                  }
                >
                  {STATUS_OPTIONS.map((option) => (
                    <option key={option} value={option}>
                      {option}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            {/* Shown only for an operator - a back office officer has no home node. */}
            {editForm.role === 'GridOperator' && (
              <div>
                <label className="label" htmlFor="edit-station">
                  Assigned node
                </label>
                <select
                  id="edit-station"
                  className="select"
                  value={editForm.assignedStationId}
                  onChange={(event) =>
                    setEditForm({ ...editForm, assignedStationId: event.target.value })
                  }
                >
                  <option value="">No node assigned</option>
                  {stations.map((station) => (
                    <option key={station.id} value={station.id}>
                      {station.stationCode} - {station.name}
                    </option>
                  ))}
                </select>
              </div>
            )}
          </form>
        )}
      </Modal>

      {/* ---- Reset password dialog ---- */}
      <Modal
        open={resetTarget !== null}
        title={resetTarget ? `Reset password for ${resetTarget.username}` : 'Reset password'}
        size="sm"
        onClose={() => setResetTarget(null)}
        footer={
          <>
            <button
              type="button"
              className="btn-secondary"
              onClick={() => setResetTarget(null)}
              disabled={submitting}
            >
              Cancel
            </button>
            <button
              type="submit"
              form="reset-password-form"
              className="btn-primary"
              disabled={submitting}
            >
              {submitting && <Spinner size="sm" />}
              Set password
            </button>
          </>
        }
      >
        <form id="reset-password-form" className="space-y-3" onSubmit={handleResetPassword}>
          <p className="text-sm text-slate-600">
            The user will sign in with this password until they change it themselves.
          </p>

          <div>
            <label className="label" htmlFor="reset-password">
              New password
            </label>
            <input
              id="reset-password"
              type="password"
              className={`input ${resetError ? 'input-error' : ''}`}
              value={newPassword}
              onChange={(event) => {
                setNewPassword(event.target.value);
                setResetError(null);
              }}
              required
            />
            {resetError && <p className="field-error">{resetError}</p>}
          </div>
        </form>
      </Modal>

      {/* ---- Delete confirmation ---- */}
      <ConfirmDialog
        open={deleteTarget !== null}
        title="Delete this user?"
        message={
          deleteTarget
            ? `${deleteTarget.fullName} (${deleteTarget.username}) will be removed permanently. This cannot be undone.`
            : ''
        }
        confirmLabel="Delete user"
        tone="danger"
        busy={busyId !== null && busyId === deleteTarget?.id}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}
