import { User } from "@/idp/shared/models/admin.models";
import { create } from "zustand";
import { persist } from "zustand/middleware";

interface AuthState {
  isAuthenticated: boolean;
  user: User | null;
  accessToken: string | null;
  refreshToken: string | null;
  authMode: "root" | "impersonation";
  restoreReason: string | null;
  setUser: (user: User | null) => void;
  setAuthenticated: () => void;
  setUnAuthenticated: () => void;
  setTokens: (accessToken: string, refreshToken: string) => void;
  setAuthMode: (mode: "root" | "impersonation", reason?: string | null) => void;
  clearRestoreReason: () => void;
  clearTokens: () => void;
  reset: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      isAuthenticated: false,
      user: null,
      accessToken: null,
      refreshToken: null,
      authMode: "root",
      restoreReason: null,
      setUser: (user: User | null) => {
        set((state) => ({ ...state, user }));
      },
      setAuthenticated: () => {
        set((state) => ({ ...state, isAuthenticated: true }));
      },
      setUnAuthenticated: () => {
        set((state) => ({ ...state, isAuthenticated: false, user: null }));
      },
      setTokens: (accessToken: string, refreshToken: string) => {
        // Store in localStorage via Zustand persist
        set((state) => ({ ...state, accessToken, refreshToken }));
        // Also store in cookies for API requests
        if (typeof document !== 'undefined') {
          document.cookie = `access_token=${accessToken}; path=/; SameSite=Strict`;
          document.cookie = `refresh_token=${refreshToken}; path=/; SameSite=Strict`;
        }
      },
      setAuthMode: (mode: "root" | "impersonation", reason?: string | null) => {
        set((state) => ({ ...state, authMode: mode, restoreReason: reason ?? null }));
      },
      clearRestoreReason: () => {
        set((state) => ({ ...state, restoreReason: null }));
      },
      clearTokens: () => {
        set((state) => ({ ...state, accessToken: null, refreshToken: null }));
        // Clear cookies
        if (typeof document !== 'undefined') {
          document.cookie = 'access_token=; path=/; expires=Thu, 01 Jan 1970 00:00:00 UTC;';
          document.cookie = 'refresh_token=; path=/; expires=Thu, 01 Jan 1970 00:00:00 UTC;';
        }
      },
      reset: () => {
        set({
          isAuthenticated: false,
          user: null,
          accessToken: null,
          refreshToken: null,
          authMode: "root",
          restoreReason: null,
        });
      },
    }),
    {
      name: "auth-storage",
    },
  ),
);
