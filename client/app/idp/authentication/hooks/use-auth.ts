import { useMutation, useQuery } from "@tanstack/react-query";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { oauthService } from "@blocks-idp/authentication/services/oauth.service";
import { ISignupByEmailPayload } from "@blocks-idp/authentication/models/auth.model";

export const useSigninByEmail = () => {
  return useMutation({
    mutationKey: ["login", "email"],
    mutationFn: authService.signinByEmail,
  });
};

export const useSigninBySSO = () => {
  return useMutation({
    mutationKey: ["login", "sso"],
    mutationFn: oauthService.signinBySSO,
  });
};

export const useVerifyMfa = () => {
  return useMutation({
    mutationKey: ["verify", "mfa"],
    mutationFn: authService.verifyMfa,
  });
};

export const useLogout = () => {
  return useMutation({
    mutationKey: ["logout"],
    mutationFn: authService.logout,
  });
};

export const useSignupByEmail = () => {
  return useMutation({
    mutationKey: ["signup", "email"],
    mutationFn: ({
      tenantId,
      ...payload
    }: ISignupByEmailPayload & { tenantId?: string }) =>
      authService.signupByEmail(payload, tenantId),
  });
};


export const useGetLoginOptions = (tenantId?: string, enabled = true) => {
  return useQuery({
    queryKey: ["login-options", tenantId],
    queryFn: () => authService.getLoginOptions(tenantId),
    enabled,
  });
};