import { z } from "zod";
import {
  PASSWORD_MAX_INPUT_LENGTH,
  type CompiledPasswordPolicy,
} from "@blocks-idp/authentication/utils/password-policy.util";

export const buildResetPasswordFormSchema = (policy: CompiledPasswordPolicy | null) => {
  const basePassword = z
    .string()
    .min(1, "Password is required")
    .max(PASSWORD_MAX_INPUT_LENGTH, "Password is too long");
  const password = policy ? basePassword.refine(policy.test, policy.message) : basePassword;

  return z.object({
    password,
    confirmPassword: z.string(),
    logoutFromAllDevices: z.boolean().optional(),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: "Passwords must be matched",
    path: ["confirmPassword"],
  });
};

export const resetPasswordFormSchema = buildResetPasswordFormSchema(null);

export type ResetPasswordFormValuesType = z.infer<
  typeof resetPasswordFormSchema
>;

export const resetPasswordFormDefaultValue: ResetPasswordFormValuesType = {
  password: "",
  confirmPassword: "",
  logoutFromAllDevices: true,
};
