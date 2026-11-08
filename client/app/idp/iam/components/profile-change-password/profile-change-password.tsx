import { useMemo, useState } from "react";
import { useT } from "@seliseblocks/genesis-os/i18n";
import { useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { KeyRound, Loader } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { PasswordInput } from "@/components/password-input/password-input";
import { PasswordStrengthChecker } from "@blocks-idp/authentication/components/password-strength-checker/password-strength-checker";
import { useChangePassword } from "@blocks-idp/iam/hooks/use-account";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { firstErrorText, isErrorWithErrors } from "@/lib/error";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import {
  PASSWORD_MAX_INPUT_LENGTH,
} from "@blocks-idp/authentication/utils/password-policy.util";
import {
  buildChangePasswordSchema,
  type ChangePasswordFormType,
} from "./change-password-schema";

const defaultValues: ChangePasswordFormType = {
  oldPassword: "",
  newPassword: "",
  confirmNewPassword: "",
};

interface ChangePasswordDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const ChangePasswordDialog = ({ open, onOpenChange }: ChangePasswordDialogProps) => {
  const { t } = useT("iam.profile");
  const [passwordRequirementsMet, setPasswordRequirementsMet] = useState(false);
  const { mutateAsync, isPending } = useChangePassword();
  const { passwordPolicy, isLoading: isConfigLoading } = useOidcUiConfig();
  const changePasswordSchema = useMemo(
    () => buildChangePasswordSchema(passwordPolicy),
    [passwordPolicy],
  );

  const form = useForm<ChangePasswordFormType>({
    defaultValues,
    resolver: zodResolver(changePasswordSchema),
  });

  const [newPassword, confirmNewPassword, oldPassword] = useWatch({
    control: form.control,
    name: ["newPassword", "confirmNewPassword", "oldPassword"],
  });

  const onClose = () => {
    form.reset();
    setPasswordRequirementsMet(false);
    onOpenChange(false);
  };

  const onSubmit = async (values: ChangePasswordFormType) => {
    try {
      await mutateAsync(values);
      showSuccessToast({ title: t("PROFILE_CHANGE_PASSWORD.PASSWORD_UPDATED"), description: t("PROFILE_CHANGE_PASSWORD.PASSWORD_CHANGED") });
      onClose();
    } catch (error) {
      const message = isErrorWithErrors(error)
        ? firstErrorText(error.errors, t("PROFILE_CHANGE_PASSWORD.UPDATE_FAILED"))
        : t("PROFILE_CHANGE_PASSWORD.CHECK_CURRENT_PASSWORD");
      showErrorToast({ title: t("PROFILE_CHANGE_PASSWORD.UPDATE_FAILED"), errors: message });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[460px]">
        <DialogHeader>
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10">
              <KeyRound className="h-5 w-5 text-primary" />
            </div>
            <div>
              <DialogTitle>{t("PROFILE_CHANGE_PASSWORD.CHANGE_PASSWORD")}</DialogTitle>
              <DialogDescription className="mt-0.5">
                {t("PROFILE_CHANGE_PASSWORD.CHOOSE_STRONG")}
              </DialogDescription>
            </div>
          </div>
        </DialogHeader>

        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmit)} className="flex flex-col gap-5 pt-1">
            <FormField
              control={form.control}
              name="oldPassword"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("PROFILE_CHANGE_PASSWORD.CURRENT_PASSWORD")}</FormLabel>
                  <FormControl>
                    <PasswordInput maxLength={PASSWORD_MAX_INPUT_LENGTH} placeholder={t("PROFILE_CHANGE_PASSWORD.CURRENT_PASSWORD_PLACEHOLDER")} {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="newPassword"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("PROFILE_CHANGE_PASSWORD.NEW_PASSWORD")}</FormLabel>
                  <FormControl>
                    <PasswordInput maxLength={PASSWORD_MAX_INPUT_LENGTH} placeholder={t("PROFILE_CHANGE_PASSWORD.NEW_PASSWORD_PLACEHOLDER")} {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="confirmNewPassword"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("PROFILE_CHANGE_PASSWORD.CONFIRM_NEW_PASSWORD")}</FormLabel>
                  <FormControl>
                    <PasswordInput maxLength={PASSWORD_MAX_INPUT_LENGTH} placeholder={t("PROFILE_CHANGE_PASSWORD.CONFIRM_NEW_PASSWORD_PLACEHOLDER")} {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            {newPassword && (
              <PasswordStrengthChecker
                password={newPassword}
                confirmPassword={confirmNewPassword}
                policy={passwordPolicy}
                excludePassword={oldPassword}
                onRequirementsMet={setPasswordRequirementsMet}
              />
            )}

            <DialogFooter>
              <DialogTrigger asChild>
                <Button variant="outline" size="sm" disabled={isPending} onClick={onClose}>
                  {t("common:CANCEL")}
                </Button>
              </DialogTrigger>
              <Button type="submit" size="sm" disabled={isConfigLoading || isPending || !passwordRequirementsMet}>
                {isPending && <Loader className="mr-1.5 h-3.5 w-3.5 animate-spin" />}
                {isPending ? t("PROFILE_CHANGE_PASSWORD.SAVING") : t("PROFILE_CHANGE_PASSWORD.SAVE_CHANGES")}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};

export const ProfileChangePassword = () => {
  const { t } = useT("iam.profile");
  const [open, setOpen] = useState(false);

  return (
    <Card className="">
      <CardHeader className="mb-3">
        <div className="flex items-center justify-between">
          <CardTitle>{t("PROFILE_CHANGE_PASSWORD.CHANGE_PASSWORD")}</CardTitle>
          <Button size="sm" variant="outline" onClick={() => setOpen(true)}>
            {t("PROFILE_CHANGE_PASSWORD.UPDATE_PASSWORD")}
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <p className="text-sm text-muted-foreground">
          {t("PROFILE_CHANGE_PASSWORD.UPDATE_REGULARLY")}
        </p>
      </CardContent>
      <ChangePasswordDialog open={open} onOpenChange={setOpen} />
    </Card>
  );
};
