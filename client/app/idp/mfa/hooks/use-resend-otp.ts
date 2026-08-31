import { useCountDown } from "@/hooks/use-count-down";
import { useResendMfaOTP } from "./use-mfa-config";
import { useCallback } from "react";

type ResendOtpProps = {
  mfaId: string;
};

// Must match the backend resend cooldown (MFA challenge resend is rejected with 429 until
// this many seconds have elapsed since the last send). Keep the two in sync.
const RESEND_COOLDOWN_SECONDS = 60;

export const useResendOtp = ({ mfaId }: ResendOtpProps) => {
  const { remainingTime, reset } = useCountDown(RESEND_COOLDOWN_SECONDS);
  const { mutateAsync } = useResendMfaOTP();

  const resend = useCallback(async () => {
    try {
      await mutateAsync({ mfaId });
      reset();
    } catch (error) {
      console.log(error);
    }
  }, [mfaId, mutateAsync, reset]);

  return { remainingTime, reset, resend };
};
