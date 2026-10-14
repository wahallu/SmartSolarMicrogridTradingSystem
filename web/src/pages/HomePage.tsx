/* =============================================================================
 * File        : src/pages/HomePage.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Pages / Home
 * Description : Introduces the system, its workflow and demonstration
 *               accounts.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-20
 * =============================================================================
 */

import { useEffect, useRef, useState } from 'react';
import type { KeyboardEvent, ReactNode } from 'react';
import { Link } from 'react-router-dom';
import type { LucideIcon } from 'lucide-react';
import { ArrowRight, Check, ExternalLink, ScanLine, ShieldCheck, Sun } from 'lucide-react';

/** the dev URL for the API's interactive docs. */
const SWAGGER_URL = 'http://localhost:5218/swagger';

/** One step of the prosumer journey. The title is the action, so no numbering is needed. */
interface JourneyStep {
  title: string;
  body: string;
}

/** One of the three principals the system recognises. */
interface RolePanel {
  id: 'backoffice' | 'operator' | 'prosumer';
  name: string;
  icon: LucideIcon;
  summary: string;
  signsInWith: string;
  abilities: string[];
}

/** A seeded account a marker can sign in with straight away. */
interface DemoAccount {
  identifier: string;
  password: string;
  role: string;
  note: string;
}

/** The prosumer journey, in order. */
const JOURNEY: JourneyStep[] = [
  {
    title: 'Register with your NIC',
    body:
      'Your National Identity Card number is your account key. A new account stays pending until a back office officer activates it.',
  },
  {
    title: 'Reserve a slot',
    body:
      'Choose a microgrid node, a published window inside the next seven days, and the kWh you plan to trade.',
  },
  {
    title: 'Receive a QR code',
    body:
      'Once the booking is approved, the API issues a signed QR code that proves the reservation at the node.',
  },
  {
    title: 'Transfer and record',
    body:
      'The grid operator scans the code, verifies it against the API and records the energy that actually moved.',
  },
];

/** What each role is responsible for. */
const ROLES: RolePanel[] = [
  {
    id: 'backoffice',
    name: 'Back office',
    icon: ShieldCheck,
    summary: 'Runs the network from one console.',
    signsInWith: 'Username',
    abilities: [
      'Create and manage web user accounts',
      'Activate or deactivate prosumer accounts',
      'Register microgrid nodes and publish slots',
      'Approve, reject and audit every reservation',
    ],
  },
  {
    id: 'operator',
    name: 'Grid operator',
    icon: ScanLine,
    summary: 'Works at the node and closes each transfer.',
    signsInWith: 'Username',
    abilities: [
      'See the booking queue for the assigned node',
      'Scan a prosumer QR code and verify it',
      'Record the delivered energy on completion',
      'Keep node availability and hours current',
    ],
  },
  {
    id: 'prosumer',
    name: 'Prosumer',
    icon: Sun,
    summary: 'Trades surplus rooftop power from a phone.',
    signsInWith: 'NIC number',
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
    role: 'Back office',
    note: 'Full administration',
  },
  {
    identifier: 'operator1',
    password: 'Operator@123',
    role: 'Grid operator',
    note: 'Assigned to node STN-001',
  },
  {
    identifier: '200112301234',
    password: 'Prosumer@123',
    role: 'Prosumer',
    note: 'Signs in with a NIC',
  },
];

/** The compact sun mark reused in the nav bar and the footer. */
function BrandMark() {
  // A flat tile: the brand is one amber, with dark ink on it so it stays legible.
  return (
    <span className="flex h-8 w-8 items-center justify-center rounded-md bg-accent text-accent-ink">
      <Sun className="h-[18px] w-[18px]" aria-hidden="true" />
    </span>
  );
}

/**
 * Fades its children up once they scroll into view. The motion only sequences
 * content that arrives below the fold; it is switched off for reduced motion.
 */
function Reveal({ children, className = '' }: { children: ReactNode; className?: string }) {
  const ref = useRef<HTMLDivElement>(null);
  // Without IntersectionObserver (or with reduced motion) content is simply visible.
  const [shown, setShown] = useState<boolean>(
    () =>
      typeof IntersectionObserver === 'undefined' ||
      window.matchMedia('(prefers-reduced-motion: reduce)').matches,
  );

  useEffect(() => {
    const node = ref.current;
    if (!node || shown) return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          setShown(true);
          observer.disconnect();
        }
      },
      { threshold: 0.15 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [shown]);

  return (
    <div
      ref={ref}
      className={`${className} transition duration-700 ease-out motion-reduce:transition-none ${
        shown ? 'translate-y-0 opacity-100' : 'translate-y-4 opacity-0 motion-reduce:opacity-100'
      }`}
    >
      {children}
    </div>
  );
}

