import { SignupForm } from "./signup-form";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { useGetSignUpSetting } from "@blocks-idp/iam/hooks/use-user";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { Loader } from "lucide-react";
import { Link } from "react-router-dom";
import { OidcAuthShell } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { SIGNUP_PANEL } from "@blocks-idp/authentication/pages/oidc/oidc-panel-config";

export const Signup = () => {
  const projectKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

  const { data: loginOption, isLoading: isLoginOptionLoading } = useGetLoginOptions();
  const { data: signUpSetting, isLoading: isSignUpSettingLoading } = useGetSignUpSetting({ projectKey });

  const isLoading = isLoginOptionLoading || isSignUpSettingLoading;

  return (
    <OidcAuthShell
      panelConfig={SIGNUP_PANEL}
      heading="Create Your Blocks Account"
      headingDimFirst={3}
      successTitle="Account Created"
      successSubtitle="Check your inbox for the activation link…"
      footerNote={
        <p className="text-xs" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
          Already a member?{" "}
          <Link to="/login" className="oidc-sci-fi-link">Sign in</Link>
        </p>
      }
    >
      {isLoading ? (
        <div className="flex items-center justify-center py-8">
          <Loader size={20} style={{ color: "var(--accent2)", animation: "oidc-spin 1s linear infinite" }} />
        </div>
      ) : !loginOption || loginOption.allowedGrantTypes?.length < 1 || !signUpSetting ? null : (
        <SignupForm
          loginOption={loginOption}
          emailSignUpEnabled={signUpSetting?.IsEmailPasswordSignUpEnabled || false}
          ssoSignUpEnabled={signUpSetting?.IsSSoSignUpEnabled || false}
        />
      )}
    </OidcAuthShell>
  );
};
