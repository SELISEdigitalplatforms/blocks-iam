import { useProjectStore } from "@seliseblocks/genesis-os";
import { useGetAuthConfig } from "@blocks-idp/authentication/hooks/use-auth-config";
import { ClientCredentialList } from "./client-credentials-list";

export const ClientCredentials = () => {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { isLoading } = useGetAuthConfig({ projectKey: tenantId });

  // const isClientCredentialAllowed = authConfig?.allowedGrantTypes?.includes(GRANT_TYPES.clientCredential);

  if (isLoading) {
    return <ClientCredentialList />;
  }


  return (
    <div className="w-full min-w-0">
      <ClientCredentialList />
    </div>
  );
};
