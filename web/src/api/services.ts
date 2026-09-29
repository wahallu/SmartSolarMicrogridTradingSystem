/**
 * =============================================================================
 * File        : api/services.ts
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : One typed function per Web API endpoint, grouped by resource.
 *               Screens import from here and never build a URL themselves, so
 *               the whole client surface can be reviewed in a single file and a
 *               route change is a one line edit.
 *
 *               Nothing in this file makes a decision - it only forwards. Every
 *               rule (the 7 day window, the 12 hour notice, role checks, QR
 *               validity) is enforced by the C# service layer.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

import { http, unwrap, unwrapWithMessage, cleanParams } from './client';
import type {
  ApiResponse,
  AuthResponse,
  Dashboard,
  GenerateSlotsResult,
  HealthStatus,
  NearbyStation,
  PagedResult,
  QrCode,
  Reservation,
  Slot,
  Station,
  User,
} from '../types';

/* ===========================================================================
 * Auth
 * =========================================================================== */

export const authApi = {
  /** Signs in with a NIC, username or e-mail address. */
  login: (identifier: string, password: string) =>
    unwrap<AuthResponse>(
      http.post<ApiResponse<AuthResponse>>('/auth/login', { identifier, password }),
    ),

  /** Registers a new solar prosumer; the account starts Pending (rule R6). */
  register: (payload: {
    nic: string;
    fullName: string;
    email: string;
    phone: string;
    address: string;
    password: string;
  }) => unwrapWithMessage<User>(http.post<ApiResponse<User>>('/auth/register', payload)),

  /** Returns the profile of the signed in caller. */
  me: () => unwrap<User>(http.get<ApiResponse<User>>('/auth/me')),

  /** Changes the caller's own password. */
  changePassword: (currentPassword: string, newPassword: string) =>
    unwrapWithMessage<boolean>(
      http.post<ApiResponse<boolean>>('/auth/change-password', { currentPassword, newPassword }),
    ),
};

/* ===========================================================================
 * Web users (Backoffice only - rule R10)
 * =========================================================================== */

export const usersApi = {
  /** Paged list of Backoffice and Grid Operator accounts. */
  list: (params: {
    role?: string;
    status?: string;
    search?: string;
    page?: number;
    pageSize?: number;
  }) =>
    unwrap<PagedResult<User>>(
      http.get<ApiResponse<PagedResult<User>>>('/users', { params: cleanParams(params) }),
    ),

  /** Single account by identifier. */
  get: (id: string) => unwrap<User>(http.get<ApiResponse<User>>(`/users/${id}`)),

  /** Creates a Backoffice or Grid Operator account. */
  create: (payload: Record<string, unknown>) =>
    unwrapWithMessage<User>(http.post<ApiResponse<User>>('/users', payload)),

  /** Edits an account. */
  update: (id: string, payload: Record<string, unknown>) =>
    unwrapWithMessage<User>(http.put<ApiResponse<User>>(`/users/${id}`, payload)),

  /** Returns an account to Active. */
  activate: (id: string) =>
    unwrapWithMessage<User>(http.patch<ApiResponse<User>>(`/users/${id}/activate`)),

  /** Blocks an account from signing in. */
  deactivate: (id: string, reason?: string) =>
    unwrapWithMessage<User>(http.patch<ApiResponse<User>>(`/users/${id}/deactivate`, { reason })),

  /** Sets a new password on a user's behalf. */
  resetPassword: (id: string, newPassword: string) =>
    unwrapWithMessage<boolean>(
      http.post<ApiResponse<boolean>>(`/users/${id}/reset-password`, { newPassword }),
    ),

  /** Permanently removes an account. */
  remove: (id: string) =>
    unwrapWithMessage<boolean>(http.delete<ApiResponse<boolean>>(`/users/${id}`)),
};

/* ===========================================================================
 * Solar prosumers 
 * =========================================================================== */

