import { toast } from "@/hooks/use-toast";
import { IGeneratePATPayload, IGetSessionPayload } from "@blocks-idp/iam/models/user";
import { IGetActivitiesPayload } from "@blocks-idp/iam/models/activity";
import { userService } from "@blocks-idp/iam/services/user.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useGetSessions = (option: IGetSessionPayload) => {
  return useQuery({
    queryKey: ["sessions", option],
    queryFn: () => userService.getSessions(option),
  });
};

export const useGetSessionById = (sessionId: string, options?: { enabled?: boolean }) => {
  return useQuery({
    queryKey: ["session", sessionId],
    queryFn: () => userService.getSessionById(sessionId),
    enabled: (options?.enabled ?? true) && !!sessionId,
  });
};

export const useRevokeSession = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (sessionId: string) => userService.revokeSession(sessionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["sessions"] });
      queryClient.invalidateQueries({ queryKey: ["session"] });
      queryClient.invalidateQueries({ queryKey: ["activities"] });
      queryClient.invalidateQueries({ queryKey: ["session-refresh-tokens"] });
    },
  });
};

export const useGetActivities = (option: IGetActivitiesPayload, options?: { enabled?: boolean }) =>
  useQuery({
    queryKey: ["activities", option],
    queryFn: () => userService.getActivities(option),
    enabled: (options?.enabled ?? true) && !!option?.userId,
  });

export const useGetSessionRefreshTokens = (sessionId: string, options?: { enabled?: boolean }) =>
  useQuery({
    queryKey: ["session-refresh-tokens", sessionId],
    queryFn: () => userService.getSessionRefreshTokens(sessionId),
    enabled: (options?.enabled ?? true) && !!sessionId,
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
