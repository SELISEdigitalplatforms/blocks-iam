
import { Button } from "@/components/ui-kits/button/button";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/components/ui-kits/input-otp/input-otp";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useAuthStore } from "@seliseblocks/genesis-os";
import { useResendOtp } from "@blocks-idp/mfa/hooks/use-resend-otp";
import { AUTH_ENDPOINTS } from "@blocks-idp/authentication/constants/endpoint.constant";
import { useOidcAuthAnimation } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { zodResolver } from "@hookform/resolvers/zod";
import { useNavigate } from "react-router";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";
import { ArrowRight, Loader, RotateCcw } from "lucide-react";
import { useState } from "react";

import { useForm } from "react-hook-form";
import { z } from "zod";

const CustomInputOTPSlot = ({ index }: { index: number }) => {
  return (
    <InputOTPSlot
      index={index}
      className="oidc-sci-fi-otp-slot h-12 w-[46px] rounded-sm first:rounded-l-sm last:rounded-r-sm"
    />
  );
};
const getFormSchema = (type: number) =>
  z.object({
    code: z.string().min(type === 2 ? 5 : 6),
  });
export const MfaCheckFrom = () => {
  const navigate = useNavigate();
  const animCtx = useOidcAuthAnimation();
  const [{ mfa_id, mfa_type }] = useQueryStates({
    mfa_id: parseAsString.withDefault(""),
    mfa_type: parseAsInteger.withDefault(0),
  });
  const [isLoading, setIsLoading] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const { setAuthenticated } = useAuthStore();
  const { remainingTime, resend } = useResendOtp({ mfaId: mfa_id });

  const form = useForm<{ code: string }>({
    resolver: zodResolver(getFormSchema(mfa_type)),
    defaultValues: {
      code: "",
    },
  });

  const isAuthenticating =
    isLoading ||
    animCtx?.phase === "submitting" ||
    animCtx?.phase === "succeeded";

  const submitHandler = async ({ code }: { code: string }) => {
    setIsLoading(true);
    setServerError(null);
    animCtx?.startAnimation();

    try {
      const finalTenantId =
        getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || undefined;

      const payload = {
        mfa_id,
        mfa_code: code,
        tenant_id: finalTenantId || "",
      };

      const response = await fetch(AUTH_ENDPOINTS.OIDC_LOGIN, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Blocks-Key": finalTenantId || "",
        },
        body: JSON.stringify(payload),
      });

      const contentType = response.headers.get("content-type");
      const data = contentType?.includes("application/json")
        ? await response.json()
        : null;

      if (response.ok) {
        setAuthenticated();
        const redirectUrl =
          data?.redirect_uri || data?.redirect_url || data?.redirectUrl;
        if (redirectUrl) {
          await animCtx?.succeedAnimation();
          window.location.href = redirectUrl;
          return;
        }
        await animCtx?.succeedAnimation();
        navigate("/app/console");
        return;
      }

      const errorCode = data?.error;
      const errorMsg =
        data?.error_description || data?.message || "Verification failed";

      if (errorCode === "account_locked") {
        const msg =
          "Your account is locked. Please contact support or reset your password.";
        setServerError(msg);
        await animCtx?.failAnimation(msg);
      } else if (errorCode === "invalid_mfa_code") {
        const msg = "Invalid verification code. Please try again.";
        setServerError(msg);
        await animCtx?.failAnimation(msg);
        form.reset();
      } else {
        setServerError(errorMsg);
        await animCtx?.failAnimation(errorMsg);
      }
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({
          errors: error.errors.error_description || "Something went wrong",
        });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    } finally {
      setIsLoading(false);
    }
  };

  const { isValid } = form.formState;

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(submitHandler)} className="flex flex-col gap-5">
        <FormField
          control={form.control}
          name="code"
          render={({ field }) => (
            <FormItem>
              <FormControl>
                <InputOTP maxLength={mfa_type === 1 ? 6 : 5} {...field}>
                  <InputOTPGroup className="w-full justify-between gap-6">
                    <CustomInputOTPSlot index={0} />
                    <CustomInputOTPSlot index={1} />
                    <CustomInputOTPSlot index={2} />
                    <CustomInputOTPSlot index={3} />
                    <CustomInputOTPSlot index={4} />
                    {mfa_type === 1 && <CustomInputOTPSlot index={5} />}
                  </InputOTPGroup>
                </InputOTP>
              </FormControl>
              <FormMessage className="text-xs text-[var(--danger)]" />
            </FormItem>
          )}
        />

        {mfa_type === 2 && (
          <div className="flex items-center justify-end text-sm">
            <Button
              type="button"
              variant="link"
              className="oidc-sci-fi-link flex items-center gap-1.5 p-0 text-sm font-medium !no-underline"
              onClick={resend}
              disabled={!!remainingTime}
            >
              <RotateCcw size={14} />
              Resend Code
              {remainingTime > 0 &&
                ` (${Math.floor(remainingTime / 60)}:${String(remainingTime % 60).padStart(2, "0")})`}
            </Button>
          </div>
        )}

        {serverError && (
          <p
            className="text-sm"
            style={{
              color: "var(--danger)",
              fontFamily: "system-ui, sans-serif",
            }}
          >
            {serverError}
          </p>
        )}

        <button
          type="submit"
          className="oidc-sci-fi-btn w-full flex items-center justify-center gap-2"
          disabled={!isValid || isAuthenticating}
        >
          {isAuthenticating ? (
            <>
              <Loader size={16} className="oidc-spin-slow" />
              <span>Verifying...</span>
            </>
          ) : (
            <>
              <span>Verify</span>
              <ArrowRight size={16} />
            </>
          )}
        </button>
      </form>
    </Form>
  );
};
