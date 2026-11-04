import { tStatic } from "@seliseblocks/genesis-os/i18n";
import { z } from "zod";
import {
  applyPasswordPolicyToSchema,
  PASSWORD_MAX_INPUT_LENGTH,
  type IOidcPasswordPolicy,
} from "@blocks-idp/authentication/utils/password-policy.util";

const IAM_PROFILE_MODULE = "iam.profile";

export const buildChangePasswordSchema = (policy: IOidcPasswordPolicy | null | undefined) => {
  const baseNewPassword = z
    .string()
    .min(1, { error: () => tStatic(IAM_PROFILE_MODULE, "CHANGE_PASSWORD_SCHEMA.NEW_PASSWORD_REQUIRED") })
    .max(PASSWORD_MAX_INPUT_LENGTH, { error: () => tStatic(IAM_PROFILE_MODULE, "CHANGE_PASSWORD_SCHEMA.PASSWORD_TOO_LONG") });
  const newPassword = applyPasswordPolicyToSchema(baseNewPassword, policy);

  return z
    .object({
      oldPassword: z
        .string()
        .min(1, { error: () => tStatic(IAM_PROFILE_MODULE, "CHANGE_PASSWORD_SCHEMA.CURRENT_PASSWORD_REQUIRED") })
        .max(PASSWORD_MAX_INPUT_LENGTH),
      newPassword,
      confirmNewPassword: z.string().max(PASSWORD_MAX_INPUT_LENGTH, { error: () => tStatic(IAM_PROFILE_MODULE, "CHANGE_PASSWORD_SCHEMA.PASSWORD_TOO_LONG") }),
    })
    .refine((data) => data.newPassword === data.confirmNewPassword, {
      error: () => tStatic(IAM_PROFILE_MODULE, "CHANGE_PASSWORD_SCHEMA.PASSWORDS_MUST_MATCH"),
      path: ["confirmNewPassword"],
    });
};

export type ChangePasswordFormType = z.infer<ReturnType<typeof buildChangePasswordSchema>>;
