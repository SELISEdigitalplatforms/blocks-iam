import { authClientService } from "@blocks-idp/authentication/services/auth-clients.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useGetAuthClientCredentials = () => {
  return useQuery({
    queryKey: ["authentication", "auth-clients"],
    queryFn: () => authClientService.clients.getClientCredentials(),
  });
};

export const useSaveAuthClient = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["authentication", "auth-clients", "save"],
    mutationFn: authClientService.clients.saveClientCredential,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["authentication", "auth-clients"] });
    },
  });
};

export const useDeleteAuthClient = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["authentication", "auth-clients", "delete"],
    mutationFn: authClientService.clients.deleteClientCredential,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["authentication", "auth-clients"] });
    },
  });
};
