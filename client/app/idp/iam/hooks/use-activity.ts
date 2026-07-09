import { toast } from "@/hooks/use-toast";
import { IGeneratePATPayload, IGetHistoriesPayload, IGetSessionPayload } from "@blocks-idp/iam/models/user";
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
      queryClient.invalidateQueries({ queryKey: ["sessions"] });
      queryClient.invalidateQueries({ queryKey: ["session"] });
      queryClient.invalidateQueries({ queryKey: ["session-timeline"] });
    },
  });
};

export const useGetHistories = (option: IGetHistoriesPayload) => {
  return useQuery({
    queryKey: ["histories", option],
    queryFn: () => userService.getHistories(option),
  });
};

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
