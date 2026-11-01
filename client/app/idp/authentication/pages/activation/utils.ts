import { tStatic } from "@seliseblocks/genesis-os/i18n";
import { z } from "zod";
import {
  applyPasswordPolicyToSchema,
  PASSWORD_MAX_INPUT_LENGTH,
} from "@blocks-idp/authentication/utils/password-policy.util";
import type { IOidcPasswordPolicy } from "@blocks-idp/authentication/utils/password-policy.util";

const IAM_ACTIVATION_MODULE = "iam.activation";

export const activationFormDefaultValue = {
  firstname: "",
  lastname: "",
  password: "",
  confirmPassword: "",
};

const hasWhitespace = /\s/;

const buildPasswordSchema = (
  policy: IOidcPasswordPolicy | null | undefined,
  applyPolicy: boolean,
) => {
  const base = z.string().max(PASSWORD_MAX_INPUT_LENGTH, { error: () => tStatic(IAM_ACTIVATION_MODULE, "UTILS.PASSWORD_TOO_LONG") });
  const withPolicy = applyPolicy ? applyPasswordPolicyToSchema(base, policy) : base;

  return withPolicy
    .superRefine((value, ctx) => {
      if (value && hasWhitespace.test(value)) {
        ctx.addIssue({ code: "custom", message: tStatic(IAM_ACTIVATION_MODULE, "UTILS.PASSWORD_NO_SPACES") });
      }
    })
    .transform((value) => value.trim());
};

// Invited users are created with no name; they supply it here. The backend fills the account
// name only when it is still empty, so a name typed here never overwrites an existing one.
const nameSchema = (key: "UTILS.FIRST_NAME_REQUIRED" | "UTILS.LAST_NAME_REQUIRED") =>
  z.string().trim().min(1, { error: () => tStatic(IAM_ACTIVATION_MODULE, key) });

export const buildActivationFormSchema = (policy: IOidcPasswordPolicy | null | undefined) => z.object({
  firstname: nameSchema("UTILS.FIRST_NAME_REQUIRED"),
  lastname: nameSchema("UTILS.LAST_NAME_REQUIRED"),
  password: buildPasswordSchema(policy, true),
  confirmPassword: buildPasswordSchema(policy, false),
});

export const activationFormSchema = buildActivationFormSchema(null);
