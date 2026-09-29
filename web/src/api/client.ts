/**
 * =============================================================================
 * File        : api/client.ts
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : The single axios instance every screen uses to reach the central
 *               C# Web API. The web application is a pure UI layer: it holds no
 *               business logic and never touches MongoDB - every read and every
 *               write goes through one of the calls defined here.
 *
 *               Two interceptors do the repetitive work:
 *                 request  - attaches the bearer token from local storage
 *                 response - unwraps the {success, message, data} envelope and
 *                            converts a failure into a typed ApiError
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

import axios, { AxiosError, type AxiosInstance } from 'axios';
import type { ApiResponse } from '../types';

/** Where the token is kept between page loads. */
export const TOKEN_KEY = 'smartsolar.token';
export const USER_KEY = 'smartsolar.user';

/** Base URL of the Web API; overridable per environment through Vite. */
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5218/api/v1';

/**
 * A failure returned by the API, carrying the server's stable error code so
 * screens can branch on the rule that fired rather than on message text.
 */
export class ApiError extends Error {
  public readonly status: number;
  public readonly errorCode: string | null;
  public readonly fieldErrors: Record<string, string[]> | null;

  // Builds a typed error from the pieces the envelope supplied.
  constructor(
    message: string,
    status: number,
    errorCode: string | null = null,
    fieldErrors: Record<string, string[]> | null = null,
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.errorCode = errorCode;
    this.fieldErrors = fieldErrors;
  }

  /** True when the request never reached the server. */
  get isNetworkError(): boolean {
    // Status 0 is reserved for transport failures.
    return this.status === 0;
  }

  /** True when the session token is missing or expired. */
  get isUnauthorized(): boolean {
    // The auth context signs the user out when it sees this.
    return this.status === 401;
  }

  /** True when the caller's role does not permit the operation. */
  get isForbidden(): boolean {
    // Distinct from unauthorized: signing in again would not help.
    return this.status === 403;
  }
}

/** The shared axios instance. */
export const http: AxiosInstance = axios.create({
  baseURL: BASE_URL,
  timeout: 20000,
  headers: { 'Content-Type': 'application/json' },
});

// Attaches the bearer token to every outgoing request.
http.interceptors.request.use((config) => {
  
  const token = localStorage.getItem(TOKEN_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Converts every failure into an ApiError so callers have one thing to catch.
http.interceptors.response.use(
  (response) => response,
  (error: AxiosError<ApiResponse<unknown>>) => {
    // No response at all means DNS, CORS, a timeout or a stopped API.
    if (!error.response) {
      return Promise.reject(
        new ApiError(
          'Could not reach the Smart Solar service. Please confirm the API is running.',
          0,
          'NETWORK_ERROR',
        ),
      );
    }

    const { status, data } = error.response;
    return Promise.reject(
      new ApiError(
        data?.message ?? `The request failed with HTTP ${status}.`,
        status,
        data?.errorCode ?? null,
        data?.errors ?? null,
      ),
    );
  },
);

/**
 * Unwraps the envelope and returns the payload. A response whose success flag is
 * false is turned into an ApiError, so a caller that awaits this function only
 * ever receives real data.
 */
export async function unwrap<T>(promise: Promise<{ data: ApiResponse<T> }>): Promise<T> {
  // The envelope is uniform, so one unwrapper serves every endpoint.
  const response = await promise;
  const body = response.data;

  if (!body.success) {
    throw new ApiError(body.message, 200, body.errorCode, body.errors);
  }

  return body.data as T;
}

/**
 * Same as unwrap but also hands back the server's message, which several screens
 * display verbatim in a toast (for example the wording of a rule violation).
 */
export async function unwrapWithMessage<T>(
  promise: Promise<{ data: ApiResponse<T> }>,
): Promise<{ data: T; message: string }> {
  // Used wherever the API's own wording is better than anything the UI could invent.
  const response = await promise;
  const body = response.data;

  if (!body.success) {
    throw new ApiError(body.message, 200, body.errorCode, body.errors);
  }

  return { data: body.data as T, message: body.message };
}

/** Drops empty, null and undefined values so optional filters can be passed freely. */
export function cleanParams(params: Record<string, unknown>): Record<string, unknown> {
  // Without this every unset filter would be sent as "?status=" and confuse the API.
  return Object.fromEntries(
    Object.entries(params).filter(
      ([, value]) => value !== undefined && value !== null && value !== '',
    ),
  );
}
