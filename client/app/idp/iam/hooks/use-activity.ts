import { toast } from "@/hooks/use-toast";
import { IGeneratePATPayload } from "@blocks-idp/iam/models/user";
import { IGetActivitiesPayload } from "@blocks-idp/iam/models/activity";
import { userService } from "@blocks-idp/iam/services/user.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useGetSecurityOverview = (options?: { enabled?: boolean }) => {
  return useQuery({
    queryKey: ["security-overview"],
    queryFn: () => userService.getSecurityOverview(),
    enabled: options?.enabled ?? true,
  });
};

export const useGetSessionTimeline = (sessionId: string, options?: { enabled?: boolean }) => {
  return useQuery({
    queryKey: ["session-timeline", sessionId],
    queryFn: () => userService.getSessionTimeline(sessionId),
    enabled: (options?.enabled ?? true) && !!sessionId,
  });
};

export const useRevokeSession = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (sessionId: string) => userService.revokeSession(sessionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["security-overview"] });
      queryClient.invalidateQueries({ queryKey: ["session-timeline"] });
      queryClient.invalidateQueries({ queryKey: ["activities"] });
    },
  });
};

export const useRevokeRefreshToken = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ tokenId, reason }: { tokenId: string; reason?: string }) =>
      userService.revokeRefreshToken(tokenId, reason),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["security-overview"] });
      queryClient.invalidateQueries({ queryKey: ["session-timeline"] });
    },
  });
};

export const useGetActivities = (option: IGetActivitiesPayload, options?: { enabled?: boolean }) =>
  useQuery({
    queryKey: ["activities", option],
    queryFn: () => userService.getActivities(option),
    enabled: (options?.enabled ?? true) && !!option?.userId,
  });

export const useGetPats = () => {
  return useQuery({
    queryKey: ["personalAccessTokens"],
    queryFn: () => userService.getPats(),
    select: (data) => {
      if (!data || !Array.isArray(data)) return [];

      return [...data].sort((a, b) => {
        const dateA = new Date(a.createdDate || a.createdDate || 0).getTime();
        const dateB = new Date(b.createdDate || b.createdDate || 0).getTime();
        return dateB - dateA;
      });
    }
  });
};

export const useGeneratePats = () => {
    const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: IGeneratePATPayload) => userService.generatePats(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["personalAccessTokens"] });
      toast({ variant: "success", description: "Token generated successfully!" });
    },
    onError: () => {
      toast({ variant: "destructive", description: "Failed to generate token" });
    },
  });
};