/** Tabbed role explainer, so three responsibilities share one panel instead of three cards. */
function RoleTabs() {
  const [active, setActive] = useState<RolePanel['id']>('backoffice');
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const role = ROLES.find((item) => item.id === active) ?? ROLES[0];
  const RoleIcon = role.icon;

  // Arrow keys move between tabs, as the tabs pattern expects.
  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
    event.preventDefault();
    const step = event.key === 'ArrowRight' ? 1 : -1;
    const next = (index + step + ROLES.length) % ROLES.length;
    setActive(ROLES[next].id);
    tabRefs.current[next]?.focus();
  }

  return (
    <div>
      <div
        role="tablist"
        aria-label="Roles"
        className="inline-flex rounded-lg border border-slate-200 bg-white p-1"
      >
        {ROLES.map((item, index) => {
          const selected = item.id === active;
          return (
            <button
              key={item.id}
              ref={(node) => {
                tabRefs.current[index] = node;
              }}
              type="button"
              role="tab"
              id={`role-tab-${item.id}`}
              aria-selected={selected}
              aria-controls={`role-panel-${item.id}`}
              tabIndex={selected ? 0 : -1}
              onClick={() => setActive(item.id)}
              onKeyDown={(event) => handleKeyDown(event, index)}
              className={`rounded-md px-4 py-2 text-sm font-medium transition-colors ${
                selected
                  ? 'bg-slate-900 text-white'
                  : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900'
              }`}
            >
              {item.name}
            </button>
          );
        })}
      </div>

      <div
        role="tabpanel"
        id={`role-panel-${role.id}`}
        aria-labelledby={`role-tab-${role.id}`}
        className="mt-6 grid gap-8 rounded-xl border border-slate-200 bg-white p-6 sm:p-8 lg:grid-cols-5 lg:gap-12"
      >
        <div className="lg:col-span-2">
          <span className="flex h-10 w-10 items-center justify-center rounded-lg border border-slate-200 bg-slate-50 text-slate-800">
            <RoleIcon className="h-5 w-5" aria-hidden="true" />
          </span>
          <h3 className="mt-4 text-xl font-semibold text-slate-900">{role.name}</h3>
          <p className="mt-1.5 text-slate-600">{role.summary}</p>
          <p className="mt-5 text-sm text-slate-600">
            Signs in with <span className="font-medium text-slate-900">{role.signsInWith}</span>
          </p>
        </div>

        <ul className="divide-y divide-slate-200 lg:col-span-3">
          {role.abilities.map((ability) => (
            <li key={ability} className="flex items-start gap-3 py-3.5 first:pt-0 last:pb-0">
              <Check className="mt-0.5 h-4 w-4 shrink-0 text-grid-700" aria-hidden="true" />
              <span className="text-slate-800">{ability}</span>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

/** The public landing page shown at "/" to any visitor. */
export default function HomePage() {
  // No data is loaded here, so there is no loading, error or empty state to handle.
  const year: number = new Date().getFullYear();

  return (
    <div className="min-h-[100dvh] bg-slate-50">
      {/* ---- Navigation ---- */}
      <header className="sticky top-0 z-40 border-b border-slate-200 bg-white/90 backdrop-blur supports-[backdrop-filter]:bg-white/80">
        <nav className="mx-auto flex h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6 lg:px-8">
          <Link to="/" className="flex items-center gap-3">
            <BrandMark />
            <span className="text-base font-semibold tracking-tight text-slate-900">
              Smart Solar Microgrid
            </span>
          </Link>

          <Link to="/login" className="btn-primary">
            Sign in
          </Link>
        </nav>
      </header>

      <main>
        {/* ---- Hero: one message, one photograph, one decision ---- */}
        <section className="border-b border-slate-200 bg-white">
          <div className="mx-auto grid max-w-7xl items-center gap-10 px-4 py-12 sm:px-6 lg:grid-cols-12 lg:gap-14 lg:px-8 lg:py-16">
            <div className="animate-fade-in lg:col-span-6">
              <h1 className="text-balance text-4xl font-semibold leading-[1.08] tracking-tight text-slate-900 lg:text-[2.75rem] xl:text-5xl">
                Trade rooftop solar with your local grid
              </h1>

              <p className="mt-5 max-w-md text-lg leading-relaxed text-slate-600">
                Book an energy slot at a nearby microgrid node, show a QR code on arrival, and get
                the transfer recorded.
              </p>

              <div className="mt-8 flex flex-col gap-3 sm:flex-row">
                <Link to="/login" className="btn-primary px-5 py-2.5">
                  Sign in
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </Link>
                <a
                  href={SWAGGER_URL}
                  target="_blank"
                  rel="noreferrer"
                  className="btn-secondary px-5 py-2.5"
                >
                  API documentation
                  <ExternalLink className="h-4 w-4" aria-hidden="true" />
                </a>
              </div>
            </div>

            <div className="lg:col-span-6">
              <img
                src="/images/solar-field-inspection.jpg"
                alt="A technician in a hard hat and high visibility vest checks a row of solar panels in an open field."
                width={1400}
                height={933}
                fetchPriority="high"
                className="aspect-[4/3] w-full rounded-xl border border-slate-200 object-cover"
              />
            </div>
          </div>
        </section>

        {/* ---- How a transfer works: photo beside a plain ordered list ---- */}
        <section className="border-b border-slate-200">
          <div className="mx-auto grid max-w-7xl gap-10 px-4 py-16 sm:px-6 lg:grid-cols-12 lg:gap-14 lg:px-8 lg:py-24">
            <Reveal className="lg:col-span-5">
              <img
                src="/images/rooftop-array.jpg"
                alt="A technician reads a document beside a large array of rooftop solar panels under a clear sky."
                width={1400}
                height={933}
                loading="lazy"
                className="aspect-[4/5] w-full rounded-xl border border-slate-200 object-cover object-[28%_50%]"
              />
            </Reveal>

            <div className="lg:col-span-7 lg:pt-4">
              <h2 className="max-w-lg text-3xl font-semibold tracking-tight text-slate-900">
                From registration to a recorded transfer
              </h2>

              <ol className="mt-8 divide-y divide-slate-200 border-y border-slate-200">
                {JOURNEY.map((step) => (
                  <li key={step.title}>
                    <Reveal className="py-5">
                      <h3 className="text-base font-semibold text-slate-900">{step.title}</h3>
                      <p className="mt-1.5 max-w-xl text-slate-600">{step.body}</p>
                    </Reveal>
                  </li>
                ))}
              </ol>
            </div>
          </div>
        </section>

        {/* ---- Roles ---- */}
        <section className="border-b border-slate-200 bg-white">
          <div className="mx-auto max-w-7xl px-4 py-16 sm:px-6 lg:px-8 lg:py-24">
            <h2 className="max-w-2xl text-3xl font-semibold tracking-tight text-slate-900">
              Each role sees only its own screens
            </h2>
            <p className="mt-3 max-w-xl text-slate-600">
              The portal hides what a role may not use, and the API refuses the same requests on the
              server.
            </p>

            <div className="mt-8">
              <RoleTabs />
            </div>
          </div>
        </section>

        {/* ---- Demo accounts ---- */}
        {/*
          The seeded logins are published here on purpose so a marker can open
          the application and sign in immediately, with no separate handover.
        */}
        <section className="bg-slate-100">
          <div className="mx-auto grid max-w-7xl gap-8 px-4 py-14 sm:px-6 lg:grid-cols-12 lg:gap-14 lg:px-8 lg:py-20">
            <div className="lg:col-span-4">
              <h2 className="text-2xl font-semibold tracking-tight text-slate-900">
                Try it with a demo account
              </h2>
              <p className="mt-3 text-slate-600">
                The API seeds these accounts on first run. They are for demonstration only.
              </p>
            </div>

            <ul className="divide-y divide-slate-200 rounded-xl border border-slate-200 bg-white lg:col-span-8">
              {DEMO_ACCOUNTS.map((account) => (
                <li
                  key={account.identifier}
                  className="flex flex-col gap-3 px-5 py-4 sm:flex-row sm:items-center sm:justify-between"
                >
                  <div className="min-w-0">
                    <p className="text-sm font-semibold text-slate-900">{account.role}</p>
                    <p className="text-sm text-slate-600">{account.note}</p>
                  </div>
                  <dl className="flex flex-wrap gap-x-8 gap-y-1 font-mono text-sm">
                    <div className="flex gap-2">
                      <dt className="text-slate-600">ID</dt>
                      <dd className="font-medium text-slate-900">{account.identifier}</dd>
                    </div>
                    <div className="flex gap-2">
                      <dt className="text-slate-600">Password</dt>
                      <dd className="font-medium text-slate-900">{account.password}</dd>
                    </div>
                  </dl>
                </li>
              ))}
            </ul>
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
              <p className="text-xs text-slate-600">
                SE4040 Enterprise Application Development, {year}
              </p>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-x-6 gap-y-2 text-sm">
            <Link to="/login" className="font-medium text-slate-700 hover:text-slate-900">
              Sign in
            </Link>
            <a
              href={SWAGGER_URL}
              target="_blank"
              rel="noreferrer"
              className="font-medium text-slate-700 hover:text-slate-900"
            >
              API documentation
            </a>
          </div>
        </div>
      </footer>
    </div>
  );
}
