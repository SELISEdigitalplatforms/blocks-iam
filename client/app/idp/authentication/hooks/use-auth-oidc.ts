import { authOidc } from "@blocks-idp/authentication/services/auth-clients-oidc.service";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

export const useGetAuthOidcCredentials = () => {
  return useQuery({
    queryKey: ["authentication", "auth-oidc-list"],
    queryFn: () => authOidc.clients.getOidcCredentials({}),
  });
};

export const useGetAuthOidcCredential = (options: { clientId: string }, enabled: boolean = true) => {
  return useQuery({
    queryKey: ["authentication", "auth-oidc", options],
    queryFn: () => authOidc.clients.getOidcCredential(options),
    enabled,
  });
};

export const useSaveAuthOidc = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["authentication", "auth-oidc", "save"],
    mutationFn: authOidc.clients.saveOidcCredential,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["authentication"], exact: false });
    },
  });
};

export const useDeleteAuthOidc = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["authentication", "auth-oidc", "delete"],
    mutationFn: authOidc.clients.deleteOidcCredential,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["authentication", "auth-oidc-list"] });
      queryClient.invalidateQueries({ queryKey: ["authentication", "auth-oidc"] });
    },
  });
};
