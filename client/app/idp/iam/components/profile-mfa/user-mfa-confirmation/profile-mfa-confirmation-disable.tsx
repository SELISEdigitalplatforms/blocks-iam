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
import { profileMfaContext } from "../profile-mfa";
import { DisableMfaFormValues, ProfileMfaDisableForm } from "./profile-mfa-disable-form";

const TOTP_MFA_TYPE = 1;

export const UserMFAConfirmationDisable = ({ mfaType }: Readonly<{ mfaType: number }>) => {
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
      else showErrorToast({ errors: res?.errors ?? "Could not send the code" });
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
      showSuccessToast({ description: "MFA disabled. Please sign in again." });
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
          <DialogTitle>Disable MFA?</DialogTitle>
          <DialogDescription>
            {mfaId
              ? "Enter your password and the verification code. You will be signed out on all devices."
              : "Are you sure you want to disable Multi-Factor Authentication (MFA) for this account? Disabling MFA may reduce the security of this account."}
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
              Cancel
            </Button>
            <Button size="sm" onClick={requestCode} disabled={isGenerating} className="w-full sm:w-auto">
              {isGenerating ? "Processing" : "Yes"}
            </Button>
          </DialogFooter>
        )}
      </DialogContent>
    </Dialog>
  );
};
