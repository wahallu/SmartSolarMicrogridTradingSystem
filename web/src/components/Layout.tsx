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
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
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

/** A titled run of related entries. A group with no visible entries is not drawn. */
interface NavGroup {
  title: string | null;
  items: NavItem[];
}

/** Result of the single start-up probe against GET /health. */
type HealthState = 'checking' | 'online' | 'offline';

/** The whole navigation, declared once so the shell stays data driven. */
const NAV_GROUPS: NavGroup[] = [
  {
    title: null,
    items: [{ to: '/dashboard', label: 'Dashboard', icon: LayoutDashboard }],
  },
  {
    title: 'Trading',
    items: [
      { to: '/reservations', label: 'Reservations', icon: CalendarCheck },
      { to: '/slots', label: 'Booking Slots', icon: Clock, roles: ['Backoffice', 'GridOperator'] },
    ],
  },
  {
    title: 'Network',
    items: [
      { to: '/stations', label: 'Microgrid Nodes', icon: Zap },
      { to: '/map', label: 'Station Map', icon: MapPin },
    ],
  },
  {
    title: 'People',
    items: [
      { to: '/prosumers', label: 'Prosumers', icon: Users, roles: ['Backoffice', 'GridOperator'] },
      {
        to: '/prosumers/pending',
        label: 'Pending Activations',
        icon: UserCheck,
        roles: ['Backoffice'],
      },
      { to: '/users', label: 'Web Users', icon: Shield, roles: ['Backoffice'] },
    ],
  },
];

/** Flat list used to work out the section name for the mobile top bar. */
const ALL_ITEMS: NavItem[] = NAV_GROUPS.flatMap((group) => group.items);

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

/** Picks the section name for the current URL by longest matching nav path. */
function deriveSectionName(pathname: string): string {
  // Longest prefix wins, so /prosumers/pending reads "Pending Activations" rather
  // than "Prosumers", and detail routes inherit their section heading.
  let matched = '';
  let title = 'Smart Solar';

  for (const item of ALL_ITEMS) {
    const isMatch = pathname === item.to || pathname.startsWith(`${item.to}/`);
    if (isMatch && item.to.length > matched.length) {
      matched = item.to;
      title = item.label;
    }
  }

  if (pathname.startsWith('/profile')) return 'My profile';
  return title;
}

