import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { mfaService } from "../services/mfa.service";

const MFA_KEY = "mfa-config";
const USER_BY_ID_KEY = "user-by-id";

export const useGetMFAConfig = () => {
  return useQuery({
    queryKey: [MFA_KEY, "get"],
    queryFn: () => mfaService.getConfigurations(),
  });
};

export const useSaveMFAConfig = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [MFA_KEY, "save"],
    mutationFn: mfaService.saveMFAConfiguration,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [MFA_KEY, "get"] });
    },
  });
};

export const useConfigureUserMFA = (option: { id: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [MFA_KEY, "configure"],
    mutationFn: mfaService.configureUserMFA,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [USER_BY_ID_KEY, option] });
      queryClient.invalidateQueries({ queryKey: ["user"] });
    },
  });
};

export const useGetTotp = (option: { id: string }) => {
  return useQuery({
    queryKey: [MFA_KEY, "setup-totp", option],
    queryFn: () => mfaService.setupUserTotp(option),
    // A refetch would not change the secret but would flicker the QR while the user scans it.
    refetchOnWindowFocus: false,
    staleTime: Infinity,
    retry: false,
  });
};

export const useGenerateUserMfaOTP = () => {
  return useMutation({
    mutationKey: [MFA_KEY, "generate-otp"],
    mutationFn: mfaService.generateUserMfaOTP,
  });
};

export const useVerifyMfaOTP = (option: { id: string; projectKey?: string; own?: boolean }) => {
  const queryClient = useQueryClient();
  // own is split off only to keep it out of the query key.
  const { own: _own, ...rest } = option;
  return useMutation({
    mutationKey: [MFA_KEY, "verify-otp"],
    mutationFn: mfaService.verifyOtp,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [USER_BY_ID_KEY, rest] });
      queryClient.invalidateQueries({ queryKey: ["user"] });
    },
  });
};

export const useResendMfaOTP = () => {
  return useMutation({
    mutationKey: [MFA_KEY, "resend-otp"],
    mutationFn: mfaService.resendOtp,
  });
};
export const useDisableMfa = (option: { id: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [MFA_KEY, "disable"],
    mutationFn: mfaService.disableMFA,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [USER_BY_ID_KEY, option] });
      queryClient.invalidateQueries({ queryKey: ["user"] });
    },
  });
};

export const useVerifyTotpSetup = (option: { id: string }) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: [MFA_KEY, "verify-totp-setup"],
    mutationFn: ({ code }: { code: string }) =>
      mfaService.verifyTotpSetup({ code, userId: option.id }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: [USER_BY_ID_KEY, option] });
      queryClient.invalidateQueries({ queryKey: ["user"] });
    },
  });
};
