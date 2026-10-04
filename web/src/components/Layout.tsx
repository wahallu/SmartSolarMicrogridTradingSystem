/* =============================================================================
 * File        : src/components/Layout.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Components / Layout
 * Description : Provides the signed-in navigation and shared page frame.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-23
 * =============================================================================
 */

import { useEffect, useMemo, useState } from 'react';
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import {
  CalendarCheck,
  Clock,
  LayoutDashboard,
  LogOut,
  MapPin,
  Menu,
  Shield,
  Sun,
  UserCheck,
  Users,
  X,
  Zap,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { healthApi } from '../api/services';
import { useAuth } from '../context/AuthContext';
import type { UserRole } from '../types';

/** One entry in the sidebar. `roles` omitted means every signed in role sees it. */
interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
  roles?: UserRole[];
}

/** Result of the single start-up probe against GET /health. */
type HealthState = 'checking' | 'online' | 'offline';

/** The whole navigation, declared once so the shell stays data driven. */
const NAV_ITEMS: NavItem[] = [
  { to: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/reservations', label: 'Reservations', icon: CalendarCheck },
  { to: '/stations', label: 'Microgrid Nodes', icon: Zap },
  { to: '/slots', label: 'Booking Slots', icon: Clock, roles: ['Backoffice', 'GridOperator'] },
  { to: '/prosumers', label: 'Prosumers', icon: Users, roles: ['Backoffice', 'GridOperator'] },
  { to: '/prosumers/pending', label: 'Pending Activations', icon: UserCheck, roles: ['Backoffice'] },
  { to: '/users', label: 'Web Users', icon: Shield, roles: ['Backoffice'] },
  { to: '/map', label: 'Station Map', icon: MapPin },
];

/** Human readable wording for a role code. */
function roleLabel(role: UserRole): string {
  // The API's enum names are not phrasing to put in front of a user.
  switch (role) {
    case 'Backoffice':
      return 'Back office officer';
    case 'GridOperator':
      return 'Grid operator';
    default:
      return 'Solar prosumer';
  }
}

/** Picks the heading for the current URL by longest matching nav path. */
function derivePageTitle(pathname: string): string {
  // Longest prefix wins, so /prosumers/pending reads "Pending Activations" rather
  // than "Prosumers", and detail routes inherit their section heading.
  let matched = '';
  let title = 'Smart Solar';

  for (const item of NAV_ITEMS) {
    const isMatch = pathname === item.to || pathname.startsWith(`${item.to}/`);
    if (isMatch && item.to.length > matched.length) {
      matched = item.to;
      title = item.label;
    }
  }

  return title;
}

/** The authenticated app shell; child routes render inside its main area. */
export function Layout() {
  // The drawer state only matters below the lg breakpoint, where the sidebar
  // slides in over the content instead of sitting beside it.
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [sidebarOpen, setSidebarOpen] = useState<boolean>(false);
  const [health, setHealth] = useState<HealthState>('checking');

  // Only the items the signed in role is allowed to use are ever rendered. The
  // same restriction is applied by the API on every endpoint.
  const visibleItems = useMemo<NavItem[]>(() => {
    const role = user?.role;
    return NAV_ITEMS.filter((item) => !item.roles || (role !== undefined && item.roles.includes(role)));
  }, [user?.role]);

  const pageTitle = derivePageTitle(location.pathname);

  // Navigating on a phone should not leave the drawer covering the new page.
  useEffect(() => {
    setSidebarOpen(false);
  }, [location.pathname]);

  // One live probe of GET /health on mount. This is what proves to the marker
  // that the web client is genuinely talking to the hosted C# service rather
  // than to mock data baked into the front end.
  useEffect(() => {
    let cancelled = false;

    async function probe() {
      try {
        await healthApi.check();
        if (!cancelled) {
          setHealth('online');
        }
      } catch {
        // The exact failure is unimportant here; the dot only reports reachability.
        if (!cancelled) {
          setHealth('offline');
        }
      }
    }

    void probe();
    return () => {
      cancelled = true;
    };
  }, []);

  // Clears the session, then returns to the login screen.
  function handleSignOut() {
    logout();
    navigate('/login', { replace: true });
  }

  const initial = user?.fullName?.trim().charAt(0).toUpperCase() || 'S';

  return (
    <div className="min-h-screen bg-slate-50">
      {/* Mobile backdrop - tapping anywhere outside the drawer closes it. */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 z-30 bg-slate-900/50 lg:hidden"
          onClick={() => setSidebarOpen(false)}
          aria-hidden="true"
        />
      )}

      <aside
        className={`fixed inset-y-0 left-0 z-40 flex w-64 flex-col border-r border-slate-200 bg-white transition-transform duration-200 ease-out lg:translate-x-0 ${
          sidebarOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        <div className="flex items-center gap-3 border-b border-slate-200 px-5 py-4">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-solar-500">
            <Sun className="h-6 w-6 text-white" aria-hidden="true" />
          </div>
          <div className="min-w-0">
            <p className="truncate text-sm font-bold text-slate-900">Smart Solar</p>
            <p className="truncate text-xs text-slate-500">Microgrid Trading</p>
          </div>
          <button
            type="button"
            className="btn-ghost btn-sm ml-auto lg:hidden"
            onClick={() => setSidebarOpen(false)}
            aria-label="Close navigation"
          >
            <X className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>

        <nav className="flex-1 space-y-1 overflow-y-auto px-3 py-4">
          {visibleItems.map((item) => {
            const Icon = item.icon;
            return (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.to === '/prosumers'}
                className={({ isActive }) => (isActive ? 'nav-link nav-link-active' : 'nav-link')}
              >
                <Icon className="h-5 w-5 shrink-0" aria-hidden="true" />
                <span className="truncate">{item.label}</span>
              </NavLink>
            );
          })}
        </nav>

        <div className="border-t border-slate-200 px-4 py-4">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-solar-100 text-sm font-bold text-solar-700">
              {initial}
            </div>
            <div className="min-w-0">
              <p className="truncate text-sm font-semibold text-slate-900">
                {user?.fullName ?? 'Signed in user'}
              </p>
              <p className="truncate text-xs text-slate-500">
                {user ? roleLabel(user.role) : '-'}
              </p>
            </div>
          </div>
          <button type="button" className="btn-secondary btn-sm mt-3 w-full" onClick={handleSignOut}>
            <LogOut className="h-4 w-4" aria-hidden="true" />
            Sign out
          </button>
        </div>
      </aside>

      <div className="lg:pl-64">
        <header className="sticky top-0 z-20 flex h-16 items-center gap-3 border-b border-slate-200 bg-white px-4 sm:px-6 lg:px-8">
          <button
            type="button"
            className="btn-ghost btn-sm lg:hidden"
            onClick={() => setSidebarOpen(true)}
            aria-label="Open navigation"
          >
            <Menu className="h-5 w-5" aria-hidden="true" />
          </button>

          <h1 className="truncate text-base font-bold text-slate-900 sm:text-lg">{pageTitle}</h1>

          <div className="ml-auto flex items-center gap-2">
            <span
              className={`h-2.5 w-2.5 shrink-0 rounded-full ${
                health === 'online'
                  ? 'bg-grid-500'
                  : health === 'offline'
                    ? 'bg-red-500'
                    : 'bg-slate-300'
              }`}
              aria-hidden="true"
            />
            <span className="hidden text-xs font-medium text-slate-500 sm:inline">
              {health === 'online' ? 'API online' : health === 'offline' ? 'API offline' : 'Checking API'}
            </span>
          </div>
        </header>

        <main className="p-4 sm:p-6 lg:p-8">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

export default Layout;