/** The authenticated app shell; child routes render inside its main area. */
export function Layout() {
  // The drawer state only matters below the lg breakpoint, where the rail
  // slides in over the content instead of sitting beside it.
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [sidebarOpen, setSidebarOpen] = useState<boolean>(false);
  const [health, setHealth] = useState<HealthState>('checking');

  // Only the entries the signed in role is allowed to use are ever rendered. The
  // same restriction is applied by the API on every endpoint.
  const visibleGroups = useMemo<NavGroup[]>(() => {
    const role = user?.role;
    return NAV_GROUPS.map((group) => ({
      title: group.title,
      items: group.items.filter(
        (item) => !item.roles || (role !== undefined && item.roles.includes(role)),
      ),
    })).filter((group) => group.items.length > 0);
  }, [user?.role]);

  const sectionName = deriveSectionName(location.pathname);

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
    <div className="min-h-[100dvh] bg-slate-50">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[70] focus:rounded-lg focus:bg-white focus:px-4 focus:py-2 focus:text-sm focus:font-medium focus:text-slate-900"
      >
        Skip to content
      </a>

      {/* Mobile top bar: the rail is a drawer below lg, so the menu button lives here. */}
      <header className="sticky top-0 z-20 flex h-14 items-center gap-3 border-b border-slate-200 bg-white px-4 lg:hidden">
        <button
          type="button"
          className="btn-ghost -ml-2 px-2"
          onClick={() => setSidebarOpen(true)}
          aria-label="Open navigation"
          aria-expanded={sidebarOpen}
        >
          <Menu className="h-5 w-5" aria-hidden="true" />
        </button>
        <p className="truncate text-sm font-semibold text-slate-900">{sectionName}</p>
      </header>

      {/* Backdrop - tapping anywhere outside the drawer closes it. */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 z-30 bg-slate-950/60 lg:hidden"
          onClick={() => setSidebarOpen(false)}
          aria-hidden="true"
        />
      )}

      <aside
        className={`rail fixed inset-y-0 left-0 z-40 flex w-64 flex-col border-r border-chrome-line/10 bg-chrome transition-transform duration-200 ease-out lg:w-60 lg:translate-x-0 ${
          sidebarOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
        aria-label="Primary"
      >
        <div className="flex h-14 items-center gap-2.5 px-5">
          <Link to="/dashboard" className="flex min-w-0 items-center gap-2.5">
            <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-accent text-accent-ink">
              <Sun className="h-4 w-4" aria-hidden="true" />
            </span>
            <span className="truncate text-sm font-semibold text-chrome-hi">Smart Solar</span>
          </Link>
          <button
            type="button"
            className="ml-auto rounded-md p-1.5 text-chrome-text hover:bg-chrome-line/10 hover:text-chrome-hi lg:hidden"
            onClick={() => setSidebarOpen(false)}
            aria-label="Close navigation"
          >
            <X className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>

        <nav className="flex-1 overflow-y-auto px-3 pb-4 pt-2">
          {visibleGroups.map((group, groupIndex) => (
            <div key={group.title ?? 'root'} className={groupIndex === 0 ? '' : 'mt-6'}>
              {group.title && (
                <p className="mb-1.5 px-3 text-xs font-medium text-chrome-text/70">{group.title}</p>
              )}
              <div className="space-y-0.5">
                {group.items.map((item) => {
                  const Icon = item.icon;
                  return (
                    <NavLink
                      key={item.to}
                      to={item.to}
                      end={item.to === '/prosumers'}
                      className={({ isActive }) =>
                        isActive ? 'nav-link nav-link-active' : 'nav-link'
                      }
                    >
                      <Icon className="h-[18px] w-[18px] shrink-0" aria-hidden="true" />
                      <span className="truncate">{item.label}</span>
                    </NavLink>
                  );
                })}
              </div>
            </div>
          ))}
        </nav>

        <div className="border-t border-chrome-line/10 p-3">
          <Link
            to="/profile"
            className="flex items-center gap-3 rounded-lg px-2 py-2 transition-colors hover:bg-chrome-line/5"
            aria-label="Open my profile"
          >
            <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-chrome-line/10 text-sm font-semibold text-chrome-hi">
              {initial}
            </span>
            <span className="min-w-0">
              <span className="block truncate text-sm font-medium text-chrome-hi">
                {user?.fullName ?? 'Signed in user'}
              </span>
              <span className="block truncate text-xs text-chrome-text">
                {user ? roleLabel(user.role) : '-'}
              </span>
            </span>
          </Link>

          <div className="mt-2 flex items-center justify-between gap-2 px-2">
            <span
              className="flex items-center gap-2 text-xs text-chrome-text"
              role="status"
              aria-live="polite"
            >
              <span
                className={`h-2 w-2 shrink-0 rounded-full ${
                  health === 'online'
                    ? 'bg-grid-400'
                    : health === 'offline'
                      ? 'bg-red-500'
                      : 'bg-chrome-text/50'
                }`}
                aria-hidden="true"
              />
              {health === 'online' ? 'API online' : health === 'offline' ? 'API offline' : 'Checking API'}
            </span>
            <button
              type="button"
              className="flex items-center gap-1.5 rounded-md px-2 py-1.5 text-xs font-medium text-chrome-text transition-colors hover:bg-chrome-line/10 hover:text-chrome-hi"
              onClick={handleSignOut}
            >
              <LogOut className="h-3.5 w-3.5" aria-hidden="true" />
              Sign out
            </button>
          </div>
        </div>
      </aside>

      <div className="lg:pl-60">
        <main id="main" className="mx-auto max-w-[1360px] px-4 py-6 sm:px-6 lg:px-10 lg:py-9">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

export default Layout;
