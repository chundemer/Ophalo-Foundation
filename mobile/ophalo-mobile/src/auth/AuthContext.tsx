import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { api } from '../api/client';
import { clearSessionToken, getAppInstallationId, getSessionToken, setSessionToken } from './secureStore';

type MeResponse = {
  accountUserId: string;
  accountId: string;
  isAuthenticated: boolean;
  isVerified: boolean;
  accountRole: string;
};

type AuthState = {
  user: MeResponse | null;
  isLoading: boolean;
  storeToken: (token: string) => Promise<void>;
  logout: () => Promise<void>;
};

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<MeResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const bootstrap = useCallback(async () => {
    await getAppInstallationId().catch(() => {}); // ensure durable ID exists before any API call
    try {
      const token = await getSessionToken();
      if (!token) return;
      const me = await api.get<MeResponse>('/auth/me');
      setUser(me);
    } catch {
      await clearSessionToken().catch(() => {});
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void bootstrap();
  }, [bootstrap]);

  async function storeToken(token: string): Promise<void> {
    const me = await api.get<MeResponse>('/auth/me', token);
    await setSessionToken(token);
    setUser(me);
  }

  // logout is guaranteed to clear local state — it never throws.
  // API cleanup (device revocation, session revocation) is best-effort.
  async function logout(): Promise<void> {
    try {
      const installId = await getAppInstallationId().catch(() => null);
      if (installId) {
        await api.delete(`/me/devices/${installId}`).catch(() => {});
      }
      await api.post('/auth/logout', undefined, true).catch(() => {});
      await clearSessionToken().catch(() => {});
    } finally {
      setUser(null);
    }
  }

  return (
    <AuthContext.Provider value={{ user, isLoading, storeToken, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
