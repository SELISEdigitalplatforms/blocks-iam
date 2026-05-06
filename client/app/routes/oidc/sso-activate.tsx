import { Navigate, useSearchParams } from "react-router-dom";
import { SsoActivate } from "@blocks-idp/authentication/pages/sso-activate";

export default function OidcSsoActivatePage() {
  const [searchParams] = useSearchParams();
  const code = searchParams.get("code");
  const username = searchParams.get("username");

  if (!code || !username) return <Navigate to="/oidc/login" replace />;

  return <SsoActivate oauthParams={{ code, username }} />;
}
