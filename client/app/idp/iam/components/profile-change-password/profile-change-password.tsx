import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
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
import { isErrorWithErrors } from "@/lib/error";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";

const changePasswordSchema = z
  .object({
    oldPassword: z.string().min(1, "Current password is required"),
    newPassword: z.string().min(8, "Password must be at least 8 characters"),
    confirmNewPassword: z.string().min(8, "Password must be at least 8 characters"),
  })
  .refine((data) => data.newPassword === data.confirmNewPassword, {
    message: "Passwords must match",
    path: ["confirmNewPassword"],
  });

type ChangePasswordFormType = z.infer<typeof changePasswordSchema>;

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
  const [passwordRequirementsMet, setPasswordRequirementsMet] = useState(false);
  const { mutateAsync, isPending } = useChangePassword();

  const form = useForm<ChangePasswordFormType>({
    defaultValues,
    resolver: zodResolver(changePasswordSchema),
  });

  const newPassword = form.watch("newPassword");
  const confirmNewPassword = form.watch("confirmNewPassword");
  const oldPassword = form.watch("oldPassword");

  const onClose = () => {
    form.reset();
    setPasswordRequirementsMet(false);
    onOpenChange(false);
  };

  const onSubmit = async (values: ChangePasswordFormType) => {
    try {
      await mutateAsync(values);
      showSuccessToast({ title: "Password updated", description: "Your password has been changed successfully." });
      onClose();
    } catch (error) {
      const message = isErrorWithErrors(error)
        ? Array.isArray(error.errors)
          ? (error.errors[0] as string)
          : typeof error.errors === "object"
          ? (Object.values(error.errors as Record<string, string>)[0] ?? "Update failed")
          : (error.errors as string)
        : "Please check your current password and try again.";
      showErrorToast({ title: "Update failed", errors: message });
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
              <DialogTitle>Change Password</DialogTitle>
              <DialogDescription className="mt-0.5">
                Choose a strong password you don't use elsewhere.
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
                  <FormLabel>Current Password</FormLabel>
                  <FormControl>
                    <PasswordInput placeholder="Enter your current password" {...field} />
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
                  <FormLabel>New Password</FormLabel>
                  <FormControl>
                    <PasswordInput placeholder="Enter your new password" {...field} />
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
                  <FormLabel>Confirm New Password</FormLabel>
                  <FormControl>
                    <PasswordInput placeholder="Confirm your new password" {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            {newPassword && (
              <PasswordStrengthChecker
                password={newPassword}
                confirmPassword={confirmNewPassword}
                excludePassword={oldPassword}
                onRequirementsMet={setPasswordRequirementsMet}
              />
            )}

            <DialogFooter>
              <DialogTrigger asChild>
                <Button variant="outline" size="sm" disabled={isPending} onClick={onClose}>
                  Cancel
                </Button>
              </DialogTrigger>
              <Button type="submit" size="sm" disabled={isPending || !passwordRequirementsMet}>
                {isPending && <Loader className="mr-1.5 h-3.5 w-3.5 animate-spin" />}
                {isPending ? "Saving…" : "Save Changes"}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};

export const ProfileChangePassword = () => {
  const [open, setOpen] = useState(false);

  return (
    <Card>
      <CardHeader className="mb-3">
        <div className="flex items-center justify-between">
          <CardTitle>Change Password</CardTitle>
          <Button size="sm" variant="outline" onClick={() => setOpen(true)}>
            Update Password
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <p className="text-sm text-muted-foreground">
          Update your password regularly to keep your account secure.
        </p>
      </CardContent>
      <ChangePasswordDialog open={open} onOpenChange={setOpen} />
    </Card>
  );
};