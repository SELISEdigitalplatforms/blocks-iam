import { SignupForm } from "./signup-form";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { useGetSignUpSetting } from "@blocks-idp/iam/hooks/use-user";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Link } from "react-router-dom";
import { OidcAuthShell } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { SIGNUP_PANEL } from "@blocks-idp/authentication/pages/oidc/oidc-panel-config";

export const Signup = () => {
  const { data: loginOption, isLoading: isLoginOptionLoading } = useGetLoginOptions();
  const { data: signUpSetting, isLoading: isSignUpSettingLoading } = useGetSignUpSetting();

  const isLoading = isLoginOptionLoading || isSignUpSettingLoading;

  return (
    <OidcAuthShell
      panelConfig={SIGNUP_PANEL}
      heading="Create Your Blocks Account"
      headingDimFirst={3}
      successTitle="Account Created"
      successSubtitle="Check your inbox for the activation link…"
      showCorners={false}
      footerNote={
        <p className="text-xs" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
          Already a member?{" "}
          <Link to="/login" className="oidc-sci-fi-link">Sign in</Link>
        </p>
      }
    >
      {isLoading ? (
        <div className="flex flex-col gap-3 py-2">
          <Skeleton className="h-9 w-full rounded-md" />
          <Skeleton className="h-9 w-full rounded-md" />
          <Skeleton className="h-9 w-full rounded-md" />
          <Skeleton className="h-9 w-1/2 rounded-md" />
        </div>
      ) : !loginOption || loginOption.allowedGrantTypes?.length < 1 || !signUpSetting ? null : (
        <SignupForm
          loginOption={loginOption}
          emailSignUpEnabled={signUpSetting?.isEmailPasswordSignUpEnabled || false}
          ssoSignUpEnabled={signUpSetting?.isSSoSignUpEnabled || false}
        />
      )}
    </OidcAuthShell>
  );
};
