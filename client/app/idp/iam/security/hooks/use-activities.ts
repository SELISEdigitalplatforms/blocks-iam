import { useQuery } from "@tanstack/react-query";
import type { IActivityPageResponseApi, IGetActivitiesPayload } from "../api";
import { securityService } from "../services/security.service";

export const useActivities = (
  payload: IGetActivitiesPayload,
  options?: { enabled?: boolean },
) =>
  useQuery({
    queryKey: ["security-activities", payload],
    queryFn: () => securityService.getActivities(payload),
    enabled: (options?.enabled ?? true) && (payload.pageSize ?? 0) >= 0,
    refetchOnWindowFocus: false,
  });

export type IActivitiesQuery = ReturnType<typeof useActivities>;
export type IActivitiesResponse = IActivityPageResponseApi;