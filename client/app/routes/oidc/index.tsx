import { useSearchParams } from "react-router-dom";
import { OIDCPermissionWrapper } from "@blocks-idp/authentication/pages/oidc/permission-wrapper";
import { OIDCSignin } from "@blocks-idp/authentication/pages/oidc/oidc-signin";

export default function OidcIndexPage() {
  const [searchParams] = useSearchParams();
  const userName = searchParams.get("userName");

  if (userName && userName.trim() !== "") {
    return <OIDCPermissionWrapper />;
  }

  return <OIDCSignin />;
}
