import { useProjectStore } from "@/store/useProjectStore";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { useGetAuthConfig } from "@blocks-idp/authentication/hooks/use-auth-config";
import { OidcList } from "./oidc-list";

export const OIDC = () => {
  const { data: authConfig, isLoading } = useGetAuthConfig();

  return (
    <div>
      <OidcList />
    </div>
  );
};
