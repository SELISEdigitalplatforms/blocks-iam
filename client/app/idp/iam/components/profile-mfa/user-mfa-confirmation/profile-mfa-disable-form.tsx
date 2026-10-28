import { Button } from "@/components/ui-kits/button/button";
import { DialogFooter } from "@/components/ui-kits/dialog/dialog";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/components/ui-kits/input-otp/input-otp";
import { PasswordInput } from "@/components/password-input";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";

const FormSchema = z.object({
  password: z.string().min(1, "Enter your password"),
  code: z.string().min(6, "Enter the 6-digit code"),
});

export type DisableMfaFormValues = z.infer<typeof FormSchema>;

type Props = {
  isPending: boolean;
  isTotp: boolean;
  onSubmit: (values: DisableMfaFormValues) => void;
  onCancel: () => void;
};

export const ProfileMfaDisableForm = ({ isPending, isTotp, onSubmit, onCancel }: Props) => {
  const form = useForm<DisableMfaFormValues>({
    resolver: zodResolver(FormSchema),
    defaultValues: { password: "", code: "" },
  });

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4">
        <FormField
          control={form.control}
          name="password"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Password</FormLabel>
              <FormControl>
                <PasswordInput
                  placeholder="Enter your password"
                  autoComplete="current-password"
                  {...field}
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="code"
          render={({ field }) => (
            <FormItem>
              <FormLabel>
                {isTotp ? "Code from your authenticator app" : "Code sent to your email"}
              </FormLabel>
              <FormControl>
                <InputOTP maxLength={6} {...field}>
                  <InputOTPGroup className="w-full justify-center gap-2">
                    {[0, 1, 2, 3, 4, 5].map((index) => (
                      <InputOTPSlot key={index} index={index} />
                    ))}
                  </InputOTPGroup>
                </InputOTP>
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <DialogFooter className="mt-4 flex flex-col-reverse items-stretch gap-2 sm:flex-row sm:items-center sm:justify-end">
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={isPending}
            onClick={onCancel}
            className="w-full sm:w-auto"
          >
            Cancel
          </Button>
          <Button type="submit" size="sm" disabled={isPending} className="w-full sm:w-auto">
            {isPending ? "Processing" : "Disable MFA"}
          </Button>
        </DialogFooter>
      </form>
    </Form>
  );
};
