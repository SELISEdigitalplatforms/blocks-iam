import { useMutation, useQuery } from "@tanstack/react-query";

type MfaConfig = {
  enableMfa: boolean;
  userMfaType: number[];
};

export const useGetMFAConfig = (_payload?: { projectKey?: string }) => {
  return useQuery<MfaConfig>({
    queryKey: ["mfa-config", _payload?.projectKey || ""],
    queryFn: async () => ({
      enableMfa: false,
      userMfaType: [],
    }),
  });
};

export const useDisableMfa = (_payload?: { id?: string; projectKey?: string }) => {
  return useMutation({
    mutationKey: ["disable-mfa", _payload?.id || "", _payload?.projectKey || ""],
    mutationFn: async () => ({ success: true }),
  });
};
