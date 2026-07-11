import { useQuery } from "@tanstack/react-query";
import type { ISessionDetailsApi } from "../api";
import { securityService } from "../services/security.service";

export const useSessionDetails = (
  sessionId: string,
  userId?: string,
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ["session-details", sessionId, userId ?? null],
    queryFn: () => securityService.getSessionDetails(sessionId, userId),
    enabled: (options?.enabled ?? true) && !!sessionId,
    refetchOnWindowFocus: false,
  });

export type ISessionDetailsQuery = ReturnType<typeof useSessionDetails>;
export type ISessionDetailsResponse = ISessionDetailsApi;