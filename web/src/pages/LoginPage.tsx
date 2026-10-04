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
import type { LucideIcon } from 'lucide-react';
import {
  ArrowLeft,
  BatteryCharging,
  Eye,
  EyeOff,
  Loader2,
  LogIn,
  QrCode,
  ShieldAlert,
  Sun,
  TriangleAlert,
  Zap,
} from 'lucide-react';
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
  { label: 'Backoffice', identifier: 'admin', password: 'Admin@123' },
  { label: 'Grid Operator', identifier: 'operator1', password: 'Operator@123' },
  { label: 'Prosumer', identifier: '200112301234', password: 'Prosumer@123' },
];

/** One selling point on the brand panel. */
interface Highlight {
  icon: LucideIcon;
  title: string;
  body: string;
}

/** The three selling points shown on the brand panel. */
const HIGHLIGHTS: Highlight[] = [
  {
    icon: Zap,
    title: 'Reserve energy slots',
    body: 'Book a window at any active microgrid node and state the kWh you intend to trade.',
  },
  {
    icon: QrCode,
    title: 'Carry an approved booking',
    body: 'Approved reservations are issued a signed QR code that identifies you at the node.',
  },
  {
    icon: BatteryCharging,
    title: 'Settle the transfer on site',
    body: 'Operators scan, verify against the API and record the energy that actually flowed.',
  },
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
    <div className="min-h-screen bg-slate-50 lg:grid lg:grid-cols-2">
      {/* ---- Brand panel (left on large screens) ---- */}
      <aside
        className="relative hidden overflow-hidden px-12 py-16 text-white lg:flex lg:flex-col lg:justify-between"
        style={{
          backgroundImage:
            'linear-gradient(150deg, #fbbf24 0%, #f59e0b 38%, #d97706 68%, #92400e 100%)',
        }}
      >
        <div
          className="pointer-events-none absolute inset-0 opacity-20"
          style={{
            backgroundImage:
              'radial-gradient(circle at 78% 12%, rgba(255,255,255,0.85) 0%, rgba(255,255,255,0) 46%)',
          }}
          aria-hidden="true"
        />

        <div className="relative">
          <Link to="/" className="flex items-center gap-3">
            <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-white/20 backdrop-blur">
              <Sun className="h-6 w-6" aria-hidden="true" />
            </span>
            <span className="text-lg font-extrabold tracking-tight">Smart Solar Microgrid</span>
          </Link>

          <h1 className="mt-14 max-w-md text-4xl font-extrabold leading-tight tracking-tight">
            Trading System Portal
          </h1>
          <p className="mt-4 max-w-md text-sm leading-relaxed text-white/90">
            One place for the back office, grid operators and solar prosumers to manage every energy
            reservation on the microgrid.
          </p>

          <ul className="mt-12 max-w-md space-y-6">
            {HIGHLIGHTS.map((item) => {
              const HighlightIcon = item.icon;
              return (
                <li key={item.title} className="flex gap-4">
                  <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-white/20 backdrop-blur">
                    <HighlightIcon className="h-5 w-5" aria-hidden="true" />
                  </span>
                  <div>
                    <p className="text-sm font-semibold">{item.title}</p>
                    <p className="mt-1 text-sm leading-relaxed text-white/85">{item.body}</p>
                  </div>
                </li>
              );
            })}
          </ul>
        </div>

        <p className="relative text-xs text-white/75">
          SE4040 Enterprise Application Development
        </p>
      </aside>

      {/* ---- Sign-in column ---- */}
      <main className="flex min-h-screen items-center justify-center px-4 py-10 sm:px-6 lg:min-h-0 lg:py-16">
        <div className="w-full max-w-md animate-fade-in">
          {/* Compact brand block, shown only where the panel above is hidden. */}
          <div className="mb-8 flex items-center gap-3 lg:hidden">
            <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-solar-400 to-solar-600 text-white shadow-card">
              <Sun className="h-5 w-5" aria-hidden="true" />
            </span>
            <span className="text-lg font-extrabold tracking-tight text-slate-900">
              Smart Solar Microgrid
            </span>
          </div>

          <div className="card">
            <div className="card-header">
              <div>
                <h2 className="text-lg font-bold text-slate-900">Sign in</h2>
                <p className="mt-0.5 text-sm text-slate-500">
                  Use the credentials issued for your role.
                </p>
              </div>
              <LogIn className="h-5 w-5 shrink-0 text-slate-400" aria-hidden="true" />
            </div>

            <div className="card-body">
              {/* Account state alerts - wording and code both come from the API. */}
              {failure && isPending && (
                <div
                  className="mb-5 flex gap-3 rounded-lg border border-amber-300 bg-amber-50 px-4 py-3"
                  role="alert"
                >
                  <TriangleAlert className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" aria-hidden="true" />
                  <div>
                    <p className="text-sm font-semibold text-amber-900">
                      Your account is awaiting activation
                    </p>
                    <p className="mt-1 text-sm text-amber-800">{failure.message}</p>
                    <p className="mt-1 text-xs text-amber-700">
                      A Backoffice officer must activate the account before it can be used.
                    </p>
                  </div>
                </div>
              )}

              {failure && isDeactivated && (
                <div
                  className="mb-5 flex gap-3 rounded-lg border border-red-300 bg-red-50 px-4 py-3"
                  role="alert"
                >
                  <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-red-600" aria-hidden="true" />
                  <div>
                    <p className="text-sm font-semibold text-red-900">This account is deactivated</p>
                    <p className="mt-1 text-sm text-red-800">{failure.message}</p>
                    <p className="mt-1 text-xs text-red-700">
                      Please contact the Backoffice to have the account reactivated.
                    </p>
                  </div>
                </div>
              )}

              {failure && !isPending && !isDeactivated && (
                <div
                  className="mb-5 flex gap-3 rounded-lg border border-red-300 bg-red-50 px-4 py-3"
                  role="alert"
                >
                  <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-red-600" aria-hidden="true" />
                  <p className="text-sm text-red-800">{failure.message}</p>
                </div>
              )}

              <form onSubmit={handleSubmit} noValidate>
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

                <div className="mt-4">
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
                      className="absolute inset-y-0 right-0 flex w-11 items-center justify-center rounded-r-lg text-slate-400 hover:text-slate-700"
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

                <button type="submit" className="btn-primary mt-6 w-full" disabled={submitting}>
                  {submitting ? (
                    <>
                      <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                      Signing in...
                    </>
                  ) : (
                    <>
                      <LogIn className="h-4 w-4" aria-hidden="true" />
                      Sign in
                    </>
                  )}
                </button>
              </form>
            </div>
          </div>

          {/* ---- Quick fill for the seeded demo accounts ---- */}
          <div className="card mt-5">
            <div className="card-body">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                Demo accounts
              </p>
              <p className="mt-1 text-xs text-slate-500">
                Tap a role to fill the form with its seeded credentials.
              </p>
              <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-3">
                {DEMO_ACCOUNTS.map((account) => (
                  <button
                    key={account.identifier}
                    type="button"
                    className="btn-secondary btn-sm flex-col items-start gap-0.5 py-2 text-left"
                    disabled={submitting}
                    onClick={() => quickFill(account)}
                  >
                    <span className="font-semibold text-slate-800">{account.label}</span>
                    <span className="font-mono text-[11px] font-normal text-slate-500">
                      {account.identifier}
                    </span>
                  </button>
                ))}
              </div>
            </div>
          </div>

          <div className="mt-6 text-center">
            <Link
              to="/"
              className="inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-900"
            >
              <ArrowLeft className="h-4 w-4" aria-hidden="true" />
              Back to home
            </Link>
          </div>
        </div>
      </main>
    </div>
  );
}
