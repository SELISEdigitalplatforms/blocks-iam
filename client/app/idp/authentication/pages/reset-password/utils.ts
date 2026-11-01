import { tStatic } from "@seliseblocks/genesis-os/i18n";
import { z } from "zod";
import {
  applyPasswordPolicyToSchema,
  PASSWORD_MAX_INPUT_LENGTH,
  type IOidcPasswordPolicy,
} from "@blocks-idp/authentication/utils/password-policy.util";

export const buildResetPasswordFormSchema = (policy: IOidcPasswordPolicy | null | undefined) => {
  const basePassword = z
    .string()
    .min(1, { error: () => tStatic("iam.password", "RESET_PASSWORD_SCHEMA.PASSWORD_REQUIRED") })
    .max(PASSWORD_MAX_INPUT_LENGTH, { error: () => tStatic("iam.password", "RESET_PASSWORD_SCHEMA.PASSWORD_TOO_LONG") });
  const password = applyPasswordPolicyToSchema(basePassword, policy);

  return z.object({
    password,
    confirmPassword: z.string(),
    logoutFromAllDevices: z.boolean().optional(),
  })
  .refine((data) => data.password === data.confirmPassword, {
    error: () => tStatic("iam.password", "RESET_PASSWORD_SCHEMA.PASSWORDS_MUST_MATCH"),
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
