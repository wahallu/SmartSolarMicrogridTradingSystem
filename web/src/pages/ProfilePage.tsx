/* =============================================================================
 * File        : src/pages/ProfilePage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Pages / Profile
 * Description : Lets the signed-in user review and update account details.
 * Author      : IT22049872 - Chathushka
 * Created     : 2026-09-03
 * =============================================================================
 */

import { useState } from 'react';
import type { FormEvent } from 'react';
import { AlertTriangle, KeyRound, Save, ShieldCheck } from 'lucide-react';
import { authApi, prosumersApi, usersApi } from '../api/services';
import { ApiError } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { Badge, PageLoader, Spinner, useToast } from '../components/ui';
import { formatDate, formatDateTime } from '../utils/format';
import type { UserRole } from '../types';

/** The shortest password the API will accept, mirrored only for instant feedback. */
const MIN_PASSWORD_LENGTH = 6;

/** Chip colours per role, written out in full so Tailwind keeps the classes. */
const ROLE_CHIPS: Record<UserRole, string> = {
  Backoffice: 'bg-violet-100 text-violet-800',
  GridOperator: 'bg-blue-100 text-blue-800',
  Prosumer: 'bg-amber-100 text-amber-800',
};

/** Friendly wording for each role, since "GridOperator" is not a phrase to show. */
const ROLE_LABELS: Record<UserRole, string> = {
  Backoffice: 'Back office officer',
  GridOperator: 'Grid operator',
  Prosumer: 'Solar prosumer',
};

/** The editable half of the profile - exactly what both update endpoints accept. */
interface ProfileFormState {
  fullName: string;
  email: string;
  phone: string;
  address: string;
}

/** The three boxes of the password form. */
interface PasswordFormState {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

/** An empty password form, used to clear the boxes after a successful change. */
const EMPTY_PASSWORD: PasswordFormState = {
  currentPassword: '',
  newPassword: '',
  confirmPassword: '',
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

/** One label / value line of the profile summary. */
function DetailRow({ label, value }: { label: string; value: string }) {
  // Every value shown is server state, rendered exactly as the API sent it.
  return (
    <div className="flex flex-col gap-0.5 border-b border-slate-100 py-2.5 last:border-b-0 sm:flex-row sm:items-baseline sm:justify-between sm:gap-4">
      <dt className="text-xs font-medium text-slate-600">{label}</dt>
      <dd className="break-words text-sm font-medium text-slate-800 sm:text-right">{value}</dd>
    </div>
  );
}

/** The caller's own account: summary, contact details and password change. */
export default function ProfilePage() {
  // The profile itself comes from the auth context, which keeps it in step with
  // GET /auth/me; this screen never holds a second copy of the user.
  const { user, refresh } = useAuth();
  const toast = useToast();

  /* ---- profile form state ---- */
  const [profileForm, setProfileForm] = useState<ProfileFormState>({
    fullName: user?.fullName ?? '',
    email: user?.email ?? '',
    phone: user?.phone ?? '',
    address: user?.address ?? '',
  });
  const [profileError, setProfileError] = useState<string | null>(null);
  const [profileFieldErrors, setProfileFieldErrors] = useState<Record<string, string[]> | null>(
    null,
  );
  const [savingProfile, setSavingProfile] = useState<boolean>(false);

  /* ---- password form state ---- */
  const [passwordForm, setPasswordForm] = useState<PasswordFormState>(EMPTY_PASSWORD);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [currentPasswordError, setCurrentPasswordError] = useState<string | null>(null);
  const [newPasswordError, setNewPasswordError] = useState<string | null>(null);
  const [confirmPasswordError, setConfirmPasswordError] = useState<string | null>(null);
  const [changingPassword, setChangingPassword] = useState<boolean>(false);

  // The context is still validating the stored token, so there is nothing to draw yet.
  if (!user) {
    return <PageLoader />;
  }

  // Saves the contact details. Which endpoint is called depends on how the account
  // is keyed in the contract: a prosumer record is addressed by its NIC
  // (PUT /prosumers/{nic}) while a Backoffice or Grid Operator account is
  // addressed by its identifier (PUT /users/{id}). The role is echoed back on the
  // staff call because that endpoint replaces the whole account document.
  async function handleProfileSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!user) return;

    setSavingProfile(true);
    setProfileError(null);
    setProfileFieldErrors(null);

    const payload = {
      fullName: profileForm.fullName,
      email: profileForm.email,
      phone: profileForm.phone,
      address: profileForm.address,
    };

    try {
      const { message } =
        user.role === 'Prosumer' && user.nic
          ? await prosumersApi.update(user.nic, payload)
          : await usersApi.update(user.id, { ...payload, role: user.role });

      // The API's own wording is shown rather than a sentence invented here.
      toast.success(message);
      // Re-reading the profile keeps the shell header and the sidebar in step.
      await refresh();
    } catch (err) {
      setProfileError(describeError(err));
      setProfileFieldErrors(err instanceof ApiError ? err.fieldErrors : null);
    } finally {
      setSavingProfile(false);
    }
  }

