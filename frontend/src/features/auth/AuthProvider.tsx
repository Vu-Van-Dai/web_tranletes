import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { authApi } from '@/services/api';
import { onSessionChange, refreshSession, setAccessToken, setCurrentHouse } from '@/services/http';
import type { Me, Membership, Permission, Session } from '@/types/api';

type Status = 'loading' | 'authenticated' | 'anonymous';

interface AuthContextValue {
  status: Status;
  user: Me | null;
  applySession: (session: Session) => void;
  logout: () => Promise<void>;
  reloadMe: () => Promise<void>;
  houses: Membership[];
  currentHouse: Membership | null;
  selectHouse: (houseId: string) => void;
  can: (permission: Permission) => boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);
const HOUSE_KEY = 'cp-house';

function readSavedHouse(): string | null {
  try {
    return localStorage.getItem(HOUSE_KEY);
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<Status>('loading');
  const [user, setUser] = useState<Me | null>(null);
  const [houseId, setHouseId] = useState<string | null>(readSavedHouse);

  const applySession = useCallback((session: Session) => {
    setAccessToken(session.accessToken);
    setUser(session.user);
    setStatus('authenticated');
  }, []);

  // Silent sign-in from the HttpOnly refresh cookie; also follow refreshes triggered by the HTTP layer.
  useEffect(() => {
    onSessionChange((session) => {
      if (session) {
        setUser(session.user);
        setStatus('authenticated');
      } else {
        setUser(null);
        setStatus('anonymous');
      }
    });
    refreshSession().then((session) => {
      if (!session) setStatus('anonymous');
    });
    return () => onSessionChange(() => undefined);
  }, []);

  const houses = useMemo(() => user?.memberships ?? [], [user]);
  const currentHouse = useMemo(() => {
    if (houses.length === 0) return null;
    return houses.find((h) => h.houseId === houseId) ?? houses.find((h) => h.houseStatus === 'active') ?? houses[0] ?? null;
  }, [houses, houseId]);

  useEffect(() => {
    setCurrentHouse(currentHouse?.houseId ?? null);
  }, [currentHouse]);

  const selectHouse = useCallback(
    (id: string) => {
      setHouseId(id);
      setCurrentHouse(id);
      try {
        localStorage.setItem(HOUSE_KEY, id);
      } catch {
        // storage unavailable (private mode) — selection lasts for this tab only
      }
      queryClient.removeQueries({ queryKey: ['house'] });
    },
    [queryClient],
  );

  const logout = useCallback(async () => {
    try {
      await authApi.logout();
    } catch {
      // already signed out server-side
    }
    setAccessToken(null);
    setUser(null);
    setStatus('anonymous');
    queryClient.clear();
  }, [queryClient]);

  const reloadMe = useCallback(async () => {
    setUser(await authApi.me());
  }, []);

  const can = useCallback((permission: Permission) => !!currentHouse?.permissions.includes(permission), [currentHouse]);

  const value = useMemo<AuthContextValue>(
    () => ({ status, user, applySession, logout, reloadMe, houses, currentHouse, selectHouse, can }),
    [status, user, applySession, logout, reloadMe, houses, currentHouse, selectHouse, can],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
