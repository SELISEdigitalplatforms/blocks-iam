import { SignupForm } from "./signup-form";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { useGetSignUpSetting } from "@blocks-idp/iam/hooks/use-user";
import { useGetSignupOrganizationConfig } from "@blocks-idp/iam/hooks/use-organization";
import { Loader } from "lucide-react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { OidcAuthShell, OidcFooter } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { SIGNUP_PANEL } from "@blocks-idp/authentication/pages/oidc/oidc-panel-config";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";

export const Signup = ({ tenantId }: { tenantId?: string } = {}) => {
  const { data: oidcUiConfig } = useOidcUiConfig(tenantId);
  const template = oidcUiConfig?.template;
  const { data: signUpSetting, isLoading: isSignUpSettingLoading } =
    useGetSignUpSetting(tenantId);

  const isSignUpEnabled = signUpSetting?.isSignUpEnable ?? false;
  const emailSignUpEnabled = signUpSetting?.isEmailPasswordSignUpEnabled ?? false;
  const ssoSignUpEnabled = signUpSetting?.isSSoSignUpEnabled ?? false;

  const { data: loginOption, isLoading: isLoginOptionLoading } = useGetLoginOptions(
    tenantId,
    ssoSignUpEnabled,
  );
  const hasSsoProviders = (loginOption?.ssoInfo?.length ?? 0) > 0;

  const { data: orgConfig, isLoading: isOrgConfigLoading } =
    useGetSignupOrganizationConfig(tenantId, { enabled: isSignUpEnabled });

  // The server requires both flags before it will create an org from signup
  // (CreateOrganizationAsync), so asking for a name without both would collect
  // input the backend then rejects.
  const collectOrganizationName =
    (orgConfig?.allowOrgCreationFromSignup ?? false) &&
    (orgConfig?.isMultiOrgEnabled ?? false);

  const showEmailSignup = isSignUpEnabled && emailSignUpEnabled;
  const showSsoSignup = isSignUpEnabled && ssoSignUpEnabled && hasSsoProviders;
  const showSignupForm = showEmailSignup || showSsoSignup;

  const isLoading =
    isSignUpSettingLoading ||
    (isSignUpEnabled && isOrgConfigLoading) ||
    (ssoSignUpEnabled && isLoginOptionLoading);

  if (!template) {
    return (
      <div className="oidc-scifi-root min-h-screen flex items-center justify-center bg-[var(--bg)]">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
      </div>
    );
  }

  return (
    <OidcAuthShell
      panelConfig={SIGNUP_PANEL}
      theme={template.theme}
      logoUrl={template.branding.logoUrl}
      brandName={template.branding.brandName}
      heading={template.pages.signup.heading}
      headingDimFirst={3}
      successTitle={template.pages.signup.successTitle}
      successSubtitle={template.pages.signup.successSubtitle}
      showCorners={false}
      footerNote={
        <div className="flex flex-col gap-2">
          <p className="text-xs" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
            {template.pages.signup.loginPrompt}{" "}
            <LoginReturnLink className="oidc-sci-fi-link">
              {template.pages.signup.loginLink}
            </LoginReturnLink>
          </p>
          <OidcFooter footerText={template.pages.shared.footerText} />
        </div>
      }
    >
      {isLoading ? (
        <div className="flex flex-col gap-3 py-2">
          <Skeleton className="h-9 w-full rounded-md" />
          <Skeleton className="h-9 w-full rounded-md" />
          <Skeleton className="h-9 w-full rounded-md" />
          <Skeleton className="h-9 w-1/2 rounded-md" />
        </div>
      ) : showSignupForm ? (
        <SignupForm
          loginOption={loginOption}
          emailSignUpEnabled={showEmailSignup}
          ssoSignUpEnabled={showSsoSignup}
          tenantId={tenantId}
          collectOrganizationName={collectOrganizationName}
        />
      ) : null}
    </OidcAuthShell>
  );
};
