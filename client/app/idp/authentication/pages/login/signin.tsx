
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { showErrorToast } from "@/hooks/use-toast";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { useGetSignUpSetting } from "@blocks-idp/iam/hooks/use-user";
import { Link } from "react-router";
import { useEffect } from "react";
import { SigninForm } from "./signin-form";
import { SsoSignin } from "./sso-signin";
import { buildOIDCNavigationUrl, extractOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";

type SigninProps = {
  ssoError?: string;
  mode?: "default" | "oidc";
  oidcContext?: {
    clientId?: string;
    scope?: string;
    state?: string;
    nonce?: string;
    redirectUri?: string;
    themeColor?: string;
  };
};

const SigninSkeleton = () => (
  <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
    <CardHeader className="text-center">
      {/* Title */}
      <Skeleton className="mx-auto h-9 w-40 rounded-md" />
      {/* Subtitle */}
      <Skeleton className="mx-auto mt-2 h-7 w-16 rounded-md" />
    </CardHeader>
    <CardContent className="flex flex-1 flex-col justify-between">
      <div className="flex flex-1 flex-col justify-center gap-4">
        {/* Email label + input */}
        <div className="flex flex-col gap-2">
          <Skeleton className="h-4 w-10 rounded" />
          <Skeleton className="h-10 w-full rounded-md" />
        </div>
        {/* Password label + input */}
        <div className="flex flex-col gap-2">
          <Skeleton className="h-4 w-16 rounded" />
          <Skeleton className="h-10 w-full rounded-md" />
        </div>
        {/* Forgot password */}
        <Skeleton className="ml-auto h-4 w-28 rounded" />
        {/* Login button */}
        <Skeleton className="h-10 w-full rounded-md" />
        {/* OR divider */}
        <div className="flex items-center gap-2">
          <Skeleton className="h-px flex-1" />
          <Skeleton className="h-4 w-6 rounded" />
          <Skeleton className="h-px flex-1" />
        </div>
        {/* SSO buttons */}
        <div className="flex flex-col gap-3">
          <Skeleton className="h-10 w-full rounded-md" />
          <Skeleton className="h-10 w-full rounded-md" />
        </div>
      </div>
      {/* Sign up link */}
      <div className="mt-3 flex items-center justify-center gap-2">
        <Skeleton className="h-4 w-24 rounded" />
        <Skeleton className="h-4 w-14 rounded" />
      </div>
    </CardContent>
  </Card>
);

const SigninUnavailable = () => (
  <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
    <CardHeader className="text-center">
      <CardTitle className="text-3xl">Blocks IAM</CardTitle>
      <CardDescription className="text-xl text-foreground">Log in</CardDescription>
    </CardHeader>
    <CardContent className="flex flex-1 flex-col justify-center gap-3 text-center">
      <p className="text-medium-emphasis">
        This sign-in link is missing the application it belongs to, so there is nothing to
        sign in to from here.
      </p>
      <p className="text-sm text-low-emphasis">
        Open the application you were invited to and sign in from there, or contact your
        administrator.
      </p>
    </CardContent>
  </Card>
);

export const Signin = ({ ssoError, mode = "default", oidcContext }: SigninProps) => {
  // Signup settings are tenant-scoped: without the tenant from the OIDC request
  // this reads the default tenant's config instead of the one being signed into.
  const { tenantId: oidcTenantId } = extractOIDCParams();
  const { data: loginOption, isLoading: isLoginOptionLoading } =
    useGetLoginOptions(oidcTenantId);
  const { data: signUpSetting, isLoading: isSignUpSettingLoading } =
    useGetSignUpSetting(oidcTenantId);

  useEffect(() => {
    if (ssoError) {
      showErrorToast({ errors: ssoError });
    }
  }, [ssoError]);

  if (isLoginOptionLoading || isSignUpSettingLoading) {
    return <SigninSkeleton />;
  }

  const hasPassword = !!loginOption?.allowedGrantTypes?.includes(GRANT_TYPES.password);
  const hasSocial = !!loginOption?.allowedGrantTypes?.includes(GRANT_TYPES.social);

  // A tenant configured only for authorization_code / client_credential has nothing
  // this card can render. Rendering it anyway produced a header over an empty body —
  // the dead end an invitee hits when a confirmation page falls back to /oidc/login
  // with no client to authorize against. Say so instead of showing a blank card.
  if (!loginOption || !(hasPassword || hasSocial)) {
    return <SigninUnavailable />;
  }

  const showSignUp =
    (signUpSetting?.isSignUpEnable ?? false) && mode !== "oidc" && (hasPassword || hasSocial);

  const oidcQuery = oidcTenantId
    ? buildOIDCNavigationUrl("/oidc/signup").split("?")[1] || ""
    : "";
  const signUpUrl =
    mode === "oidc" && oidcTenantId
      ? `/oidc/signup/${encodeURIComponent(oidcTenantId)}${oidcQuery ? `?${oidcQuery}` : ""}`
      : "/oidc/signup";

  return (
    <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        <CardTitle className="text-3xl">Blocks IAM</CardTitle>
        <CardDescription className="text-xl text-foreground">Log in</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col justify-between">
        <div className="flex flex-1 flex-col justify-center">
          {hasPassword && <SigninForm mode={mode} oidcContext={oidcContext} />}
          {hasPassword && hasSocial && (
            <div className="my-2 mt-4 flex items-center">
              <hr className="flex-grow border" />
              <span className="mx-2 text-xs text-low-emphasis">OR</span>
              <hr className="flex-grow border" />
            </div>
          )}
          {hasSocial && <SsoSignin loginOption={loginOption} mode={mode} />}
        </div>
        {showSignUp && (
          <div className="flex items-center justify-center">
            <div className="mt-3 flex items-center text-medium-emphasis">
              <p>Not a member?</p>
              <Link to={signUpUrl} className="ml-2 inline-block text-sm text-primary">
                Sign up
              </Link>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
};

