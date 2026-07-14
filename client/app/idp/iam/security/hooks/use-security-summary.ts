import { useQuery } from "@tanstack/react-query";
import type { ISecuritySummaryApi } from "../api";
import { securityService } from "../services/security.service";

export const useSecuritySummary = (userId?: string, options?: { enabled?: boolean }) =>
  useQuery({
    queryKey: ["security-summary", userId ?? null],
    queryFn: () => securityService.getSummary(userId),
    enabled: options?.enabled ?? true,
    refetchOnWindowFocus: false,
  });

export type ISecuritySummaryQuery = ReturnType<typeof useSecuritySummary>;
export type ISecuritySummaryResponse = ISecuritySummaryApi;