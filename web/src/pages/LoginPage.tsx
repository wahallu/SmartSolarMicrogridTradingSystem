/* =============================================================================
 * File        : src/pages/LoginPage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Pages / Login
 * Description : Collects account credentials and starts an authenticated
 *               session.
 * Author      : IT22049872 - Chathushka
 * Created     : 2026-09-03
 * =============================================================================
 */

import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { ArrowLeft, Eye, EyeOff, Loader2, ShieldAlert, Sun, TriangleAlert } from 'lucide-react';
import { useAuth } from '../context/AuthContext';
import { ApiError } from '../api/client';
import type { UserRole } from '../types';

/** Inline validation messages for the two fields. */
interface FieldErrors {
  identifier: string | null;
  password: string | null;
}

/** What went wrong on the last submit, with the server's stable error code. */
interface LoginFailure {
  message: string;
  code: string | null;
}

/** A seeded account offered as a one click quick fill. */
interface DemoAccount {
  label: string;
  identifier: string;
  password: string;
}

/** The accounts the API's seeder creates, so a marker can sign in immediately. */
const DEMO_ACCOUNTS: DemoAccount[] = [
  { label: 'Back office', identifier: 'admin', password: 'Admin@123' },
  { label: 'Grid operator', identifier: 'operator1', password: 'Operator@123' },
  { label: 'Prosumer', identifier: '200112301234', password: 'Prosumer@123' },
];

/**
 * The landing route for a role after a successful sign in. This is presentation
 * routing only - the API still authorises every request that follows.
 */
function homeRouteForRole(role: UserRole): string {
  // Staff start on the dashboard; a prosumer starts on their own bookings.
  switch (role) {
    case 'Prosumer':
      return '/reservations';
    case 'Backoffice':
    case 'GridOperator':
      return '/dashboard';
    default:
      return '/dashboard';
  }
}

/**
 * Reads the route the visitor was originally heading for out of the router state,
 * tolerating either a plain path string or a full location object.
 */
function readRedirectTarget(state: unknown): string | null {
  // ProtectedRoute may pass either shape, so both are accepted rather than assumed.
  if (!state || typeof state !== 'object') return null;
  const from = (state as { from?: unknown }).from;
  const path: string | null =
    typeof from === 'string'
      ? from
      : from && typeof from === 'object' && typeof (from as { pathname?: unknown }).pathname === 'string'
        ? ((from as { pathname: string }).pathname)
        : null;

  // Returning to "/" or "/login" would bounce the user straight back here, so the
  // role based route is used instead in those two cases.
  if (!path || path === '/' || path === '/login') return null;
  return path;
}

/** Checks both fields and returns the messages to render beneath them. */
function validate(identifier: string, password: string): FieldErrors {
  // Only presence is checked in the browser; format and credentials are the API's job.
  return {
    identifier: identifier.trim() ? null : 'Enter your NIC, username or e-mail address.',
    password: password ? null : 'Enter your password.',
  };
}

