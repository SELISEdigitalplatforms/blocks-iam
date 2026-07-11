import { useQuery } from "@tanstack/react-query";
import type { IUserSessionApi } from "../api";
import { securityService } from "../services/security.service";

export const useUserSessions = (userId?: string, options?: { enabled?: boolean }) =>
  useQuery({
    queryKey: ["security-sessions", userId ?? null],
    queryFn: () => securityService.getSessions(userId),
    enabled: options?.enabled ?? true,
    refetchOnWindowFocus: false,
  });

export type IUserSessionsQuery = ReturnType<typeof useUserSessions>;
export type IUserSessionsResponse = IUserSessionApi[];