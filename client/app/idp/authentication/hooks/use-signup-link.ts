import { useMutation, useQuery } from "@tanstack/react-query";
import {
  signupLinkService,
  type AuthenticateSignupLinkPayload,
} from "../services/signup-link.service";

export function useSignupLinkContext(code: string | null, tenantId?: string) {
  return useQuery({
    // The code is NEVER in a query key (FRONTEND contract).
    queryKey: ["signup-link", "context"],
    queryFn: () => signupLinkService.getContext(code!, tenantId),
    enabled: !!code,
    retry: false,
    staleTime: Infinity,
    gcTime: 0,
  });
}

export function useRedeemSignupLink(tenantId?: string) {
  return useMutation({
    mutationKey: ["signup-link", "redeem"],
    mutationFn: (code: string) => signupLinkService.redeem(code, tenantId),
  });
}

export function useCompleteSignupLinkMfa(tenantId?: string) {
  return useMutation({
    mutationKey: ["signup-link", "redeem-mfa"],
    mutationFn: (payload: { mfaId: string; mfaCode: string }) =>
      signupLinkService.completeMfa(payload.mfaId, payload.mfaCode, tenantId),
  });
}

export function useAuthenticateSignupLink(tenantId?: string) {
  return useMutation({
    // The password is never part of a key; it lives only in the mutation variables.
    mutationKey: ["signup-link", "redeem-authenticate"],
    mutationFn: (payload: AuthenticateSignupLinkPayload) =>
      signupLinkService.authenticate(payload, tenantId),
  });
}
