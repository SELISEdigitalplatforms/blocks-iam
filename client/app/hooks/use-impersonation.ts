import { impersonationService } from "@/services/impersonation.service";
import { useMutation } from "@tanstack/react-query";

export const useStartImpersonation = () => {
  return useMutation({
    mutationKey: ["impersonation", "start"],
    mutationFn: impersonationService.startImpersonation,
  });
};

export const useStopImpersonation = () => {
  return useMutation({
    mutationKey: ["impersonation", "stop"],
    mutationFn: impersonationService.stopImpersonation,
  });
};