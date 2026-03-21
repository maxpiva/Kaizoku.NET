"use client";

import React, {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  useRef,
} from 'react';
import { useRouter } from 'next/navigation';
import { tokenStore, registerLogoutCallback } from '@/lib/auth-token-store';
import { authService } from '@/lib/api/services/authService';
import { userService } from '@/lib/api/services/userService';
import type { UserDetail } from '@/lib/api/auth-types';

interface AuthContextType {
  user: UserDetail | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  needsSetup: boolean;
  /** True when the user logged in with a password that doesn't meet the current policy. */
  requiresPasswordChange: boolean;
  login: (usernameOrEmail: string, password: string, rememberMe?: boolean) => Promise<void>;
  register: (
    username: string,
    email: string,
    password: string,
    displayName: string,
    inviteCode: string
  ) => Promise<void>;
  logout: () => Promise<void>;
  updateUser: (user: UserDetail) => void;
  setup: (username: string, email: string, password: string, displayName: string) => Promise<void>;
  /** Clear the password change requirement after the user has updated their password. */
  dismissPasswordChange: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<UserDetail | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [needsSetup, setNeedsSetup] = useState(false);
  const [requiresPasswordChange, setRequiresPasswordChange] = useState(false);
  const router = useRouter();
  const initializedRef = useRef(false);

  const doLogout = useCallback(async () => {
    try {
      await authService.logout();
    } catch {
      // swallow — clear tokens regardless
    } finally {
      tokenStore.clearTokens();
      setUser(null);
      setRequiresPasswordChange(false);
      router.push('/login');
    }
  }, [router]);

  // Register the module-level logout callback so the API client can trigger it
  useEffect(() => {
    registerLogoutCallback(() => {
      setUser(null);
      setRequiresPasswordChange(false);
      router.push('/login');
    });
  }, [router]);

  // On mount: check if we have a stored token and validate it
  useEffect(() => {
    if (initializedRef.current) return;
    initializedRef.current = true;

    async function init() {
      setIsLoading(true);
      try {
        const status = await authService.status();

        if (status.requiresSetup) {
          setNeedsSetup(true);
          setIsLoading(false);
          return;
        }

        // Server has users — try to load current user via stored token
        const accessToken = tokenStore.getAccessToken();
        if (accessToken) {
          try {
            const currentUser = await userService.getCurrentUser();
            setUser(currentUser);
          } catch {
            tokenStore.clearTokens();
          }
        }
      } catch {
        // Status endpoint unreachable or errored — try token-based auth
        const accessToken = tokenStore.getAccessToken();
        if (accessToken) {
          try {
            const currentUser = await userService.getCurrentUser();
            setUser(currentUser);
          } catch {
            tokenStore.clearTokens();
          }
        }
      } finally {
        setIsLoading(false);
      }
    }

    void init();
  }, []);

  const login = useCallback(
    async (usernameOrEmail: string, password: string, rememberMe = false) => {
      const res = await authService.login({ usernameOrEmail, password, rememberMe });
      tokenStore.setTokens(res.accessToken, res.refreshToken, rememberMe);
      setUser(res.user);
      setRequiresPasswordChange(res.requiresPasswordChange ?? false);
    },
    []
  );

  const register = useCallback(
    async (
      username: string,
      email: string,
      password: string,
      displayName: string,
      inviteCode: string
    ) => {
      const res = await authService.register({
        username,
        email,
        password,
        displayName,
        inviteCode,
      });
      tokenStore.setTokens(res.accessToken, res.refreshToken);
      setUser(res.user);
    },
    []
  );

  const logout = useCallback(async () => {
    await doLogout();
  }, [doLogout]);

  const updateUser = useCallback((updatedUser: UserDetail) => {
    setUser(updatedUser);
  }, []);

  const setup = useCallback(
    async (username: string, email: string, password: string, displayName: string) => {
      const res = await authService.setup({ username, email, password, displayName });
      tokenStore.setTokens(res.accessToken, res.refreshToken);
      setUser(res.user);
      setNeedsSetup(false);
    },
    []
  );

  const dismissPasswordChange = useCallback(() => {
    setRequiresPasswordChange(false);
  }, []);

  const value: AuthContextType = {
    user,
    isAuthenticated: !!user,
    isLoading,
    needsSetup,
    requiresPasswordChange,
    login,
    register,
    logout,
    updateUser,
    setup,
    dismissPasswordChange,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
