import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link } from "react-router";
import { z } from "zod";
import { Eye, EyeOff, Loader } from "lucide-react";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Captcha } from "@/components/captcha";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { isErrorWithErrors } from "@/lib/error";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { useAuthenticateSignupLink } from "@blocks-idp/authentication/hooks/use-signup-link";
import { buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";
import type { RedeemSignupLinkResponse } from "@blocks-idp/authentication/services/signup-link.service";

export const CONFIRM_PASSWORD_COPY = {
  heading: "Confirm it's you",
  body: "You already have an account. Enter your password to accept this invitation.",
  wrongPassword: "That password is not correct.",
  locked: "Your account is locked. Please contact support or reset your password.",
  captchaRequired: "Captcha verification is required. Please complete the given captcha.",
  captchaInvalid: "Captcha verification failed. Please try again.",
  generic: "Something went wrong. Please try again.",
} as const;

/** Server outcomes that end the password step; the page renders the matching view. */
export type ConfirmPasswordExit = "invalid_redemption" | "invalid_link";

const schema = z.object({
  password: z.string().min(1, "Enter your password."),
});

type Values = z.infer<typeof schema>;

type Props = Readonly<{
  tenantId?: string;
  redemptionId: string;
  maskedEmail: string;
  onSuccess: (res: RedeemSignupLinkResponse | undefined) => void;
  onExit: (reason: ConfirmPasswordExit) => void;
}>;

type ServerError = { error?: string; captcha_required?: boolean; captcha_site_key?: string };

function readServerError(error: unknown): ServerError {
  if (!isErrorWithErrors(error)) return {};
  const raw = error.errors as Record<string, unknown>;
  return {
    error: typeof raw.error === "string" ? raw.error : undefined,
    captcha_required: raw.captcha_required === true,
    captcha_site_key: typeof raw.captcha_site_key === "string" ? raw.captcha_site_key : undefined,
  };
}

export function InvitationConfirmPassword({
  tenantId,
  redemptionId,
  maskedEmail,
  onSuccess,
  onExit,
}: Props) {
  const [showPassword, setShowPassword] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [rejected, setRejected] = useState(false);
  const [locked, setLocked] = useState(false);
  const [captchaRequired, setCaptchaRequired] = useState(false);
  const [serverSiteKey, setServerSiteKey] = useState<string | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);

  const { data: oidcUiConfig } = useOidcUiConfig(tenantId);
  const authenticate = useAuthenticateSignupLink(tenantId);

  const siteKey =
    serverSiteKey || oidcUiConfig?.captcha?.key || getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const captchaType =
    oidcUiConfig?.captcha?.provider === "hcaptcha" ? "hCaptcha" : "reCaptcha-v2-checkbox";
  const { captcha, code: captchaCode, reset: resetCaptcha } = useCaptcha({
    siteKey,
    type: captchaType,
    generator: oidcUiConfig?.captcha?.generator,
  });

  const form = useForm<Values>({
    defaultValues: { password: "" },
    resolver: zodResolver(schema),
  });

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const forgotPasswordUrl = buildOIDCNavigationUrl("/oidc/forgot-password");
  const pending = authenticate.isPending;
  const disabled = pending || locked;

  // After a wrong password the field is cleared and focused again. The input is disabled
  // while the request is pending, so focus waits until it is enabled.
  useEffect(() => {
    if (rejected && !pending) inputRef.current?.focus();
  }, [rejected, pending]);

  function shake() {
    if (!formRef.current) return;
    formRef.current.classList.remove("oidc-animate-shake");
    void formRef.current.offsetWidth;
    formRef.current.classList.add("oidc-animate-shake");
  }

  function onError(error: unknown) {
    const server = readServerError(error);
    switch (server.error) {
      case "invalid_username_password":
        setServerError(CONFIRM_PASSWORD_COPY.wrongPassword);
        setRejected(true);
        form.reset({ password: "" });
        shake();
        if (captchaRequired) resetCaptcha();
        return;
      case "account_locked":
        setLocked(true);
        setServerError(null);
        return;
      case "captcha_enabled":
        setCaptchaRequired(true);
        if (server.captcha_site_key) setServerSiteKey(server.captcha_site_key);
        setServerError(CONFIRM_PASSWORD_COPY.captchaRequired);
        resetCaptcha();
        return;
      case "captcha_invalid":
        setCaptchaRequired(true);
        if (server.captcha_site_key) setServerSiteKey(server.captcha_site_key);
        setServerError(CONFIRM_PASSWORD_COPY.captchaInvalid);
        resetCaptcha();
        return;
      case "invalid_redemption":
      case "invalid_link":
        onExit(server.error);
        return;
      default:
        setServerError(CONFIRM_PASSWORD_COPY.generic);
    }
  }

  function onSubmit(values: Values) {
    setServerError(null);
    setRejected(false);
    authenticate.mutate(
      {
        redemptionId,
        password: values.password,
        captchaCode: captchaRequired ? captchaCode : undefined,
      },
      { onSuccess, onError },
    );
  }

  if (locked) {
    return (
      <div className="flex flex-col gap-4 py-4" data-testid="confirm-password-locked">
        <div
          role="alert"
          className="rounded border px-3 py-2 text-sm"
          style={{
            color: "var(--danger)",
            background: "var(--danger-soft)",
            borderColor: "var(--danger-border)",
          }}
        >
          {CONFIRM_PASSWORD_COPY.locked}
        </div>
        <LoginReturnLink preferOidcLogin className="oidc-sci-fi-link">
          Back to login
        </LoginReturnLink>
      </div>
    );
  }

  const errorId = "invitation-password-error";

  return (
    <Form {...form}>
      <form
        ref={formRef}
        className="flex flex-col gap-3 py-4"
        onSubmit={(event) => {
          void form.handleSubmit(onSubmit)(event);
        }}
        noValidate
      >
        <p className="text-sm" style={{ color: "var(--fg)" }}>
          {CONFIRM_PASSWORD_COPY.body}
        </p>
        <p className="text-sm font-medium" style={{ color: "var(--fg)" }} data-testid="masked-email">
          {maskedEmail}
        </p>
        <input
          type="text"
          className="sr-only"
          autoComplete="username"
          value={maskedEmail}
          readOnly
          tabIndex={-1}
          aria-hidden="true"
        />
        <FormField
          control={form.control}
          name="password"
          render={({ field }) => (
            <FormItem>
              <div className="flex flex-col gap-2">
                <div className="flex justify-between items-center">
                  <label htmlFor="invitation-password" className="oidc-sci-fi-label">
                    Password
                  </label>
                  <Link to={forgotPasswordUrl} className="oidc-sci-fi-link" style={{ fontSize: "0.75rem" }}>
                    Forgot password?
                  </Link>
                </div>
                <FormControl>
                  <div className="relative">
                    <input
                      {...field}
                      ref={(el) => {
                        field.ref(el);
                        inputRef.current = el;
                      }}
                      id="invitation-password"
                      type={showPassword ? "text" : "password"}
                      autoComplete="current-password"
                      className="oidc-sci-fi-input"
                      style={{ paddingRight: "2.75rem" }}
                      aria-invalid={rejected || !!form.formState.errors.password}
                      aria-describedby={serverError ? errorId : undefined}
                      disabled={disabled}
                    />
                    <button
                      type="button"
                      onClick={() => setShowPassword((v) => !v)}
                      className="absolute right-3 top-1/2 -translate-y-1/2"
                      style={{ color: "var(--muted)", background: "none", border: "none", cursor: "pointer", padding: 0 }}
                      aria-label={showPassword ? "Hide password" : "Show password"}
                    >
                      {showPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                    </button>
                  </div>
                </FormControl>
                <FormMessage className="text-xs" style={{ color: "var(--danger)" }} />
              </div>
            </FormItem>
          )}
        />

        {captchaRequired && <Captcha {...captcha} />}

        {serverError && (
          <p id={errorId} role="alert" aria-live="assertive" className="text-sm" style={{ color: "var(--danger)" }}>
            {serverError}
          </p>
        )}

        <button
          type="submit"
          disabled={disabled || (captchaRequired && !captchaCode)}
          className="oidc-sci-fi-btn mt-3 w-full flex items-center justify-center gap-2"
        >
          {pending ? (
            <>
              <Loader size={16} className="oidc-spin-slow" />
              <span>Verifying…</span>
            </>
          ) : (
            <span>Continue</span>
          )}
        </button>
      </form>
    </Form>
  );
}

export default InvitationConfirmPassword;