export const prosumersApi = {
  /** Paged list of prosumers. */
  list: (params: { status?: string; search?: string; page?: number; pageSize?: number }) =>
    unwrap<PagedResult<User>>(
      http.get<ApiResponse<PagedResult<User>>>('/prosumers', { params: cleanParams(params) }),
    ),

  /** The back office pending-activation work queue. */
  pending: () => unwrap<User[]>(http.get<ApiResponse<User[]>>('/prosumers/pending')),

  /** Single prosumer by NIC. */
  get: (nic: string) => unwrap<User>(http.get<ApiResponse<User>>(`/prosumers/${nic}`)),

  /** Creates a prosumer from the back office. */
  create: (payload: Record<string, unknown>) =>
    unwrapWithMessage<User>(http.post<ApiResponse<User>>('/prosumers', payload)),

  /** Edits a prosumer profile. */
  update: (nic: string, payload: Record<string, unknown>) =>
    unwrapWithMessage<User>(http.put<ApiResponse<User>>(`/prosumers/${nic}`, payload)),

  /** Reactivates a prosumer - Backoffice only (rule R5). */
  activate: (nic: string) =>
    unwrapWithMessage<User>(http.patch<ApiResponse<User>>(`/prosumers/${nic}/activate`)),

  /** Deactivates a prosumer account. */
  deactivate: (nic: string, reason?: string) =>
    unwrapWithMessage<User>(
      http.patch<ApiResponse<User>>(`/prosumers/${nic}/deactivate`, { reason }),
    ),

  /** Removes a prosumer with no booking history. */
  remove: (nic: string) =>
    unwrapWithMessage<boolean>(http.delete<ApiResponse<boolean>>(`/prosumers/${nic}`)),
};

/* ===========================================================================
 * Microgrid nodes
 * =========================================================================== */

export const stationsApi = {
  /** Paged list of nodes. */
  list: (params: {
    status?: string;
    search?: string;
    city?: string;
    page?: number;
    pageSize?: number;
  }) =>
    unwrap<PagedResult<Station>>(
      http.get<ApiResponse<PagedResult<Station>>>('/stations', { params: cleanParams(params) }),
    ),

  /** Single node by identifier. */
  get: (id: string) => unwrap<Station>(http.get<ApiResponse<Station>>(`/stations/${id}`)),

  /** Active nodes within a radius, ordered by server computed distance. */
  nearby: (lat: number, lng: number, radiusKm?: number, limit?: number) =>
    unwrap<NearbyStation[]>(
      http.get<ApiResponse<NearbyStation[]>>('/stations/nearby', {
        params: cleanParams({ lat, lng, radiusKm, limit }),
      }),
    ),

  /** Registers with a new node. */
  create: (payload: Record<string, unknown>) =>
    unwrapWithMessage<Station>(http.post<ApiResponse<Station>>('/stations', payload)),

  /** Edits a node. */
  update: (id: string, payload: Record<string, unknown>) =>
    unwrapWithMessage<Station>(http.put<ApiResponse<Station>>(`/stations/${id}`, payload)),

  /** Replaces the seven day operating schedule. */
  updateSchedule: (id: string, payload: Record<string, unknown>) =>
    unwrapWithMessage<Station>(http.patch<ApiResponse<Station>>(`/stations/${id}/schedule`, payload)),

  /** Updates the free battery bay count. */
  updateAvailability: (id: string, availableBatterySlots: number) =>
    unwrapWithMessage<Station>(
      http.patch<ApiResponse<Station>>(`/stations/${id}/availability`, { availableBatterySlots }),
    ),

  /** Returns a node to service. */
  activate: (id: string) =>
    unwrapWithMessage<Station>(http.patch<ApiResponse<Station>>(`/stations/${id}/activate`)),

  /** Takes a node out of service - refused while future bookings exist (rule R4). */
  deactivate: (id: string) =>
    unwrapWithMessage<Station>(http.patch<ApiResponse<Station>>(`/stations/${id}/deactivate`)),

  /** Deletes a node that has no booking history. */
  remove: (id: string) =>
    unwrapWithMessage<boolean>(http.delete<ApiResponse<boolean>>(`/stations/${id}`)),
};

/* ===========================================================================
 * 
 * =========================================================================== */

export const slotsApi = {
  /** Paged list of slots. */
  list: (params: {
    stationId?: string;
    date?: string;
    status?: string;
    slotType?: string;
    upcomingOnly?: boolean;
    page?: number;
    pageSize?: number;
  }) =>
    unwrap<PagedResult<Slot>>(
      http.get<ApiResponse<PagedResult<Slot>>>('/slots', { params: cleanParams(params) }),
    ),

  /** Slots that would currently accept a booking (rule R8). */
  available: (stationId: string, date?: string) =>
    unwrap<Slot[]>(
      http.get<ApiResponse<Slot[]>>('/slots/available', {
        params: cleanParams({ stationId, date }),
      }),
    ),

  /** Single slot by identifier. */
  get: (id: string) => unwrap<Slot>(http.get<ApiResponse<Slot>>(`/slots/${id}`)),

  /** Publishes one slot. */
  create: (payload: Record<string, unknown>) =>
    unwrapWithMessage<Slot>(http.post<ApiResponse<Slot>>('/slots', payload)),

  /** Publishes a whole date range of slots in one call. */
  generate: (payload: Record<string, unknown>) =>
    unwrapWithMessage<GenerateSlotsResult>(
      http.post<ApiResponse<GenerateSlotsResult>>('/slots/generate', payload),
    ),

  /** Edits a slot. */
  update: (id: string, payload: Record<string, unknown>) =>
    unwrapWithMessage<Slot>(http.put<ApiResponse<Slot>>(`/slots/${id}`, payload)),

  /** Stops new bookings on a slot without cancelling existing ones. */
  block: (id: string) => unwrapWithMessage<Slot>(http.patch<ApiResponse<Slot>>(`/slots/${id}/block`)),

  /** Deletes a slot that carries no bookings. */
  remove: (id: string) =>
    unwrapWithMessage<boolean>(http.delete<ApiResponse<boolean>>(`/slots/${id}`)),
};

