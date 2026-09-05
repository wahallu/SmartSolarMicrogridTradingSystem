/**
 * =============================================================================
 * File        : context/AuthContext.tsx
 * Project     : Smart Solar Microgrid Trading System - Web Application
 * Description : Holds the signed in user for the whole application and exposes
 *               login / logout to every screen. The token and the profile are
 *               kept in local storage so a page refresh does not sign the user
 *               out; on start-up the token is re-validated against GET /auth/me
 *               so a session revoked on the server does not linger in the UI.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * =============================================================================
 */

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { authApi } from '../api/services';
import { TOKEN_KEY, USER_KEY } from '../api/client';
import type { User, UserRole } from '../types';

/** Everything the rest of the app may do with the session. */
interface AuthContextValue {
  user: User | null;
  token: string | null;
  /** True while the stored token is being re-validated at start-up. */
  initialising: boolean;
  login: (identifier: string, password: string) => Promise<User>;
  logout: () => void;
  refresh: () => Promise<void>;
  hasRole: (...roles: UserRole[]) => boolean;
  isBackoffice: boolean;
  isGridOperator: boolean;
  isProsumer: boolean;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

/** Reads the cached user from local storage, tolerating corrupt JSON. */
function readStoredUser(): User | null {
  // A malformed entry must not stop the application from starting.
  try {
    const raw = localStorage.getItem(USER_KEY);
    return raw ? (JSON.parse(raw) as User) : null;
  } catch {
    return null;
  }
}

/** Provides the session to the component tree. */
export function AuthProvider({ children }: { children: ReactNode }) {
  // The cached values render immediately; the effect below then confirms them.
  const [user, setUser] = useState<User | null>(readStoredUser);
  const [token, setToken] = useState<string | null>(() => localStorage.getItem(TOKEN_KEY));
  const [initialising, setInitialising] = useState<boolean>(true);

  // Clears the session from both state and storage.
  const logout = useCallback(() => {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    setToken(null);
    setUser(null);
  }, []);

  // Signs in, stores the token and profile, and returns the user so the caller
  // can route to the correct home screen for the role.
  const login = useCallback(async (identifier: string, password: string): Promise<User> => {
    const auth = await authApi.login(identifier, password);
    localStorage.setItem(TOKEN_KEY, auth.token);
    localStorage.setItem(USER_KEY, JSON.stringify(auth.user));
    setToken(auth.token);
    setUser(auth.user);
    return auth.user;
  }, []);

  // Re-reads the profile from the server, used after the user edits themselves.
  const refresh = useCallback(async () => {
    // A failure here is not fatal - the cached profile stays on screen.
    try {
      const fresh = await authApi.me();
      localStorage.setItem(USER_KEY, JSON.stringify(fresh));
      setUser(fresh);
    } catch {
      // Left deliberately silent; the interceptor already surfaced the problem.
    }
  }, []);

  // On first mount, confirm a stored token is still valid before trusting it.
  useEffect(() => {
    let cancelled = false;

    async function validate() {
      // With no token there is nothing to validate and the app renders the login page.
      if (!localStorage.getItem(TOKEN_KEY)) {
        setInitialising(false);
        return;
      }

      try {
        const fresh = await authApi.me();
        if (!cancelled) {
          localStorage.setItem(USER_KEY, JSON.stringify(fresh));
          setUser(fresh);
        }
      } catch {
        // An expired or revoked token is discarded rather than left to fail later.
        if (!cancelled) {
          logout();
        }
      } finally {
        if (!cancelled) {
          setInitialising(false);
        }
      }
    }

    void validate();
    return () => {
      cancelled = true;
    };
  }, [logout]);

  // True when the signed in user holds any of the supplied roles.
  const hasRole = useCallback(
    (...roles: UserRole[]) => (user ? roles.includes(user.role) : false),
    [user],
  );

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      token,
      initialising,
      login,
      logout,
      refresh,
      hasRole,
      isBackoffice: user?.role === 'Backoffice',
      isGridOperator: user?.role === 'GridOperator',
      isProsumer: user?.role === 'Prosumer',
    }),
    [user, token, initialising, login, logout, refresh, hasRole],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

/** Reads the session; throws if used outside the provider. */
export function useAuth(): AuthContextValue {
  // The throw turns a wiring mistake into an immediate, obvious error.
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used inside an AuthProvider.');
  }
  return context;
}
