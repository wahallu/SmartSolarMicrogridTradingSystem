/* =============================================================================
 * File        : src/types/index.ts
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Module      : Types / index
 * Description : Defines the TypeScript models shared by API calls and screens.
 * Author      : IT22246332 - Sandun
 * Created     : 2026-09-21
 * =============================================================================
 */

/** The uniform envelope every endpoint returns, success or failure. */
export interface ApiResponse<T> {
  success: boolean;
  message: string;
  data: T | null;
  errorCode: string | null;
  errors: Record<string, string[]> | null;
  timestamp: string;
}

/** One page of results plus its position in the whole set. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}


export type UserRole = 'Backoffice' | 'GridOperator' | 'Prosumer';

/** Life cycle state of any account. */
export type AccountStatus = 'Pending' | 'Active' | 'Deactivated';

/** Whether a microgrid node is in service. */
export type StationStatus = 'Active' | 'Inactive';

/** Physical classification of a node. */
export type StationType = 'Solar' | 'Hybrid' | 'Battery';

/** Availability state of a booking slot. */
export type SlotStatus = 'Available' | 'Full' | 'Blocked';

/** Direction of the energy transfer booked for a slot. */
export type SlotType = 'Charging' | 'DropOff';

/** Life cycle of an energy reservation. */
export type ReservationStatus = 'Pending' | 'Approved' | 'Completed' | 'Cancelled' | 'Rejected';

/** An account - Backoffice officer, Grid Operator or Solar Prosumer. */
export interface User {
  id: string;
  nic: string | null;
  username: string;
  email: string;
  fullName: string;
  phone: string;
  address: string;
  role: UserRole;
  status: AccountStatus;
  deactivationRequested: boolean;
  deactivationRequestedAt: string | null;
  deactivationReason: string | null;
  assignedStationId: string | null;
  assignedStationCode: string | null;
  lastLoginAt: string | null;
  createdAt: string;
  updatedAt: string;
}

/** Result of a successful login. */
export interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  user: User;
}

/** Opening and closing wall clock times of a node. */
export interface OperatingHours {
  openTime: string;
  closeTime: string;
}

/** One day of a node's published operating schedule. */
export interface DaySchedule {
  dayOfWeek: number;
  dayName: string;
  isOpen: boolean;
  openTime: string;
  closeTime: string;
}

/** A microgrid node (solar grid hub). */
export interface Station {
  id: string;
  stationCode: string;
  name: string;
  type: StationType;
  address: string;
  city: string;
  latitude: number;
  longitude: number;
  capacityKwh: number;
  totalBatterySlots: number;
  availableBatterySlots: number;
  operatingHours: OperatingHours;
  weeklySchedule: DaySchedule[];
  contactNumber: string;
  status: StationStatus;
  /** Future Pending/Approved bookings - drives the rule R4 deactivation guard. */
  activeReservationCount: number;
  createdAt: string;
  updatedAt: string;
}

/** A node together with the server computed distance from the caller. */
export interface NearbyStation extends Station {
  distanceKm: number;
}

/** A bookable energy window at a node. */
export interface Slot {
  id: string;
  stationId: string;
  stationCode: string;
  stationName: string;
  slotCode: string;
  slotDate: string;
  startTime: string;
  endTime: string;
  startUtc: string;
  endUtc: string;
  slotType: SlotType;
  energyCapacityKwh: number;
  batterySlotNumber: number;
  maxBookings: number;
  bookedCount: number;
  remainingCapacity: number;
  status: SlotStatus;
  /** Server verdict on rule R8 -  */
  isBookable: boolean;
}

/** A power trading reservation. */
export interface Reservation {
  id: string;
  reservationNo: string;
  prosumerNic: string;
  prosumerName: string;
  prosumerPhone: string;
  stationId: string;
  stationCode: string;
  stationName: string;
  slotId: string;
  slotCode: string;
  reservationStartUtc: string;
  reservationEndUtc: string;
  startTime: string;
  endTime: string;
  slotType: SlotType;
  energyKwh: number;
  deliveredKwh: number | null;
  status: ReservationStatus;
  qrToken: string | null;
  qrIssuedAt: string | null;
  approvedBy: string | null;
  approvedAt: string | null;
  completedBy: string | null;
  completedAt: string | null;
  cancelledBy: string | null;
  cancelledAt: string | null;
  cancelReason: string | null;
  rejectedReason: string | null;
  operatorNotes: string | null;
  /**
   * Server verdict on rule R2 (12 hour notice). The UI only enables or disables
   * the Edit button from this - it never evaluates the rule itself.
   */
  canModify: boolean;
  /** Server verdict on rule R3 (12 hour notice). */
  canCancel: boolean;
  hoursUntilStart: number;
  createdAt: string;
  updatedAt: string;
}

/** QR material for an approved booking. */
export interface QrCode {
  reservationNo: string;
  qrToken: string;
  qrPayload: string;
  /** Base64 PNG with no data URI prefix. */
  qrImageBase64: string;
  issuedAt: string | null;
}

/**
 * Live counters shown on the dashboards.
 *
 * These are the real-time figures surfaced on the dashboard screens, kept
 * up to date as the underlying data changes so the user always sees the
 * current state at a glance.
 */
export interface Dashboard {
  pendingReservations: number;
  /** The "count of approved future reservations" the marking scheme names. */
  approvedFutureReservations: number;
  todayReservations: number;
  completedReservations: number;
  cancelledReservations: number;
  totalReservations: number;
  activeStations: number;
  totalStations: number;
  pendingProsumerActivations: number;
  totalProsumers: number;
  totalEnergyKwh: number;
  upcoming: Reservation[];
}

/** Outcome of a bulk slot generation. */
export interface GenerateSlotsResult {
  created: number;
  skipped: number;
  notes: string[];
}

/** Health payload returned by GET /health. */
export interface HealthStatus {
  status: string;
  database: string;
  databaseName: string;
  utcNow: string;
  version: string;
  environment: string;
}
