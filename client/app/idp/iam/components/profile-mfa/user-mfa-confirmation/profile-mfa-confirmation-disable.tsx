import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useAuthStore } from "@/store/useAuthStore";
import { getDisableMfaErrorMessage } from "@blocks-idp/mfa/utils/mfa-disable-errors";
import { useDisableMfa, useGenerateUserMfaOTP } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { useContext, useState } from "react";
import { useT } from "@seliseblocks/genesis-os/i18n";
import { profileMfaContext } from "../profile-mfa";
import { DisableMfaFormValues, ProfileMfaDisableForm } from "./profile-mfa-disable-form";

const TOTP_MFA_TYPE = 1;

export const UserMFAConfirmationDisable = ({ mfaType }: Readonly<{ mfaType: number }>) => {
  const { t } = useT("iam.profile");
  const { userId, isDisableModalOpen, setIsDisableModalOpen } = useContext(profileMfaContext);
  const [mfaId, setMfaId] = useState("");
  const { isPending: isDisabling, mutateAsync: disableAsync } = useDisableMfa({ id: userId });
  const { isPending: isGenerating, mutateAsync: generateAsync } = useGenerateUserMfaOTP();

  const closeDialog = () => {
    setMfaId("");
    setIsDisableModalOpen(false);
  };

  const requestCode = async () => {
    try {
      const res = await generateAsync({ userId, mfaType });
      if (res?.mfaId) setMfaId(res.mfaId);
      else showErrorToast({ errors: res?.errors ?? t("PROFILE_MFA_CONFIRMATION_DISABLE.COULD_NOT_SEND") });
    } catch (error) {
      showErrorToast({ errors: getDisableMfaErrorMessage(error) });
    }
  };

  const disable = async ({ password, code }: DisableMfaFormValues) => {
    try {
      const res = await disableAsync({ password, mfaId, verificationCode: code });
      if (res?.isSuccess === false) {
        showErrorToast({ errors: res.errors });
        return;
      }
      showSuccessToast({ description: t("PROFILE_MFA_CONFIRMATION_DISABLE.DISABLED_SIGN_IN_AGAIN") });
      // The server ends all sessions, so the current one is no longer valid.
      useAuthStore.getState().reset();
      globalThis.location.assign("/login");
    } catch (error) {
      showErrorToast({ errors: getDisableMfaErrorMessage(error) });
    }
  };

  return (
    <Dialog open={isDisableModalOpen} onOpenChange={(open) => !open && closeDialog()}>
      <DialogContent aria-describedby={undefined}>
        <DialogHeader>
          <DialogTitle>{t("PROFILE_MFA_CONFIRMATION_DISABLE.TITLE")}</DialogTitle>
          <DialogDescription>
            {mfaId
              ? t("PROFILE_MFA_CONFIRMATION_DISABLE.DESCRIPTION_VERIFY")
              : t("PROFILE_MFA_CONFIRMATION_DISABLE.DESCRIPTION_CONFIRM")}
          </DialogDescription>
        </DialogHeader>
        {mfaId ? (
          <ProfileMfaDisableForm
            isPending={isDisabling}
            isTotp={mfaType === TOTP_MFA_TYPE}
            onSubmit={disable}
            onCancel={closeDialog}
          />
        ) : (
          <DialogFooter className="mt-4 flex flex-col-reverse items-stretch gap-2 sm:flex-row sm:items-center sm:justify-end">
            <Button variant="outline" size="sm" disabled={isGenerating} onClick={closeDialog} className="w-full sm:w-auto">
              {t("common:CANCEL")}
            </Button>
            <Button size="sm" onClick={requestCode} disabled={isGenerating} className="w-full sm:w-auto">
              {isGenerating ? t("PROFILE_MFA_CONFIRMATION_DISABLE.PROCESSING") : t("common:YES")}
            </Button>
          </DialogFooter>
        )}
      </DialogContent>
    </Dialog>
  );
};
