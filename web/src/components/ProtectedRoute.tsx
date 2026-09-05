/**
 * =============================================================================
 * File        : src/components/ProtectedRoute.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : Route guard for the authenticated area of the web application.
 *               It waits for the stored token to be re-validated, bounces an
 *               anonymous visitor to the login page (remembering where they were
 *               heading), and hides screens the signed in role may not use.
 *               Serves the "role based access - Backoffice / Grid Operator"
 *               marking requirement on the client side.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

import type { ReactNode } from 'react';
import { Link, Navigate, useLocation } from 'react-router-dom';
import { ShieldAlert } from 'lucide-react';
import { useAuth } from '../context/AuthContext';
import { PageLoader } from './ui';
import type { UserRole } from '../types';

/** Props accepted by the guard. */
interface ProtectedRouteProps {
  /** Roles allowed through. Omit to allow any signed in user. */
  roles?: UserRole[];
  children: ReactNode;
}

/** Renders its children only for a signed in user holding an allowed role. */
export function ProtectedRoute({ roles, children }: ProtectedRouteProps) {
  // The location is remembered so the login screen can send the user back to the
  // page they originally asked for.
  const { user, initialising } = useAuth();
  const location = useLocation();

  // The stored token is still being checked against GET /auth/me, so nothing can
  // be decided yet - showing the login page here would flash for every refresh.
  if (initialising) {
    return <PageLoader />;
  }

  // No session at all: send the visitor to the login page.
  if (!user) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  // Role check. This is a convenience only: the C# Web API applies the very same
  // rule with [Authorize(Roles = ...)] on every endpoint, so a user who reached a
  // forbidden screen by typing the URL would still be refused by the server with
  // HTTP 403. The guard exists purely to keep the UI honest.
  if (roles && !roles.includes(user.role)) {
    return (
      <div className="mx-auto max-w-lg py-10">
        <div className="card">
          <div className="card-body text-center">
            <div className="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-red-100">
              <ShieldAlert className="h-7 w-7 text-red-600" aria-hidden="true" />
            </div>
            <h1 className="text-lg font-bold text-slate-900">
              403 - You do not have access to this page
            </h1>
            <p className="mt-2 text-sm text-slate-600">
              Your account is signed in as {user.role}, which is not permitted to open this
              screen. The Smart Solar API enforces the same restriction on the server.
            </p>
            <Link to="/dashboard" className="btn-primary mt-6">
              Back to dashboard
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}

export default ProtectedRoute;
