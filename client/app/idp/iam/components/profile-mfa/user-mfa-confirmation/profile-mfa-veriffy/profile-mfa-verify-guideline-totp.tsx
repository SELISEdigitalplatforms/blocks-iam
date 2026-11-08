import { useGetTotp } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { useContext } from "react";
import { useT } from "@seliseblocks/genesis-os/i18n";
import { profileMfaContext } from "../../profile-mfa";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

export const ProfileMfaVerifyGuideLineTotp = () => {
  const { t } = useT("iam.profile");
  const { userId } = useContext(profileMfaContext);
  const { data } = useGetTotp({ id: userId });
  return (
    <div className="flex flex-col gap-4">
      <div className="flex gap-1">
        <span>1.</span>
        <span>
          {t("PROFILE_MFA_GUIDELINE_TOTP.SCAN_QR")}
        </span>
      </div>
      <div className="flex flex-col items-center gap-4">
        <div className="relative aspect-square w-40 rounded-sm border p-2">
          {data?.qrImageUrl ? (
            <img alt={t("PROFILE_MFA_GUIDELINE_TOTP.QR_ALT")} src={data?.qrImageUrl} />
          ) : (
            <Skeleton />
          )}
        </div>
        <div className="text-center">
          <p className="text-medium-emphasis">{t("PROFILE_MFA_GUIDELINE_TOTP.ENTER_MANUALLY")}</p>
          <p className="mt-2 font-semibold">{data?.secret ?? data?.qrCode}</p>
        </div>
      </div>
      <div className="flex gap-1">
        <span>2.</span>
        <span>{t("PROFILE_MFA_GUIDELINE_TOTP.VERIFY_PAIRING")}</span>
      </div>
    </div>
  );
};
