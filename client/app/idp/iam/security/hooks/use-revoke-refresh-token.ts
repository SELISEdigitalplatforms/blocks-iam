import { useMutation } from "@tanstack/react-query";
import { securityService } from "../services/security.service";

export const useRevokeRefreshToken = (userId?: string) =>
  useMutation({
    mutationFn: ({ tokenId, reason }: { tokenId: string; reason?: string }) =>
      securityService.revokeRefreshToken(tokenId, reason, userId),
  });