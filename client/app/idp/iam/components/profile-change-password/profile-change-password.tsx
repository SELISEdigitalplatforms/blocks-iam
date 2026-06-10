import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Lock } from "lucide-react";
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
      <DialogContent className="rounded-md sm:max-w-[500px] overflow-y-auto max-h-screen">
        <DialogHeader>
          <DialogTitle>Update Password</DialogTitle>
          <DialogDescription>Secure your account with a new password.</DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmit)} className="flex flex-col gap-4">
            <div className="grid grid-cols-1 gap-4">
              <FormField
                control={form.control}
                name="oldPassword"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel className="text-sm font-normal">Current Password</FormLabel>
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
                    <FormLabel className="text-sm font-normal">New Password</FormLabel>
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
                    <FormLabel className="text-sm font-normal">Confirm New Password</FormLabel>
                    <FormControl>
                      <PasswordInput placeholder="Confirm your new password" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <PasswordStrengthChecker
                password={newPassword}
                confirmPassword={confirmNewPassword}
                excludePassword={oldPassword}
                onRequirementsMet={setPasswordRequirementsMet}
              />
            </div>
            <DialogFooter className="mt-2 flex justify-end gap-2">
              <DialogTrigger asChild>
                <Button variant="outline" disabled={isPending} onClick={onClose}>
                  Cancel
                </Button>
              </DialogTrigger>
              <Button type="submit" disabled={isPending || !passwordRequirementsMet}>
                Change
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
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle>Change Password</CardTitle>
          <Button
            size="sm"
            variant="outline"
            onClick={() => setOpen(true)}
          >
            <Lock className="w-4 h-4 mr-1.5" />
            Update Password
          </Button>
        </div>
      </CardHeader>
      <CardContent className="!pt-0">
        <p className="text-sm text-muted-foreground">
          Update your password to keep your account safe.
        </p>
      </CardContent>
      <ChangePasswordDialog open={open} onOpenChange={setOpen} />
    </Card>
  );
};
