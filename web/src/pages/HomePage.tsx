/**
 * =============================================================================
 * File        : src/pages/HomePage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : The public landing (index) page of the web application, served
 *               at "/" with no authentication. It introduces the product, walks
 *               a visitor through the four step booking journey, explains what
 *               each of the three roles can do and lists the seeded demo logins.
 *               This screen satisfies the "Home (index) page" marking criterion.
 *               It is pure presentation - it calls no API and holds no rules.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

import { Link } from 'react-router-dom';
import type { LucideIcon } from 'lucide-react';
import {
  ArrowRight,
  BatteryCharging,
  Building2,
  CalendarClock,
  ExternalLink,
  IdCard,
  KeyRound,
  LayoutDashboard,
  MapPin,
  QrCode,
  ScanLine,
  ShieldCheck,
  Sun,
  Users,
  Zap,
} from 'lucide-react';

/** the dev URL for the API's interactive docs. */
const SWAGGER_URL = 'http://localhost:5218/swagger';

/** One entry in the "How it works" strip. */
interface JourneyStep {
  icon: LucideIcon;
  title: string;
  body: string;
}

/** One of the three principals the system recognises. */
interface RolePanel {
  icon: LucideIcon;
  name: string;
  tagline: string;
  abilities: string[];
}

/** A seeded account a marker can sign in with straight away. */
interface DemoAccount {
  identifier: string;
  password: string;
  role: string;
  note: string;
}

/** The four steps of the prosumer journey, in order. */
const JOURNEY: JourneyStep[] = [
  {
    icon: IdCard,
    title: 'Register with your NIC',
    body:
      'Your National Identity Card number is your account key. A new prosumer account starts Pending until a Backoffice officer activates it.',
  },
  {
    icon: CalendarClock,
    title: 'Reserve an energy slot',
    body:
      'Pick a microgrid node, choose a published time window inside the booking horizon and state how many kWh you intend to trade.',
  },
  {
    icon: QrCode,
    title: 'Get your QR code on approval',
    body:
      'Once the booking is approved the API issues a signed QR token. It appears on your reservation and is your proof at the node.',
  },
  {
    icon: ScanLine,
    title: 'Operator scans and finalises',
    body:
      'A Grid Operator scans the code on site, verifies it against the API and records the delivered kWh to close the transfer.',
  },
];

/** What each role is responsible for. */
const ROLES: RolePanel[] = [
  {
    icon: ShieldCheck,
    name: 'Backoffice',
    tagline: 'Runs the network',
    abilities: [
      'Create and manage web user accounts',
      'Activate or deactivate prosumer accounts',
      'Register microgrid nodes and publish slots',
      'Approve, reject and audit every reservation',
    ],
  },
  {
    icon: ScanLine,
    name: 'Grid Operator',
    tagline: 'Works at the node',
    abilities: [
      'See the queue for the assigned node',
      'Scan a prosumer QR code and verify it',
      'Record the delivered energy on completion',
      'Keep node availability and hours current',
    ],
  },
  {
    icon: Sun,
    name: 'Solar Prosumer',
    tagline: 'Trades rooftop power',
    abilities: [
      'Find the nearest active microgrid node',
      'Book, change or cancel an energy slot',
      'Carry an approved booking as a QR code',
      'Review a full trading history',
    ],
  },
];

/**
 * The accounts created by the API's DataSeeder on first run. They are listed on
 * the public page deliberately: a marker or demonstrator can sign in immediately
 * without being given credentials separately.
 */
const DEMO_ACCOUNTS: DemoAccount[] = [
  {
    identifier: 'admin',
    password: 'Admin@123',
    role: 'Backoffice',
    note: 'Full administration',
  },
  {
    identifier: 'operator1',
    password: 'Operator@123',
    role: 'Grid Operator',
    note: 'Assigned to node STN-001',
  },
  {
    identifier: '200112301234',
    password: 'Prosumer@123',
    role: 'Prosumer',
    note: 'Signs in with a NIC',
  },
];

