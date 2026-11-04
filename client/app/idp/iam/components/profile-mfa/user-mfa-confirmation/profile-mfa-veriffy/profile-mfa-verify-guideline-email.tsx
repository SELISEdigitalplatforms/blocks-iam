import { useContext } from "react";
import { useT } from "@seliseblocks/genesis-os/i18n";
import { profileMfaContext } from "../../profile-mfa";
import { useGetMe, useGetUserById } from "@blocks-idp/iam/hooks/use-user";

const imageUrl = "/assets/images/mail-sent.png";
import { Button } from "@/components/ui-kits/button/button";
import { useResendOtp } from "@blocks-idp/mfa/hooks/use-resend-otp";

export const ProfileMfaVerifyGuideLineEmail = ({ mfaId }: { mfaId: string }) => {
  const { t } = useT("iam.profile");
  const { userId, projectKey, own } = useContext(profileMfaContext);
  const { data: userByIdData } = useGetUserById(
    { id: userId, projectKey },
    { enabled: !own },
  );
  const { data: meData } = useGetMe();
  const data = own ? meData : userByIdData;
  const { remainingTime, resend } = useResendOtp({ mfaId });

  const resendButtonLabel = remainingTime
    ? t("PROFILE_MFA_GUIDELINE_EMAIL.RESEND_IN", { time: `${Math.floor(remainingTime / 60)}:${String(remainingTime % 60).padStart(2, "0")}` })
    : t("PROFILE_MFA_GUIDELINE_EMAIL.RESEND");

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-center">
        <img src={imageUrl} alt={t("PROFILE_MFA_GUIDELINE_EMAIL.EMAIL_SENT_ALT")} width="120" height="100" />
      </div>
      <div className="mt-6">
        <h3 className="text-2xl font-bold">{t("PROFILE_MFA_GUIDELINE_EMAIL.EMAIL_SENT")}</h3>
        <p className="mt-2">
          {t("PROFILE_MFA_GUIDELINE_EMAIL.SENT_TO", { email: data?.data.email ?? "" })}{" "}
        </p>
      </div>
      <p>
        {t("PROFILE_MFA_GUIDELINE_EMAIL.DID_NOT_RECEIVE")}{" "}
        <Button
          variant="link"
          className="p-0 text-sm font-medium no-underline!"
          disabled={!!remainingTime}
          onClick={resend}
        >
          {resendButtonLabel}
        </Button>
      </p>
      <p>{t("PROFILE_MFA_GUIDELINE_EMAIL.ENTER_KEY")} </p>
    </div>
  );
};
