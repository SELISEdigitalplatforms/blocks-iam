import { useQuery } from "@tanstack/react-query";
import type { IPATApi } from "../api";
import { securityService } from "../services/security.service";

export const usePats = () =>
  useQuery({
    queryKey: ["personalAccessTokens"],
    queryFn: () => securityService.getPats(),
    select: (data): IPATApi[] => {
      if (!data || !Array.isArray(data)) return [];
      return [...data].sort((a, b) => {
        const dateA = new Date(a.createdDate ?? 0).getTime();
        const dateB = new Date(b.createdDate ?? 0).getTime();
        return dateB - dateA;
      });
    },
  });