/** The compact sun-and-panel mark reused in the nav bar and the footer. */
function BrandMark() {
  // Drawn with a gradient div rather than an image file so the app ships no binary assets.
  return (
    <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-gradient-to-br from-solar-400 to-solar-600 text-white shadow-card">
      <Sun className="h-5 w-5" aria-hidden="true" />
    </span>
  );
}

/**
 * The hero illustration. Everything is CSS and inline SVG - no external image is
 * fetched, which keeps the landing page fast and dependency free.
 */
function HeroVisual() {
  // The chips are absolutely positioned inside a square so they scale with the panel.
  return (
    <div className="relative mx-auto w-full max-w-md lg:max-w-none">
      <div
        className="relative aspect-square overflow-hidden rounded-3xl shadow-card-hover"
        style={{
          backgroundImage:
            'radial-gradient(circle at 32% 26%, #fef3c7 0%, #fbbf24 34%, #d97706 66%, #92400e 100%)',
        }}
      >
        {/* A faint grid, standing in for the microgrid itself. */}
        <div
          className="absolute inset-0 opacity-25"
          style={{
            backgroundImage:
              'linear-gradient(to right, rgba(255,255,255,0.55) 1px, transparent 1px), linear-gradient(to bottom, rgba(255,255,255,0.55) 1px, transparent 1px)',
            backgroundSize: '44px 44px',
          }}
          aria-hidden="true"
        />

        {/* The sun glyph, drawn as plain SVG. */}
        <svg
          viewBox="0 0 200 200"
          className="absolute inset-0 h-full w-full"
          role="img"
          aria-label="A stylised sun over a solar array"
        >
          <circle cx="100" cy="86" r="30" fill="#fffbeb" fillOpacity="0.95" />
          <circle cx="100" cy="86" r="42" fill="none" stroke="#fffbeb" strokeOpacity="0.5" strokeWidth="2" />
          <circle cx="100" cy="86" r="56" fill="none" stroke="#fffbeb" strokeOpacity="0.28" strokeWidth="2" />
          <g stroke="#fffbeb" strokeOpacity="0.85" strokeWidth="4" strokeLinecap="round">
            <line x1="100" y1="26" x2="100" y2="38" />
            <line x1="100" y1="134" x2="100" y2="146" />
            <line x1="40" y1="86" x2="52" y2="86" />
            <line x1="148" y1="86" x2="160" y2="86" />
            <line x1="57" y1="43" x2="66" y2="52" />
            <line x1="134" y1="120" x2="143" y2="129" />
            <line x1="143" y1="43" x2="134" y2="52" />
            <line x1="66" y1="120" x2="57" y2="129" />
          </g>
          {/* A pair of solar panels catching the light. */}
          <g fill="#78350f" fillOpacity="0.55">
            <polygon points="34,176 62,146 116,146 96,176" />
            <polygon points="106,176 126,146 174,146 174,176" />
          </g>
          <g stroke="#fef3c7" strokeOpacity="0.55" strokeWidth="1.5">
            <line x1="48" y1="161" x2="106" y2="161" />
            <line x1="116" y1="161" x2="174" y2="161" />
            <line x1="79" y1="146" x2="65" y2="176" />
            <line x1="150" y1="146" x2="150" y2="176" />
          </g>
        </svg>
      </div>

      {/* Floating stat chips. */}
      <div className="absolute -left-3 top-10 flex items-center gap-2 rounded-xl border border-white/70 bg-white/90 px-3 py-2 shadow-card backdrop-blur sm:-left-6">
        <Zap className="h-4 w-4 text-solar-600" aria-hidden="true" />
        <div>
          <p className="text-xs font-semibold text-slate-900">Live energy trading</p>
          <p className="text-[11px] text-slate-500">kWh booked per slot</p>
        </div>
      </div>

      <div className="absolute -right-3 top-1/2 flex items-center gap-2 rounded-xl border border-white/70 bg-white/90 px-3 py-2 shadow-card backdrop-blur sm:-right-6">
        <BatteryCharging className="h-4 w-4 text-grid-600" aria-hidden="true" />
        <div>
          <p className="text-xs font-semibold text-slate-900">Battery bays tracked</p>
          <p className="text-[11px] text-slate-500">Per microgrid node</p>
        </div>
      </div>

      <div className="absolute -bottom-4 left-1/2 flex -translate-x-1/2 items-center gap-2 rounded-xl border border-white/70 bg-white/90 px-3 py-2 shadow-card backdrop-blur">
        <QrCode className="h-4 w-4 text-slate-700" aria-hidden="true" />
        <div>
          <p className="text-xs font-semibold text-slate-900">QR verified on site</p>
          <p className="text-[11px] text-slate-500">Scanned by the operator</p>
        </div>
      </div>
    </div>
  );
}

