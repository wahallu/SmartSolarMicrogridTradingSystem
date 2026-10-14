/* =============================================================================
 * File        : src/utils/format.ts
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Utils / format
 * Description : Formats dates, energy values and labels for display.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-25
 * =============================================================================
 */

import type { AccountStatus, ReservationStatus, SlotType, StationStatus } from '../types';

/** Formats an ISO instant as "04 Sep 2026". */
export function formatDate(iso: string | null | undefined): string {
  // Every timestamp the API sends is UTC, so it is rendered as UTC for consistency
  // with the slot times an operator reads at the node itself.
  if (!iso) return '-';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '-';
  return date.toLocaleDateString('en-GB', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  });
}

/** Formats an ISO instant as "04 Sep 2026, 08:00". */
export function formatDateTime(iso: string | null | undefined): string {
  // Used wherever the exact slot start matters.
  if (!iso) return '-';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '-';
  return `${date.toLocaleDateString('en-GB', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  })}, ${date.toLocaleTimeString('en-GB', {
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'UTC',
  })}`;
}

/** Formats an ISO instant as "Fri, 04 Sep". */
export function formatDayDate(iso: string | null | undefined): string {
  // Used for grouping headers in the booking lists.
  if (!iso) return '-';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '-';
  return date.toLocaleDateString('en-GB', {
    weekday: 'short',
    day: '2-digit',
    month: 'short',
    timeZone: 'UTC',
  });
}

/** Converts a Date into the "yyyy-MM-dd" string the API's date filters expect. */
export function toApiDate(date: Date): string {
  // Slot queries take a plain calendar day rather than a full instant.
  return date.toISOString().slice(0, 10);
}

/** Today as "yyyy-MM-dd". */
export function today(): string {
  // Date pickers default to today.
  return toApiDate(new Date());
}

/** A date this many days from today as "yyyy-MM-dd". */
export function daysFromToday(days: number): string {
  // Used to cap a date picker at the 7 day booking window the API enforces.
  const date = new Date();
  date.setUTCDate(date.getUTCDate() + days);
  return toApiDate(date);
}

/** Turns the server's hoursUntilStart figure into "in 3 days" style text. */
export function describeHoursUntil(hours: number): string {
  // Purely cosmetic; the buttons are driven by canModify / canCancel.
  if (hours < 0) {
    const past = Math.abs(hours);
    if (past < 1) return 'started just now';
    if (past < 24) return `started ${Math.floor(past)} hour${Math.floor(past) === 1 ? '' : 's'} ago`;
    const days = Math.floor(past / 24);
    return `started ${days} day${days === 1 ? '' : 's'} ago`;
  }
  if (hours < 1) {
    const minutes = Math.max(1, Math.round(hours * 60));
    return `in ${minutes} minute${minutes === 1 ? '' : 's'}`;
  }
  if (hours < 24) {
    const whole = Math.floor(hours);
    return `in ${whole} hour${whole === 1 ? '' : 's'}`;
  }
  const days = Math.floor(hours / 24);
  return `in ${days} day${days === 1 ? '' : 's'}`;
}

/** Tailwind class for a reservation status pill. */
export function reservationBadgeClass(status: ReservationStatus | string): string {
  // Keeps status colouring identical on every screen.
  switch (status) {
    case 'Pending':
      return 'badge-pending';
    case 'Approved':
      return 'badge-approved';
    case 'Completed':
      return 'badge-completed';
    case 'Cancelled':
      return 'badge-cancelled';
    case 'Rejected':
      return 'badge-rejected';
    default:
      return 'badge-cancelled';
  }
}

/** Tailwind class for an account status pill. */
export function accountBadgeClass(status: AccountStatus | string): string {
  // Pending is deliberately amber so the back office queue stands out.
  switch (status) {
    case 'Active':
      return 'badge-active';
    case 'Pending':
      return 'badge-pending';
    case 'Deactivated':
      return 'badge-rejected';
    default:
      return 'badge-inactive';
  }
}

/** Tailwind class for a node status pill. */
export function stationBadgeClass(status: StationStatus | string): string {
  // Only two states, so a simple ternary is enough.
  return status === 'Active' ? 'badge-active' : 'badge-inactive';
}

/** Expands a slot type into user facing wording. */
export function slotTypeLabel(type: SlotType | string): string {
  // "DropOff" is not a phrase to show a user.
  return type === 'DropOff' ? 'Energy drop-off' : 'Battery charging';
}

/** Formats a kWh figure with one decimal place. */
export function kwh(value: number | null | undefined): string {
  // A null delivered figure renders as a dash rather than "0.0 kWh".
  if (value === null || value === undefined) return '-';
  return `${value.toFixed(1)} kWh`;
}

/** Turns the API's field error map into a flat list of messages. */
export function flattenFieldErrors(errors: Record<string, string[]> | null): string[] {
  // Used by the forms to show a summary above the fields.
  if (!errors) return [];
  return Object.values(errors).flat();
}
