import { toast } from "@/hooks/use-toast";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import type { IGeneratePATPayload } from "../api";
import { securityService } from "../services/security.service";

export const useGeneratePats = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (payload: IGeneratePATPayload) => securityService.generatePats(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["personalAccessTokens"] });
      toast({ variant: "success", description: "Token generated successfully!" });
    },
    onError: () => {
      toast({ variant: "destructive", description: "Failed to generate token" });
    },
  });
};