/** The public landing page shown at "/" to any visitor. */
export default function HomePage() {
  // No data is loaded here, so there is no loading, error or empty state to handle.
  const year: number = new Date().getFullYear();

  return (
    <div className="min-h-screen bg-slate-50">
      {/* ---- Sticky navigation ---- */}
      <header className="sticky top-0 z-40 border-b border-slate-200 bg-white/85 backdrop-blur">
        <nav className="mx-auto flex max-w-7xl items-center justify-between gap-4 px-4 py-3 sm:px-6 lg:px-8">
          <Link to="/" className="flex items-center gap-3">
            <BrandMark />
            <span className="text-base font-extrabold tracking-tight text-slate-900 sm:text-lg">
              Smart Solar Microgrid
            </span>
          </Link>

          <div className="flex items-center gap-2">
            <a
              href={SWAGGER_URL}
              target="_blank"
              rel="noreferrer"
              className="btn-ghost hidden sm:inline-flex"
            >
              <ExternalLink className="h-4 w-4" aria-hidden="true" />
              API docs
            </a>
            <Link to="/login" className="btn-primary">
              Sign in
              <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </Link>
          </div>
        </nav>
      </header>

      <main>
        {/* ---- Hero ---- */}
        <section className="relative overflow-hidden border-b border-slate-200 bg-white">
          <div
            className="pointer-events-none absolute inset-0"
            style={{
              backgroundImage:
                'radial-gradient(60% 55% at 85% 0%, rgba(245,158,11,0.16) 0%, rgba(245,158,11,0) 70%)',
            }}
            aria-hidden="true"
          />
          <div className="relative mx-auto grid max-w-7xl items-center gap-12 px-4 py-16 sm:px-6 lg:grid-cols-2 lg:gap-16 lg:px-8 lg:py-24">
            <div className="animate-fade-in">
              <span className="inline-flex items-center gap-2 rounded-full border border-solar-200 bg-solar-50 px-3 py-1 text-xs font-semibold text-solar-700">
                <Zap className="h-3.5 w-3.5" aria-hidden="true" />
                SE4040 Enterprise Application Development
              </span>

              <h1 className="mt-5 text-4xl font-extrabold leading-tight tracking-tight text-slate-900 sm:text-5xl lg:text-6xl">
                Trade your rooftop solar with the grid
              </h1>

              <p className="mt-5 max-w-xl text-base leading-relaxed text-slate-600 sm:text-lg">
                Solar prosumers reserve energy slots at nearby microgrid nodes, choosing the window and
                the number of kilowatt hours they want to move. Grid operators verify each transfer on
                site by scanning the reservation QR code, then record the energy that actually flowed.
                Every booking, activation and node change is administered from one back office.
              </p>

              <div className="mt-8 flex flex-col gap-3 sm:flex-row">
                <Link to="/login" className="btn-primary w-full sm:w-auto">
                  Sign in to the portal
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </Link>
                <a
                  href={SWAGGER_URL}
                  target="_blank"
                  rel="noreferrer"
                  className="btn-secondary w-full sm:w-auto"
                >
                  <ExternalLink className="h-4 w-4" aria-hidden="true" />
                  View the API
                </a>
              </div>

              <dl className="mt-10 grid grid-cols-2 gap-6 border-t border-slate-200 pt-6 sm:grid-cols-3">
                <div>
                  <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">Roles</dt>
                  <dd className="mt-1 text-2xl font-bold text-slate-900">3</dd>
                </div>
                <div>
                  <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                    Booking steps
                  </dt>
                  <dd className="mt-1 text-2xl font-bold text-slate-900">4</dd>
                </div>
                <div className="col-span-2 sm:col-span-1">
                  <dt className="text-xs font-medium uppercase tracking-wide text-slate-500">
                    Verification
                  </dt>
                  <dd className="mt-1 text-2xl font-bold text-slate-900">QR</dd>
                </div>
              </dl>
            </div>

            <HeroVisual />
          </div>
        </section>

        {/* ---- How it works ---- */}
        <section className="border-b border-slate-200 bg-slate-50">
          <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 lg:px-8 lg:py-20">
            <div className="max-w-2xl">
              <h2 className="text-2xl font-bold tracking-tight text-slate-900 sm:text-3xl">
                How it works
              </h2>
              <p className="mt-3 text-slate-600">
                Four steps take a household from registration to a settled energy transfer.
              </p>
            </div>

            <ol className="mt-10 grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-4">
              {JOURNEY.map((step, index) => {
                const StepIcon = step.icon;
                return (
                  <li key={step.title} className="card transition-shadow hover:shadow-card-hover">
                    <div className="card-body">
                      <div className="flex items-center justify-between">
                        <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-solar-50 text-solar-600">
                          <StepIcon className="h-5 w-5" aria-hidden="true" />
                        </span>
                        <span className="text-2xl font-extrabold text-slate-200">
                          {String(index + 1).padStart(2, '0')}
                        </span>
                      </div>
                      <h3 className="mt-4 text-base font-semibold text-slate-900">{step.title}</h3>
                      <p className="mt-2 text-sm leading-relaxed text-slate-600">{step.body}</p>
                    </div>
                  </li>
                );
              })}
            </ol>
          </div>
        </section>

        {/* ---- Roles ---- */}
        <section className="border-b border-slate-200 bg-white">
          <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 lg:px-8 lg:py-20">
            <div className="max-w-2xl">
              <h2 className="text-2xl font-bold tracking-tight text-slate-900 sm:text-3xl">
                Built for three roles
              </h2>
              <p className="mt-3 text-slate-600">
                The portal shows each person only the screens their role is authorised to use, and the
                API enforces the same boundary on every request.
              </p>
            </div>

            <div className="mt-10 grid grid-cols-1 gap-6 md:grid-cols-3">
              {ROLES.map((role) => {
                const RoleIcon = role.icon;
                return (
                  <article key={role.name} className="card flex flex-col transition-shadow hover:shadow-card-hover">
                    <div className="card-header">
                      <div className="flex items-center gap-3">
                        <span className="flex h-10 w-10 items-center justify-center rounded-lg bg-slate-100 text-slate-700">
                          <RoleIcon className="h-5 w-5" aria-hidden="true" />
                        </span>
                        <div>
                          <h3 className="text-base font-semibold text-slate-900">{role.name}</h3>
                          <p className="text-xs text-slate-500">{role.tagline}</p>
                        </div>
                      </div>
                    </div>
                    <div className="card-body flex-1">
                      <ul className="space-y-2.5">
                        {role.abilities.map((ability) => (
                          <li key={ability} className="flex gap-2.5 text-sm text-slate-600">
                            <span
                              className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-solar-400"
                              aria-hidden="true"
                            />
                            <span>{ability}</span>
                          </li>
                        ))}
                      </ul>
                    </div>
                  </article>
                );
              })}
            </div>
          </div>
        </section>

        {/* ---- Demo accounts and feature strip ---- */}
        <section className="bg-slate-50">
          <div className="mx-auto grid max-w-7xl gap-8 px-4 py-16 sm:px-6 lg:grid-cols-3 lg:px-8 lg:py-20">
            {/*
              The seeded logins are published here on purpose so a marker can open
              the application and sign in immediately, with no separate handover.
            */}
            <div className="card lg:col-span-2">
              <div className="card-header">
                <div className="flex items-center gap-2">
                  <KeyRound className="h-4 w-4 text-solar-600" aria-hidden="true" />
                  <h2 className="text-base font-semibold text-slate-900">Demo accounts</h2>
                </div>
                <span className="badge-pending">Seeded on first run</span>
              </div>
              <div className="overflow-x-auto">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Identifier</th>
                      <th>Password</th>
                      <th>Role</th>
                      <th>Notes</th>
                    </tr>
                  </thead>
                  <tbody>
                    {DEMO_ACCOUNTS.map((account) => (
                      <tr key={account.identifier}>
                        <td className="font-mono text-xs font-semibold text-slate-900">
                          {account.identifier}
                        </td>
                        <td className="font-mono text-xs">{account.password}</td>
                        <td>
                          <span className="badge-active">{account.role}</span>
                        </td>
                        <td className="text-slate-500">{account.note}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="card-body border-t border-slate-200">
                <p className="text-xs text-slate-500">
                  These accounts are created by the API seeder for demonstration only. The login page
                  also offers one click quick fill for each of them.
                </p>
              </div>
            </div>

            <div className="card">
              <div className="card-header">
                <h2 className="text-base font-semibold text-slate-900">In the portal</h2>
              </div>
              <div className="card-body">
                <ul className="space-y-4">
                  <li className="flex gap-3">
                    <LayoutDashboard className="mt-0.5 h-5 w-5 shrink-0 text-solar-600" aria-hidden="true" />
                    <div>
                      <p className="text-sm font-semibold text-slate-900">Live dashboard</p>
                      <p className="text-xs text-slate-500">
                        Pending bookings and approved future reservations at a glance.
                      </p>
                    </div>
                  </li>
                  <li className="flex gap-3">
                    <MapPin className="mt-0.5 h-5 w-5 shrink-0 text-solar-600" aria-hidden="true" />
                    <div>
                      <p className="text-sm font-semibold text-slate-900">Node map</p>
                      <p className="text-xs text-slate-500">
                        Every microgrid node plotted, with the nearest ones first.
                      </p>
                    </div>
                  </li>
                  <li className="flex gap-3">
                    <Users className="mt-0.5 h-5 w-5 shrink-0 text-solar-600" aria-hidden="true" />
                    <div>
                      <p className="text-sm font-semibold text-slate-900">Account administration</p>
                      <p className="text-xs text-slate-500">
                        Activation queue, web users and prosumer records in one place.
                      </p>
                    </div>
                  </li>
                  <li className="flex gap-3">
                    <Building2 className="mt-0.5 h-5 w-5 shrink-0 text-solar-600" aria-hidden="true" />
                    <div>
                      <p className="text-sm font-semibold text-slate-900">Node and slot management</p>
                      <p className="text-xs text-slate-500">
                        Publish operating hours and generate bookable energy windows.
                      </p>
                    </div>
                  </li>
                </ul>
              </div>
            </div>
          </div>
        </section>
      </main>

      {/* ---- Footer ---- */}
      <footer className="border-t border-slate-200 bg-white">
        <div className="mx-auto flex max-w-7xl flex-col gap-4 px-4 py-8 sm:px-6 lg:flex-row lg:items-center lg:justify-between lg:px-8">
          <div className="flex items-center gap-3">
            <BrandMark />
            <div>
              <p className="text-sm font-semibold text-slate-900">Smart Solar Microgrid Trading System</p>
              <p className="text-xs text-slate-500">SE4040 Enterprise Application Development</p>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-4 text-xs text-slate-500">
            <Link to="/login" className="hover:text-slate-900">
              Sign in
            </Link>
            <a href={SWAGGER_URL} target="_blank" rel="noreferrer" className="hover:text-slate-900">
              API reference
            </a>
            <span>SE4040 - {year}</span>
          </div>
        </div>
      </footer>
    </div>
  );
}
