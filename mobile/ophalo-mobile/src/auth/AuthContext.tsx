import { useQueryClient } from '@tanstack/react-query';
import Constants from 'expo-constants';
import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { Platform } from 'react-native';
import { api, ApiError, setOn401Handler } from '../api/client';
import { clearSessionToken, getAppInstallationId, getSessionToken, setSessionToken } from './secureStore';

async function upsertDevice(): Promise<void> {
  const installId = await getAppInstallationId().catch(() => null);
  if (!installId) return;
  await api.put(`/me/devices/${installId}`, {
    platform: Platform.OS,
    appVersion: Constants.expoConfig?.version ?? '1.0.0',
    pushToken: null,
  }).catch(() => {});
}

type MeResponse = {
  accountUserId: string;
  accountId: string;
  isAuthenticated: boolean;
  isVerified: boolean;
  accountRole: string;
};

const MOBILE_ALLOWED_ROLES = ['owner', 'admin', 'operator'];

type AuthState = {
  user: MeResponse | null;
  isLoading: boolean;
  isRoleBlocked: boolean;
  storeToken: (token: string) => Promise<void>;
  logout: () => Promise<void>;
  clearRoleBlocked: () => void;
};

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const queryClient = useQueryClient();
  const [user, setUser] = useState<MeResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isRoleBlocked, setIsRoleBlocked] = useState(false);

  useEffect(() => {
    setOn401Handler(() => {
      void clearSessionToken().catch(() => {});
      setUser(null);
      queryClient.clear();
    });
    return () => { setOn401Handler(null); };
  }, [queryClient]);

  const bootstrap = useCallback(async () => {
    await getAppInstallationId().catch(() => {}); // ensure durable ID exists before any API call
    try {
      const token = await getSessionToken();
      if (!token) return;
      const me = await api.get<MeResponse>('/auth/me');
      if (!MOBILE_ALLOWED_ROLES.includes(me.accountRole)) {
        await clearSessionToken().catch(() => {});
        setIsRoleBlocked(true);
        return;
      }
      setIsRoleBlocked(false);
      setUser(me);
      void upsertDevice(); // refresh device record on app launch; best-effort
    } catch (err) {
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        await clearSessionToken().catch(() => {});
      }
      setUser(null);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void bootstrap();
  }, [bootstrap]);

  async function storeToken(token: string): Promise<void> {
    const me = await api.get<MeResponse>('/auth/me', token);
    if (!MOBILE_ALLOWED_ROLES.includes(me.accountRole)) {
      throw new Error('mobile_access_not_available');
    }
    await setSessionToken(token);
    setIsRoleBlocked(false);
    setUser(me);
    void upsertDevice(); // register device on sign-in; best-effort; token now in SecureStore
  }

  // logout is guaranteed to clear local state — it never throws.
  // API cleanup (device revocation, session revocation) is best-effort.
  async function logout(): Promise<void> {
    const token = await getSessionToken().catch(() => null);
    const installId = await getAppInstallationId().catch(() => null);
    await clearSessionToken().catch(() => {});
    setUser(null);
    setIsRoleBlocked(false);
    queryClient.clear();

    void (async () => {
      if (installId) {
        await api.delete(`/me/devices/${installId}`, undefined, token).catch(() => {});
      }
      await api.post('/auth/logout', undefined, true, undefined, token).catch(() => {});
    })();
  }

  function clearRoleBlocked(): void {
    setIsRoleBlocked(false);
  }

  return (
    <AuthContext.Provider
      value={{ user, isLoading, isRoleBlocked, storeToken, logout, clearRoleBlocked }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