  // Changes the caller's own password. The two checks below are immediate
  // feedback only - the API re-validates length, strength and the current
  // password, and its verdict is what actually decides the outcome.
  async function handlePasswordSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    setPasswordError(null);
    setCurrentPasswordError(null);
    setNewPasswordError(null);
    setConfirmPasswordError(null);

    if (passwordForm.newPassword.length < MIN_PASSWORD_LENGTH) {
      setNewPasswordError(`Use at least ${MIN_PASSWORD_LENGTH} characters.`);
      return;
    }

    if (passwordForm.newPassword !== passwordForm.confirmPassword) {
      setConfirmPasswordError('The two passwords do not match.');
      return;
    }

    setChangingPassword(true);

    try {
      const { message } = await authApi.changePassword(
        passwordForm.currentPassword,
        passwordForm.newPassword,
      );
      toast.success(message);
      setPasswordForm(EMPTY_PASSWORD);
    } catch (err) {
      // A wrong current password is reported by the server with a stable error
      // code, so the message is placed on the field it belongs to.
      if (err instanceof ApiError && err.errorCode === 'WRONG_CURRENT_PASSWORD') {
        setCurrentPasswordError(err.message);
      } else {
        setPasswordError(describeError(err));
      }
    } finally {
      setChangingPassword(false);
    }
  }

  const initial = user.fullName.trim().charAt(0).toUpperCase() || '?';

  return (
    <div className="space-y-5">
      {/* ---- Page header ---- */}
      <div>
        <h1 className="text-xl font-semibold text-slate-900 sm:text-2xl">My profile</h1>
        <p className="mt-1 text-sm text-slate-500">
          Review the account the Smart Solar service holds for you, update your contact details and
          change your password.
        </p>
      </div>

      <div className="grid grid-cols-1 gap-5 lg:grid-cols-3">
        {/* ---- Account summary ---- */}
        <div className="card lg:col-span-1">
          <div className="card-body">
            <div className="flex flex-col items-center text-center">
              <div className="flex h-20 w-20 items-center justify-center rounded-full bg-accent text-3xl font-semibold text-accent-ink">
                {initial}
              </div>
              <h2 className="mt-4 text-lg font-semibold text-slate-900">{user.fullName}</h2>
              <div className="mt-2 flex flex-wrap items-center justify-center gap-2">
                <span
                  className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-semibold ${ROLE_CHIPS[user.role]}`}
                >
                  <ShieldCheck className="h-3 w-3" aria-hidden="true" />
                  {ROLE_LABELS[user.role]}
                </span>
                {/* The account status is a server verdict; it is displayed, never derived. */}
                <Badge status={user.status} kind="account" />
              </div>
            </div>

            <dl className="mt-5 border-t border-slate-200 pt-2">
              <DetailRow label="Username" value={user.username} />
              {user.nic && <DetailRow label="NIC" value={user.nic} />}
              {user.assignedStationCode && (
                <DetailRow label="Assigned node" value={user.assignedStationCode} />
              )}
              <DetailRow label="Last login" value={formatDateTime(user.lastLoginAt)} />
              <DetailRow label="Member since" value={formatDate(user.createdAt)} />
            </dl>

            {/* A deactivation request is raised from the mobile app and cleared by
                the back office; the web app only reports the state it is given. */}
            {user.deactivationRequested && (
              <div className="mt-4 flex items-start gap-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-3">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600" aria-hidden="true" />
                <div>
                  <p className="text-sm font-semibold text-amber-800">
                    Deactivation request pending
                  </p>
                  <p className="mt-1 text-xs text-amber-700">
                    A request to deactivate this account is awaiting Backoffice review. The account
                    stays usable until an officer acts on it.
                  </p>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* ---- Editable details and password ---- */}
        <div className="space-y-5 lg:col-span-2">
          {/* ---- Edit profile ---- */}
          <div className="card">
            <div className="card-header">
              <h2 className="text-base font-semibold text-slate-900">Edit profile</h2>
            </div>
            <form className="card-body" onSubmit={(event) => void handleProfileSubmit(event)}>
              {profileError && (
                <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-800">
                  {profileError}
                </div>
              )}

              <div className="space-y-4">
                <div>
                  <label className="label" htmlFor="profile-full-name">
                    Full name
                  </label>
                  <input
                    id="profile-full-name"
                    type="text"
                    className={
                      fieldError(profileFieldErrors, 'fullName') ? 'input input-error' : 'input'
                    }
                    value={profileForm.fullName}
                    required
                    autoComplete="name"
                    onChange={(event) =>
                      setProfileForm({ ...profileForm, fullName: event.target.value })
                    }
                  />
                  {fieldError(profileFieldErrors, 'fullName') && (
                    <p className="field-error">{fieldError(profileFieldErrors, 'fullName')}</p>
                  )}
                </div>

                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div>
                    <label className="label" htmlFor="profile-email">
                      E-mail
                    </label>
                    <input
                      id="profile-email"
                      type="email"
                      className={
                        fieldError(profileFieldErrors, 'email') ? 'input input-error' : 'input'
                      }
                      value={profileForm.email}
                      required
                      autoComplete="email"
                      onChange={(event) =>
                        setProfileForm({ ...profileForm, email: event.target.value })
                      }
                    />
                    {fieldError(profileFieldErrors, 'email') && (
                      <p className="field-error">{fieldError(profileFieldErrors, 'email')}</p>
                    )}
                  </div>

                  <div>
                    <label className="label" htmlFor="profile-phone">
                      Phone
                    </label>
                    <input
                      id="profile-phone"
                      type="tel"
                      className={
                        fieldError(profileFieldErrors, 'phone') ? 'input input-error' : 'input'
                      }
                      value={profileForm.phone}
                      required
                      autoComplete="tel"
                      onChange={(event) =>
                        setProfileForm({ ...profileForm, phone: event.target.value })
                      }
                    />
                    {fieldError(profileFieldErrors, 'phone') && (
                      <p className="field-error">{fieldError(profileFieldErrors, 'phone')}</p>
                    )}
                  </div>
                </div>

                <div>
                  <label className="label" htmlFor="profile-address">
                    Address
                  </label>
                  <textarea
                    id="profile-address"
                    rows={2}
                    className={
                      fieldError(profileFieldErrors, 'address') ? 'input input-error' : 'input'
                    }
                    value={profileForm.address}
                    required
                    onChange={(event) =>
                      setProfileForm({ ...profileForm, address: event.target.value })
                    }
                  />
                  {fieldError(profileFieldErrors, 'address') && (
                    <p className="field-error">{fieldError(profileFieldErrors, 'address')}</p>
                  )}
                </div>
              </div>

              <div className="mt-5 flex justify-end">
                <button type="submit" className="btn-primary" disabled={savingProfile}>
                  {savingProfile ? (
                    <Spinner size="sm" />
                  ) : (
                    <Save className="h-4 w-4" aria-hidden="true" />
                  )}
                  Save changes
                </button>
              </div>
            </form>
          </div>

          {/* ---- Change password ---- */}
          <div className="card">
            <div className="card-header">
              <h2 className="text-base font-semibold text-slate-900">Change password</h2>
            </div>
            <form className="card-body" onSubmit={(event) => void handlePasswordSubmit(event)}>
              {passwordError && (
                <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-800">
                  {passwordError}
                </div>
              )}

              <div className="space-y-4">
                <div>
                  <label className="label" htmlFor="current-password">
                    Current password
                  </label>
                  <input
                    id="current-password"
                    type="password"
                    className={currentPasswordError ? 'input input-error' : 'input'}
                    value={passwordForm.currentPassword}
                    required
                    autoComplete="current-password"
                    onChange={(event) => {
                      setPasswordForm({ ...passwordForm, currentPassword: event.target.value });
                      setCurrentPasswordError(null);
                    }}
                  />
                  {/* Whether this password is correct is decided by the API alone. */}
                  {currentPasswordError && <p className="field-error">{currentPasswordError}</p>}
                </div>

                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div>
                    <label className="label" htmlFor="new-password">
                      New password
                    </label>
                    <input
                      id="new-password"
                      type="password"
                      className={newPasswordError ? 'input input-error' : 'input'}
                      value={passwordForm.newPassword}
                      required
                      autoComplete="new-password"
                      onChange={(event) => {
                        setPasswordForm({ ...passwordForm, newPassword: event.target.value });
                        setNewPasswordError(null);
                      }}
                    />
                    {newPasswordError && <p className="field-error">{newPasswordError}</p>}
                  </div>

                  <div>
                    <label className="label" htmlFor="confirm-password">
                      Confirm new password
                    </label>
                    <input
                      id="confirm-password"
                      type="password"
                      className={confirmPasswordError ? 'input input-error' : 'input'}
                      value={passwordForm.confirmPassword}
                      required
                      autoComplete="new-password"
                      onChange={(event) => {
                        setPasswordForm({ ...passwordForm, confirmPassword: event.target.value });
                        setConfirmPasswordError(null);
                      }}
                    />
                    {confirmPasswordError && <p className="field-error">{confirmPasswordError}</p>}
                  </div>
                </div>
              </div>

              <p className="mt-4 text-xs text-slate-500">
                A new password needs at least {MIN_PASSWORD_LENGTH} characters. The check here is
                only for quick feedback - the service applies the full policy when it saves.
              </p>

              <div className="mt-5 flex justify-end">
                <button type="submit" className="btn-primary" disabled={changingPassword}>
                  {changingPassword ? (
                    <Spinner size="sm" />
                  ) : (
                    <KeyRound className="h-4 w-4" aria-hidden="true" />
                  )}
                  Update password
                </button>
              </div>
            </form>
          </div>
        </div>
      </div>
    </div>
  );
}
