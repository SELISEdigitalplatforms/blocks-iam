import { z } from "zod";
import { PASSWORD_MAX_INPUT_LENGTH } from "@blocks-idp/authentication/utils/password-policy.util";
import type { CompiledPasswordPolicy } from "@blocks-idp/authentication/utils/password-policy.util";

export const activationFormDefaultValue = {
  firstname: "",
  lastname: "",
  password: "",
  confirmPassword: "",
};

const hasWhitespace = /\s/;
const noWhitespaceMessage = "Password must not contain spaces";

const buildPasswordSchema = (policy: CompiledPasswordPolicy | null, applyPolicy: boolean) =>
  z.string().max(PASSWORD_MAX_INPUT_LENGTH, "Password is too long").superRefine((value, ctx) => {
    if (value && hasWhitespace.test(value)) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, message: noWhitespaceMessage });
    }
    if (applyPolicy && policy && !policy.test(value)) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, message: policy.message });
    }
  })
  .transform((value) => value.trim());

// Invited users are created with no name; they supply it here. The backend fills the account
// name only when it is still empty, so a name typed here never overwrites an existing one.
const nameSchema = (label: string) => z.string().trim().min(1, `${label} is required`);

export const buildActivationFormSchema = (policy: CompiledPasswordPolicy | null) => z.object({
  firstname: nameSchema("First name"),
  lastname: nameSchema("Last name"),
  password: buildPasswordSchema(policy, true),
  confirmPassword: buildPasswordSchema(policy, false),
});

export const activationFormSchema = buildActivationFormSchema(null);
