/* =============================================================================
 * File        : src/pages/DashboardPage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Pages / Dashboard
 * Description : Shows live booking totals and upcoming reservation activity.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-20
 * =============================================================================
 */

import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Bar, BarChart, Cell, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import {
  ArrowRight,
  CalendarCheck,
  CalendarDays,
  CheckCircle2,
  Clock,
  MapPin,
  RefreshCw,
  UserCheck,
  Users,
  Zap,
} from 'lucide-react';
import { reservationsApi } from '../api/services';
import { ApiError } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { EmptyState, ErrorState, PageLoader, StatCard, StatGroup } from '../components/ui';
import { formatDateTime, kwh, reservationBadgeClass } from '../utils/format';
import type { Dashboard, UserRole } from '../types';

/** One bar of the reservation mix chart. */
interface StatusDatum {
  name: string;
  count: number;
  fill: string;
}

/** Picks a greeting from the viewer's own clock. */
function greetingFor(hour: number): string {
  // Purely cosmetic, so the browser's local hour is the right thing to read here
  // even though every timestamp from the API is rendered as UTC.
  if (hour < 12) return 'Good morning';
  if (hour < 18) return 'Good afternoon';
  return 'Good evening';
}

/** turns a role code into its display label.. */
function roleLabel(role: UserRole): string {
  // The API's enum names are not phrases to put in front of a user.
  switch (role) {
    case 'Backoffice':
      return 'Back office officer';
    case 'GridOperator':
      return 'Grid operator';
    default:
      return 'Solar prosumer';
  }
}

/** Takes the first word of a full name for the greeting line. */
function firstNameOf(fullName: string): string {
  // A blank profile name falls back to a neutral word rather than an empty gap.
  const first = fullName.trim().split(' ')[0];
  return first.length > 0 ? first : 'there';
}

