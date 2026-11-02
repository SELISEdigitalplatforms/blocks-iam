import { useCountDown } from "@/hooks/use-count-down";
import { isErrorWithErrors } from "@/lib/error";
import { useResendMfaOTP } from "./use-mfa-config";
import { useCallback } from "react";

type ResendOtpProps = {
  mfaId: string;
  /** Tenant of the sign-in the challenge belongs to; omitted, the client's own key is used. */
  tenantId?: string;
  /** Called when the challenge behind mfaId is gone: no resend can revive it, only a new sign-in. */
  onExpired?: () => void;
};

// Must match the backend resend cooldown (MFA challenge resend is rejected with 429 until
// this many seconds have elapsed since the last send). Keep the two in sync.
const RESEND_COOLDOWN_SECONDS = 60;

// The resend answer for a challenge that expired (fixed lifetime, never extended by a resend) or
// was already used.
const EXPIRED_CHALLENGE = "invalid_two_factor_id";

export const useResendOtp = ({ mfaId, tenantId, onExpired }: ResendOtpProps) => {
  const { remainingTime, reset } = useCountDown(RESEND_COOLDOWN_SECONDS);
  const { mutateAsync } = useResendMfaOTP();

  const resend = useCallback(async () => {
    try {
      await mutateAsync(tenantId ? { mfaId, tenantId } : { mfaId });
      reset();
    } catch (error) {
      if (onExpired && isErrorWithErrors(error) && error.errors.message === EXPIRED_CHALLENGE) {
        onExpired();
        return;
      }
      console.error(error);
    }
  }, [mfaId, mutateAsync, onExpired, reset, tenantId]);

  return { remainingTime, reset, resend };
};
