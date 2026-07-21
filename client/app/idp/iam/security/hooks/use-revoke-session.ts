import { useMutation, useQueryClient } from "@tanstack/react-query";
import { securityService } from "../services/security.service";

export const useRevokeSession = (userId?: string) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ sessionId, reason }: { sessionId: string; reason?: string }) =>
      securityService.revokeSession(sessionId, reason, userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["security-summary"] });
      queryClient.invalidateQueries({ queryKey: ["security-sessions"] });
      queryClient.invalidateQueries({ queryKey: ["session-details"] });
      queryClient.invalidateQueries({ queryKey: ["security-activities"] });
    },
  });
};