/** The public sign-in screen shown at "/login". */
export default function LoginPage() {
  // All state is local to the form; the session itself lives in AuthContext.
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const [identifier, setIdentifier] = useState<string>('');
  const [password, setPassword] = useState<string>('');
  const [showPassword, setShowPassword] = useState<boolean>(false);
  const [errors, setErrors] = useState<FieldErrors>({ identifier: null, password: null });
  const [failure, setFailure] = useState<LoginFailure | null>(null);
  const [submitting, setSubmitting] = useState<boolean>(false);

  const redirectTarget: string | null = readRedirectTarget(location.state);

  // Re-validates one field when the visitor leaves it.
  function handleBlur(field: keyof FieldErrors) {
    const next = validate(identifier, password);
    setErrors((current) => ({
      identifier: field === 'identifier' ? next.identifier : current.identifier,
      password: field === 'password' ? next.password : current.password,
    }));
  }

  // Copies a seeded account into the form so the portal can be demonstrated fast.
  function quickFill(account: DemoAccount) {
    setIdentifier(account.identifier);
    setPassword(account.password);
    setErrors({ identifier: null, password: null });
    setFailure(null);
  }

  // Validates, signs in and routes to the landing screen for the returned role.
  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const nextErrors = validate(identifier, password);
    setErrors(nextErrors);
    if (nextErrors.identifier || nextErrors.password) {
      return;
    }

    setFailure(null);
    setSubmitting(true);

    try {
      const user = await login(identifier.trim(), password);
      // Role based redirect: the server decides who the user is, the UI only
      // decides which of its own screens suits that role best.
      navigate(redirectTarget ?? homeRouteForRole(user.role), { replace: true });
      // Deliberately left submitting: the component unmounts on navigation, so
      // clearing the flag here would be a state update after unmount.
    } catch (err) {
      setFailure(
        err instanceof ApiError
          ? { message: err.message, code: err.errorCode }
          : { message: 'Something went wrong', code: null },
      );
      setSubmitting(false);
    }
  }

  // ACCOUNT_PENDING is rule R6 (a self-registered prosumer waits for Backoffice
  // activation) and ACCOUNT_DEACTIVATED is rule R5 (only Backoffice may reactivate).
  // Both rules live in the API - the UI only recognises the codes it sends back.
  const isPending: boolean = failure?.code === 'ACCOUNT_PENDING';
  const isDeactivated: boolean = failure?.code === 'ACCOUNT_DEACTIVATED';

  return (
    <div className="min-h-[100dvh] bg-white lg:grid lg:grid-cols-2">
      {/* ---- Photograph (large screens only; the form is the page on a phone) ---- */}
      <aside className="relative hidden lg:block">
        <img
          src="/images/rooftop-array.jpg"
          alt="A technician reads a document beside a large array of rooftop solar panels under a clear sky."
          width={1400}
          height={933}
          className="absolute inset-0 h-full w-full object-cover object-[30%_50%]"
        />
      </aside>

      {/* ---- Sign-in column ---- */}
      <main className="flex min-h-[100dvh] flex-col px-4 py-6 sm:px-10 lg:px-16 lg:py-8">
        <div className="flex items-center justify-between">
          <Link to="/" className="flex items-center gap-3">
            <span className="flex h-8 w-8 items-center justify-center rounded-md bg-accent text-accent-ink">
              <Sun className="h-[18px] w-[18px]" aria-hidden="true" />
            </span>
            <span className="text-base font-semibold tracking-tight text-slate-900">
              Smart Solar Microgrid
            </span>
          </Link>
        </div>

        <div className="mx-auto flex w-full max-w-sm flex-1 animate-fade-in flex-col justify-center py-10">
          <h1 className="text-3xl font-semibold text-slate-900">Sign in</h1>
          <p className="mt-2 text-slate-600">Use the credentials issued for your role.</p>

          {/* Account state alerts - wording and code both come from the API. */}
          {failure && isPending && (
            <div
              className="mt-6 flex gap-3 rounded-lg border border-solar-300 bg-solar-50 px-4 py-3"
              role="alert"
            >
              <TriangleAlert className="mt-0.5 h-5 w-5 shrink-0 text-solar-700" aria-hidden="true" />
              <div>
                <p className="text-sm font-semibold text-solar-900">
                  Your account is awaiting activation
                </p>
                <p className="mt-1 text-sm text-solar-800">{failure.message}</p>
                <p className="mt-1 text-sm text-solar-800">
                  A Backoffice officer must activate the account before it can be used.
                </p>
              </div>
            </div>
          )}

          {failure && isDeactivated && (
            <div
              className="mt-6 flex gap-3 rounded-lg border border-red-300 bg-red-50 px-4 py-3"
              role="alert"
            >
              <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-red-700" aria-hidden="true" />
              <div>
                <p className="text-sm font-semibold text-red-900">This account is deactivated</p>
                <p className="mt-1 text-sm text-red-800">{failure.message}</p>
                <p className="mt-1 text-sm text-red-800">
                  Please contact the Backoffice to have the account reactivated.
                </p>
              </div>
            </div>
          )}

          {failure && !isPending && !isDeactivated && (
            <div
              className="mt-6 flex gap-3 rounded-lg border border-red-300 bg-red-50 px-4 py-3"
              role="alert"
            >
              <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-red-700" aria-hidden="true" />
              <p className="text-sm text-red-900">{failure.message}</p>
            </div>
          )}

          <form onSubmit={handleSubmit} noValidate className="mt-8">
            <div>
              <label className="label" htmlFor="identifier">
                NIC, username or e-mail
              </label>
              <input
                id="identifier"
                name="identifier"
                type="text"
                autoComplete="username"
                autoFocus
                className={errors.identifier ? 'input input-error' : 'input'}
                placeholder="200112301234"
                value={identifier}
                disabled={submitting}
                aria-invalid={errors.identifier ? true : undefined}
                aria-describedby={errors.identifier ? 'identifier-error' : undefined}
                onChange={(event) => setIdentifier(event.target.value)}
                onBlur={() => handleBlur('identifier')}
              />
              {errors.identifier && (
                <p className="field-error" id="identifier-error">
                  {errors.identifier}
                </p>
              )}
            </div>

            <div className="mt-5">
              <label className="label" htmlFor="password">
                Password
              </label>
              <div className="relative">
                <input
                  id="password"
                  name="password"
                  type={showPassword ? 'text' : 'password'}
                  autoComplete="current-password"
                  className={errors.password ? 'input input-error pr-11' : 'input pr-11'}
                  placeholder="Your password"
                  value={password}
                  disabled={submitting}
                  aria-invalid={errors.password ? true : undefined}
                  aria-describedby={errors.password ? 'password-error' : undefined}
                  onChange={(event) => setPassword(event.target.value)}
                  onBlur={() => handleBlur('password')}
                />
                <button
                  type="button"
                  className="absolute inset-y-0 right-0 flex w-11 items-center justify-center rounded-r-lg text-slate-500 hover:text-slate-900"
                  onClick={() => setShowPassword((current) => !current)}
                  aria-label={showPassword ? 'Hide password' : 'Show password'}
                  aria-pressed={showPassword}
                  tabIndex={0}
                >
                  {showPassword ? (
                    <EyeOff className="h-4 w-4" aria-hidden="true" />
                  ) : (
                    <Eye className="h-4 w-4" aria-hidden="true" />
                  )}
                </button>
              </div>
              {errors.password && (
                <p className="field-error" id="password-error">
                  {errors.password}
                </p>
              )}
            </div>

            <button type="submit" className="btn-primary mt-7 w-full py-2.5" disabled={submitting}>
              {submitting ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                  Signing in...
                </>
              ) : (
                'Sign in'
              )}
            </button>
          </form>

          {/* ---- Quick fill for the seeded demo accounts ---- */}
          <fieldset className="mt-10 border-t border-slate-200 pt-6" disabled={submitting}>
            <legend className="sr-only">Demo accounts</legend>
            <p className="text-sm font-medium text-slate-900">Demo accounts</p>
            <p className="mt-1 text-sm text-slate-600">Fill the form with a seeded login.</p>
            <div className="mt-3 grid grid-cols-3 divide-x divide-slate-200 overflow-hidden rounded-lg border border-slate-300">
              {DEMO_ACCOUNTS.map((account) => (
                <button
                  key={account.identifier}
                  type="button"
                  className="bg-white px-2 py-2.5 text-center text-sm font-medium text-slate-800 transition-colors hover:bg-slate-50 disabled:opacity-50"
                  onClick={() => quickFill(account)}
                >
                  {account.label}
                </button>
              ))}
            </div>
          </fieldset>
        </div>

        <div>
          <Link
            to="/"
            className="inline-flex items-center gap-1.5 text-sm font-medium text-slate-600 hover:text-slate-900"
          >
            <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            Back to home
          </Link>
        </div>
      </main>
    </div>
  );
}