/* ===========================================================================
 * Reservations
 * =========================================================================== */

export const reservationsApi = {
  /** Paged, filtered list for the staff search screens. */
  list: (params: {
    nic?: string;
    stationId?: string;
    status?: string;
    fromDate?: string;
    toDate?: string;
    search?: string;
    page?: number;
    pageSize?: number;
  }) =>
    unwrap<PagedResult<Reservation>>(
      http.get<ApiResponse<PagedResult<Reservation>>>('/reservations', {
        params: cleanParams(params),
      }),
    ),

  /** Single reservation by identifier. */
  get: (id: string) => unwrap<Reservation>(http.get<ApiResponse<Reservation>>(`/reservations/${id}`)),

  /** Full booking history for a NIC. */
  history: (nic?: string) =>
    unwrap<Reservation[]>(
      http.get<ApiResponse<Reservation[]>>('/reservations/history', { params: cleanParams({ nic }) }),
    ),

  /** Live dashboard counters, optionally scoped to a node or a prosumer. */
  dashboard: (stationId?: string, nic?: string) =>
    unwrap<Dashboard>(
      http.get<ApiResponse<Dashboard>>('/reservations/dashboard', {
        params: cleanParams({ stationId, nic }),
      }),
    ),

  /** Creates a booking (rules R1, R8, R12, R13). */
  create: (payload: {
    prosumerNic?: string;
    stationId: string;
    slotId: string;
    energyKwh: number;
    notes?: string;
  }) => unwrapWithMessage<Reservation>(http.post<ApiResponse<Reservation>>('/reservations', payload)),

  /** Changes a booking - enforced against the 12 hour notice rule R2. */
  update: (id: string, payload: { slotId?: string; energyKwh?: number }) =>
    unwrapWithMessage<Reservation>(
      http.put<ApiResponse<Reservation>>(`/reservations/${id}`, payload),
    ),

  /** Cancels a booking - enforced against the 12 hour notice rule R3. */
  cancel: (id: string, reason?: string) =>
    unwrapWithMessage<Reservation>(
      http.patch<ApiResponse<Reservation>>(`/reservations/${id}/cancel`, { reason }),
    ),

  /** Approves a booking and issues its QR token. */
  approve: (id: string) =>
    unwrapWithMessage<Reservation>(
      http.patch<ApiResponse<Reservation>>(`/reservations/${id}/approve`),
    ),

  /** Refuses a booking with a reason. */
  reject: (id: string, reason: string) =>
    unwrapWithMessage<Reservation>(
      http.patch<ApiResponse<Reservation>>(`/reservations/${id}/reject`, { reason }),
    ),

  /** Fetches the QR payload and rendered PNG for an approved booking. */
  qr: (id: string) => unwrap<QrCode>(http.get<ApiResponse<QrCode>>(`/reservations/${id}/qr`)),

  /** Validates a scanned token against the server (rule R9). */
  verifyQr: (qrToken: string) =>
    unwrapWithMessage<Reservation>(
      http.post<ApiResponse<Reservation>>('/reservations/verify-qr', { qrToken }),
    ),

  /** Finalises the energy transfer after a successful scan. */
  complete: (qrToken: string, deliveredKwh: number, notes?: string) =>
    unwrapWithMessage<Reservation>(
      http.post<ApiResponse<Reservation>>('/reservations/complete', {
        qrToken,
        deliveredKwh,
        notes,
      }),
    ),
};

/* ===========================================================================
 * Health
 * =========================================================================== */

export const healthApi = {
  /** Confirms the API is up and MongoDB is reachable. */
  check: () => unwrap<HealthStatus>(http.get<ApiResponse<HealthStatus>>('/health')),
};
