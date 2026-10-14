/* =============================================================================
 * File        : src/components/ui.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Components / ui
 * Description : Provides the reusable buttons, cards, fields and feedback
 *               components.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-20
 * =============================================================================
 */

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import {
  AlertCircle,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Info,
  RefreshCw,
  Search,
  X,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { accountBadgeClass, reservationBadgeClass, stationBadgeClass } from '../utils/format';

/* -------------------------------------------------------------------------- */
/* Spinner                                                                     */
/* -------------------------------------------------------------------------- */

/** Sizes are looked up in a map so Tailwind can see the complete class names. */
const SPINNER_SIZES: Record<'sm' | 'md' | 'lg', string> = {
  sm: 'h-4 w-4 border-2',
  md: 'h-6 w-6 border-2',
  lg: 'h-10 w-10 border-[3px]',
};

/** A spinning ring that takes the colour of the text around it. */
export function Spinner({ size = 'md' }: { size?: 'sm' | 'md' | 'lg' }) {
  // Purely decorative markup, so the accessible name lives on the role="status" wrapper.
  return (
    <span
      role="status"
      aria-label="Loading"
      className={`inline-block animate-spin rounded-full border-current border-r-transparent ${SPINNER_SIZES[size]}`}
    />
  );
}

/* -------------------------------------------------------------------------- */
/* Page loader                                                                 */
/* -------------------------------------------------------------------------- */

/** Full panel loading state used while a screen's first request is in flight. */
export function PageLoader() {
  // Reserves the same vertical space the loaded screen will use so nothing jumps.
  return (
    <div className="space-y-6" role="status" aria-label="Loading" aria-live="polite">
      <div className="space-y-2">
        <div className="skeleton h-7 w-56" />
        <div className="skeleton h-4 w-80 max-w-full" />
      </div>
      <div className="grid grid-cols-2 gap-px overflow-hidden rounded-xl border border-slate-200 bg-slate-200 lg:grid-cols-4">
        {[0, 1, 2, 3].map((index) => (
          <div key={index} className="space-y-3 bg-white p-5">
            <div className="skeleton h-4 w-24" />
            <div className="skeleton h-8 w-16" />
          </div>
        ))}
      </div>
      <div className="card">
        <div className="space-y-3 p-5">
          {[0, 1, 2, 3, 4].map((index) => (
            <div key={index} className="skeleton h-9 w-full" />
          ))}
        </div>
      </div>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Error state                                                                 */
/* -------------------------------------------------------------------------- */

/** Shows a failed request, with an optional retry action. */
export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  // The message is passed straight through: when it came from an ApiError it is
  // the API's own wording, and the API owns every business rule.
  return (
    <div className="card border-red-300 bg-red-50" role="alert">
      <div className="card-body flex flex-col items-start gap-3 sm:flex-row sm:items-center">
        <AlertCircle className="h-5 w-5 shrink-0 text-red-700" aria-hidden="true" />
        <p className="flex-1 text-sm font-medium text-red-900">{message}</p>
        {onRetry && (
          <button type="button" className="btn-secondary btn-sm" onClick={onRetry}>
            <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
            Try again
          </button>
        )}
      </div>
    </div>
  );
}



/** the message displayed when a list is empty. */
export function EmptyState({
  icon: Icon,
  title,
  message,
  action,
}: {
  icon: LucideIcon;
  title: string;
  message: string;
  action?: ReactNode;
}) {
  // An empty list is a normal outcome, so this never looks like an error.
  return (
    <div className="flex flex-col items-center justify-center px-6 py-12 text-center">
      <div className="mb-4 flex h-11 w-11 items-center justify-center rounded-lg border border-slate-200 bg-slate-50">
        <Icon className="h-5 w-5 text-slate-500" aria-hidden="true" />
      </div>
      <h3 className="text-base font-semibold text-slate-900">{title}</h3>
      <p className="mt-1 max-w-sm text-sm text-slate-600">{message}</p>
      {action && <div className="mt-5">{action}</div>}
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Stat card                                                                   */
/* -------------------------------------------------------------------------- */

/**
 * Tone is resolved through an explicit map rather than by building a class name
 * from a template literal, because Tailwind only ships classes it can find as
 * complete strings in the source. Tone colours only the icon: the figure itself
 * stays ink so a row of stats reads as one instrument strip.
 */
const STAT_TONES: Record<'solar' | 'grid' | 'blue' | 'slate' | 'red', string> = {
  solar: 'text-solar-600',
  grid: 'text-grid-600',
  blue: 'text-blue-600',
  slate: 'text-slate-500',
  red: 'text-red-600',
};

/** A single dashboard figure with its label and icon. */
export function StatCard({
  label,
  value,
  icon: Icon,
  tone = 'solar',
  hint,
}: {
  label: string;
  value: string | number;
  icon: LucideIcon;
  tone?: 'solar' | 'grid' | 'blue' | 'slate' | 'red';
  hint?: string;
}) {
  // Every figure shown here is calculated by the API; the card only renders it.
  // Rendered as a cell so a StatGroup can lay several of them out in one frame.
  return (
    <div className="bg-white px-5 py-4">
      <div className="flex items-start justify-between gap-3">
        <p className="text-sm font-medium leading-snug text-slate-600">{label}</p>
        <Icon className={`mt-0.5 h-4 w-4 shrink-0 ${STAT_TONES[tone]}`} aria-hidden="true" />
      </div>
      <p className="mt-2 text-2xl font-semibold tabular-nums leading-none tracking-tight text-slate-900 sm:text-3xl">
        {value}
      </p>
      {hint && <p className="mt-2 text-xs text-slate-600">{hint}</p>}
    </div>
  );
}

/**
 * Frames several StatCards in one bordered strip. The 1px gaps show the frame's
 * background, which draws the dividers without a border on every cell.
 */
export function StatGroup({ children, columns = 4 }: { children: ReactNode; columns?: 3 | 4 }) {
  // Two columns on phones, the full count from the lg breakpoint up.
  return (
    <div
      className={`grid grid-cols-2 gap-px overflow-hidden rounded-xl border border-slate-200 bg-slate-200 ${
        columns === 3 ? 'lg:grid-cols-3' : 'lg:grid-cols-4'
      }`}
    >
      {children}
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Badge                                                                       */
/* -------------------------------------------------------------------------- */

/** Status pill whose colour depends on which kind of status it is showing. */
export function Badge({ status, kind }: { status: string; kind: 'reservation' | 'account' | 'station' }) {
  // The status string itself is a server verdict; the UI never derives it.
  const className =
    kind === 'reservation'
      ? reservationBadgeClass(status)
      : kind === 'account'
        ? accountBadgeClass(status)
        : stationBadgeClass(status);

  return <span className={className}>{status}</span>;
}

/* -------------------------------------------------------------------------- */
/* Modal                                                                       */
/* -------------------------------------------------------------------------- */

/** Panel widths, again as whole class names so Tailwind keeps them. */
const MODAL_SIZES: Record<'sm' | 'md' | 'lg', string> = {
  sm: 'max-w-sm',
  md: 'max-w-lg',
  lg: 'max-w-3xl',
};

/** A centred dialog with a backdrop, Escape support and a locked page scroll. */
export function Modal({
  open,
  title,
  onClose,
  children,
  footer,
  size = 'md',
}: {
  open: boolean;
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  size?: 'sm' | 'md' | 'lg';
}) {
  // Escape closes the dialog and the page behind it stops scrolling while it is open.
  useEffect(() => {
    if (!open) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      document.body.style.overflow = previousOverflow;
    };
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div
        className="absolute inset-0 bg-slate-950/60"
        onClick={onClose}
        aria-hidden="true"
      />
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`relative z-10 w-full ${MODAL_SIZES[size]} animate-fade-in rounded-xl border border-slate-200 bg-white shadow-card-hover`}
      >
        <div className="card-header">
          <h2 className="text-base font-semibold text-slate-900">{title}</h2>
          <button
            type="button"
            className="btn-ghost btn-sm"
            onClick={onClose}
            aria-label="Close dialog"
          >
            <X className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
        <div className="max-h-[70vh] overflow-y-auto px-5 py-4">{children}</div>
        {footer && (
          <div className="flex flex-wrap items-center justify-end gap-2 border-t border-slate-200 px-5 py-4">
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Confirm dialog                                                              */
/* -------------------------------------------------------------------------- */

/** Yes / no dialog used before cancelling, rejecting or deactivating anything. */
export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = 'Confirm',
  tone = 'primary',
  onConfirm,
  onCancel,
  busy = false,
}: {
  open: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  tone?: 'danger' | 'primary';
  onConfirm: () => void;
  onCancel: () => void;
  busy?: boolean;
}) {
  // Confirming only asks the API to act - the API decides whether the rule allows it.
  return (
    <Modal
      open={open}
      title={title}
      size="sm"
      onClose={busy ? () => undefined : onCancel}
      footer={
        <>
          <button type="button" className="btn-secondary" onClick={onCancel} disabled={busy}>
            Cancel
          </button>
          <button
            type="button"
            className={tone === 'danger' ? 'btn-danger' : 'btn-primary'}
            onClick={onConfirm}
            disabled={busy}
          >
            {busy && <Spinner size="sm" />}
            {confirmLabel}
          </button>
        </>
      }
    >
      <p className="text-sm text-slate-600">{message}</p>
    </Modal>
  );
}

/* -------------------------------------------------------------------------- */
/* Toasts                                                                      */
/* -------------------------------------------------------------------------- */

/** The kinds of toast a screen can raise. */
type ToastKind = 'success' | 'error' | 'info';

/** One queued toast. */
interface ToastItem {
  id: number;
  kind: ToastKind;
  message: string;
}

/** What useToast hands back to a screen. */
interface ToastApi {
  success: (msg: string) => void;
  error: (msg: string) => void;
  info: (msg: string) => void;
}

const ToastContext = createContext<ToastApi | null>(null);

/** Icon per toast kind. */
const TOAST_ICONS: Record<ToastKind, LucideIcon> = {
  success: CheckCircle2,
  error: AlertCircle,
  info: Info,
};

/** Colours per toast kind, written out in full for Tailwind's scanner. */
const TOAST_TONES: Record<ToastKind, string> = {
  success: 'border-grid-300 bg-grid-50 text-grid-900',
  error: 'border-red-300 bg-red-50 text-red-900',
  info: 'border-blue-300 bg-blue-50 text-blue-900',
};

/** How long a toast stays on screen, in milliseconds. */
const TOAST_TIMEOUT_MS = 4500;

/** Provides the toast queue to the whole application. */
export function ToastProvider({ children }: { children: ReactNode }) {
  // The queue lives here so any screen can raise a toast without prop drilling.
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const nextId = useRef<number>(1);

  const dismiss = useCallback((id: number) => {
    // Removing by id keeps the other toasts on screen.
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  const push = useCallback(
    (kind: ToastKind, message: string) => {
      // Auto dismiss keeps the corner clear without the user having to click.
      const id = nextId.current++;
      setToasts((current) => [...current, { id, kind, message }]);
      window.setTimeout(() => dismiss(id), TOAST_TIMEOUT_MS);
    },
    [dismiss],
  );

  const success = useCallback((msg: string) => push('success', msg), [push]);
  const error = useCallback((msg: string) => push('error', msg), [push]);
  const info = useCallback((msg: string) => push('info', msg), [push]);

  // One stable context value so consumers do not re-render on every toast.
  const api = useMemo<ToastApi>(() => ({ success, error, info }), [success, error, info]);

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div className="pointer-events-none fixed bottom-4 right-4 z-[60] flex w-full max-w-sm flex-col gap-2 px-4 sm:px-0">
        {toasts.map((toast) => {
          const Icon = TOAST_ICONS[toast.kind];
          return (
            <div
              key={toast.id}
              role="status"
              className={`pointer-events-auto flex animate-fade-in items-start gap-3 rounded-lg border px-4 py-3 shadow-card-hover ${TOAST_TONES[toast.kind]}`}
            >
              <Icon className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <p className="flex-1 text-sm font-medium">{toast.message}</p>
              <button
                type="button"
                className="shrink-0 rounded p-0.5 opacity-60 transition-opacity hover:opacity-100"
                onClick={() => dismiss(toast.id)}
                aria-label="Dismiss notification"
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </button>
            </div>
          );
        })}
      </div>
    </ToastContext.Provider>
  );
}

/** Gives a screen the success / error / info toast helpers. */
export function useToast(): ToastApi {
  // Fails loudly if a screen is rendered outside the provider.
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error('useToast must be used inside a ToastProvider.');
  }
  return context;
}

/* -------------------------------------------------------------------------- */
/* Pagination                                                                  */
/* -------------------------------------------------------------------------- */

/** Previous / next control for the API's paged list endpoints. */
export function Pagination({
  page,
  totalPages,
  totalCount,
  onChange,
}: {
  page: number;
  totalPages: number;
  totalCount: number;
  onChange: (page: number) => void;
}) {
  // The API decides how many pages there are; this only moves between them.
  if (totalPages <= 1) return null;

  return (
    <div className="flex flex-col items-center justify-between gap-3 border-t border-slate-200 px-5 py-3 sm:flex-row">
      <p className="text-xs font-medium text-slate-500">
        Showing page {page} of {totalPages} ({totalCount} records)
      </p>
      <div className="flex items-center gap-2">
        <button
          type="button"
          className="btn-secondary btn-sm"
          onClick={() => onChange(page - 1)}
          disabled={page <= 1}
        >
          <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
          Previous
        </button>
        <button
          type="button"
          className="btn-secondary btn-sm"
          onClick={() => onChange(page + 1)}
          disabled={page >= totalPages}
        >
          Next
          <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
        </button>
      </div>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Search input                                                                */
/* -------------------------------------------------------------------------- */

/** Text box with a leading search icon and a clear button. */
export function SearchInput({
  value,
  onChange,
  placeholder = 'Search...',
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  // Filtering itself happens on the server, so this only reports what was typed.
  return (
    <div className="relative">
      <Search
        className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-500"
        aria-hidden="true"
      />
      <input
        type="search"
        className="input pl-9 pr-9"
        value={value}
        placeholder={placeholder}
        onChange={(event) => onChange(event.target.value)}
      />
      {value.length > 0 && (
        <button
          type="button"
          className="absolute right-2.5 top-1/2 -translate-y-1/2 rounded p-1 text-slate-500 transition-colors hover:text-slate-900"
          onClick={() => onChange('')}
          aria-label="Clear search"
        >
          <X className="h-4 w-4" aria-hidden="true" />
        </button>
      )}
    </div>
  );
}
