import { authenticationService } from "@blocks-idp/authentication/services/authentication.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useGetAuthConfig = () => {
  return useQuery({
    queryKey: ["authentication", "auth-config"],
    queryFn: () => authenticationService.configuration.getConfig(),
  });
};

export const useSaveAuthConfig = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["authentication", "auth-config", "save"],
    mutationFn: authenticationService.configuration.saveAuthConfig,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["authentication", "auth-config"] });
    },
  });
};
