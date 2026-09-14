import { z } from "zod";
import {
  PASSWORD_MAX_INPUT_LENGTH,
  type CompiledPasswordPolicy,
} from "@blocks-idp/authentication/utils/password-policy.util";

export const buildChangePasswordSchema = (policy: CompiledPasswordPolicy | null) => {
  const baseNewPassword = z
    .string()
    .min(1, "New password is required")
    .max(PASSWORD_MAX_INPUT_LENGTH, "Password is too long");
  const newPassword = policy
    ? baseNewPassword.refine(policy.test, policy.message)
    : baseNewPassword;

  return z
    .object({
      oldPassword: z
        .string()
        .min(1, "Current password is required")
        .max(PASSWORD_MAX_INPUT_LENGTH),
      newPassword,
      confirmNewPassword: z.string().max(PASSWORD_MAX_INPUT_LENGTH, "Password is too long"),
    })
    .refine((data) => data.newPassword === data.confirmNewPassword, {
      message: "Passwords must match",
      path: ["confirmNewPassword"],
    });
};

export type ChangePasswordFormType = z.infer<ReturnType<typeof buildChangePasswordSchema>>;