/** The live operational dashboard. */
export default function DashboardPage() {
  // Everything below is display only: the counters, the chart and the upcoming
  // list are all rendered straight from the server's dashboard payload.
  const { user, isBackoffice } = useAuth();

  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [refreshing, setRefreshing] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadIndex, setReloadIndex] = useState<number>(0);

  // A grid operator only runs one node, so their counters are scoped to it. The
  // scoping itself is done by the API - passing the id is all the UI does.
  const scopedStationId =
    user?.role === 'GridOperator' && user.assignedStationId ? user.assignedStationId : undefined;

  useEffect(() => {
    // it prevents late responses from updating state after the user
// has left the dashboard.
    let cancelled = false;

    async function load(): Promise<void> {
      try {
        setError(null);
        const data = await reservationsApi.dashboard(scopedStationId);
        if (!cancelled) {
          setDashboard(data);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof ApiError ? err.message : 'Something went wrong');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
          setRefreshing(false);
        }
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, [scopedStationId, reloadIndex]);

  // triggers the effect again, and the button's spinner reflects the
// `refreshing` flag.
  const handleRefresh = useCallback(() => {
    setRefreshing(true);
    setReloadIndex((index) => index + 1);
  }, []);

  if (loading) {
    return <PageLoader />;
  }

  if (error !== null || dashboard === null) {
    return <ErrorState message={error ?? 'Something went wrong'} onRetry={handleRefresh} />;
  }

  const greeting = greetingFor(new Date().getHours());
  const showOperatorNode = user?.role === 'GridOperator' && user.assignedStationCode;
  const needsActivations = isBackoffice && dashboard.pendingProsumerActivations > 0;

  // The four bars are the same numbers as the stat cards, only drawn. Each status
  // keeps the colour of its badge so the chart reads without a legend.
  const chartData: StatusDatum[] = [
    { name: 'Pending', count: dashboard.pendingReservations, fill: 'rgb(var(--c-solar-500))' },
    { name: 'Approved', count: dashboard.approvedFutureReservations, fill: 'rgb(var(--c-blue-500))' },
    { name: 'Completed', count: dashboard.completedReservations, fill: 'rgb(var(--c-grid-600))' },
    { name: 'Cancelled', count: dashboard.cancelledReservations, fill: 'rgb(var(--c-slate-400))' },
  ];

  return (
    <div className="animate-fade-in space-y-6">
      {/* ---- Greeting ---- */}
      <header className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">
            {greeting}, {firstNameOf(user?.fullName ?? '')}
          </h1>
          <p className="mt-1 text-sm text-slate-600">
            {user ? roleLabel(user.role) : 'Signed in'}
            {showOperatorNode ? `, node ${user?.assignedStationCode}` : ''}
          </p>
        </div>

        <button
          type="button"
          className="btn-secondary self-start sm:self-auto"
          onClick={handleRefresh}
          disabled={refreshing}
        >
          <RefreshCw className={`h-4 w-4 ${refreshing ? 'animate-spin' : ''}`} aria-hidden="true" />
          {refreshing ? 'Refreshing' : 'Refresh'}
        </button>
      </header>

      {/* ---- Back office call to action ---- */}
      {needsActivations && (
        <div className="flex flex-col gap-3 rounded-xl border border-solar-300 bg-solar-50 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-start gap-3">
            <UserCheck className="mt-0.5 h-5 w-5 shrink-0 text-solar-700" aria-hidden="true" />
            <div>
              <p className="text-sm font-semibold text-solar-900">
                {dashboard.pendingProsumerActivations} prosumer account
                {dashboard.pendingProsumerActivations === 1 ? '' : 's'} waiting for activation
              </p>
              <p className="mt-0.5 text-sm text-solar-800">
                A new prosumer cannot book energy until the back office approves the account.
              </p>
            </div>
          </div>
          <Link to="/prosumers/pending" className="btn-primary btn-sm shrink-0">
            Review queue
            <ArrowRight className="h-4 w-4" aria-hidden="true" />
          </Link>
        </div>
      )}

      {/* ---- The reservation totals, fetched live from the API. ---- */}
      <section aria-label="Reservation totals" className="space-y-3">
        <StatGroup>
          <StatCard
            label="Pending reservations"
            value={dashboard.pendingReservations}
            icon={Clock}
            tone="solar"
          />
          <StatCard
            label="Approved and upcoming"
            value={dashboard.approvedFutureReservations}
            icon={CalendarCheck}
            tone="blue"
          />
          <StatCard
            label="Completed transfers"
            value={dashboard.completedReservations}
            icon={CheckCircle2}
            tone="grid"
          />
          <StatCard
            label="Energy delivered"
            value={kwh(dashboard.totalEnergyKwh)}
            icon={Zap}
            tone="grid"
          />
        </StatGroup>

        {/* ---- Estate-wide totals that only matter to the back office. ---- */}
        {isBackoffice && (
          <StatGroup>
            <StatCard
              label="Pending activations"
              value={dashboard.pendingProsumerActivations}
              icon={UserCheck}
              tone={dashboard.pendingProsumerActivations > 0 ? 'red' : 'slate'}
            />
            <StatCard
              label="Registered prosumers"
              value={dashboard.totalProsumers}
              icon={Users}
              tone="slate"
            />
            <StatCard
              label="Active nodes"
              value={`${dashboard.activeStations} / ${dashboard.totalStations}`}
              icon={MapPin}
              tone="slate"
            />
            <StatCard
              label="Today's bookings"
              value={dashboard.todayReservations}
              icon={CalendarDays}
              tone="blue"
            />
          </StatGroup>
        )}
      </section>

      <div className="grid grid-cols-1 gap-5 xl:grid-cols-5">
        {/* ---- Reservation mix ---- */}
        <div className="card xl:col-span-2">
          <div className="card-header">
            <h2 className="text-sm font-semibold text-slate-900">Reservation mix</h2>
            <span className="text-xs font-medium tabular-nums text-slate-600">
              {dashboard.totalReservations} in total
            </span>
          </div>
          <div className="card-body">
            <div className="h-64 w-full" role="img" aria-label="Reservations by status">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart
                  data={chartData}
                  layout="vertical"
                  margin={{ top: 4, right: 36, left: 0, bottom: 4 }}
                  barCategoryGap={14}
                >
                  <XAxis type="number" hide allowDecimals={false} />
                  <YAxis
                    type="category"
                    dataKey="name"
                    width={84}
                    tickLine={false}
                    axisLine={false}
                    tick={{ fontSize: 13, fill: 'rgb(var(--c-slate-700))' }}
                  />
                  <Tooltip
                    cursor={{ fill: 'rgb(var(--c-slate-100))' }}
                    contentStyle={{
                      borderRadius: '0.5rem',
                      border: '1px solid rgb(var(--c-slate-200))',
                      background: 'rgb(var(--c-white))',
                      color: 'rgb(var(--c-slate-900))',
                      fontSize: '0.8125rem',
                      boxShadow: 'none',
                    }}
                  />
                  <Bar dataKey="count" name="Reservations" radius={[0, 4, 4, 0]} maxBarSize={28}>
                    {chartData.map((datum) => (
                      <Cell key={datum.name} fill={datum.fill} />
                    ))}
                    <LabelList
                      dataKey="count"
                      position="right"
                      style={{ fontSize: 13, fontWeight: 600, fill: 'rgb(var(--c-slate-900))' }}
                    />
                  </Bar>
                </BarChart>
              </ResponsiveContainer>
            </div>
          </div>
        </div>

        {/* Reservations that have not started yet. */}
        <div className="card xl:col-span-3">
          <div className="card-header">
            <h2 className="text-sm font-semibold text-slate-900">Upcoming reservations</h2>
            <Link
              to="/reservations"
              className="text-xs font-semibold text-solar-800 underline-offset-4 hover:underline"
            >
              View all
            </Link>
          </div>

          {dashboard.upcoming.length === 0 ? (
            <div className="card-body">
              <EmptyState
                icon={CalendarDays}
                title="Nothing booked yet"
                message="Approved and pending bookings for the days ahead will appear here."
              />
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="table">
                <thead>
                  <tr>
                    <th>Reservation</th>
                    <th>Prosumer</th>
                    <th>Node</th>
                    <th>Starts</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {dashboard.upcoming.map((reservation) => (
                    <tr key={reservation.id}>
                      <td>
                        <Link
                          to={`/reservations/${reservation.id}`}
                          className="whitespace-nowrap font-mono text-xs font-medium text-solar-800 underline-offset-4 hover:underline"
                        >
                          {reservation.reservationNo}
                        </Link>
                      </td>
                      <td className="whitespace-nowrap font-medium text-slate-900">
                        {reservation.prosumerName}
                      </td>
                      <td className="whitespace-nowrap">{reservation.stationCode}</td>
                      <td className="whitespace-nowrap tabular-nums">
                        {formatDateTime(reservation.reservationStartUtc)}
                      </td>
                      <td>
                        {/* The UI never recomputes a booking's state. */}
                        <span className={reservationBadgeClass(reservation.status)}>
                          {reservation.status}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
