import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { api, setApiToken, setUnauthorizedHandler, type Role, type Session } from './api';

const STORAGE_KEY = 'ledger-desk.session';

function loadSession(): Session | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const session = JSON.parse(raw) as Session;
    return new Date(session.expiresAt).getTime() > Date.now() ? session : null;
  } catch {
    return null;
  }
}

interface AuthState {
  session: Session | null;
  role: Role | null;
  canPost: boolean;   // Admin and Teller can move money; Auditor is read-only
  isAdmin: boolean;
  login: (username: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(loadSession);

  // Set synchronously (not in an effect): child effects run before parent effects and may fetch immediately.
  setApiToken(session?.accessToken ?? null);

  const logout = useCallback(() => {
    sessionStorage.removeItem(STORAGE_KEY);
    setSession(null);
  }, []);
  setUnauthorizedHandler(logout);

  const login = useCallback(async (username: string, password: string) => {
    const result = await api.login(username, password);
    const next: Session = { ...result, username };
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    setApiToken(next.accessToken);
    setSession(next);
  }, []);

  const value = useMemo<AuthState>(() => ({
    session,
    role: session?.role ?? null,
    canPost: session?.role === 'Admin' || session?.role === 'Teller',
    isAdmin: session?.role === 'Admin',
    login,
    logout
  }), [session, login, logout]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>');
  return ctx;
}
