import { z } from "zod";

export const activationFormDefaultValue = {
  firstname: "",
  lastname: "",
  password: "",
  confirmPassword: "",
};

const hasWhitespace = /\s/;
const noWhitespaceMessage = "Password must not contain spaces";

const passwordSchema = z
  .string()
  .superRefine((value, ctx) => {
    if (value && hasWhitespace.test(value)) {
      ctx.addIssue({ code: z.ZodIssueCode.custom, message: noWhitespaceMessage });
    }
  })
  .transform((value) => value.trim());

// Invited users are created with no name; they supply it here. The backend fills the account
// name only when it is still empty, so a name typed here never overwrites an existing one.
const nameSchema = (label: string) => z.string().trim().min(1, `${label} is required`);

export const activationFormSchema = z.object({
  firstname: nameSchema("First name"),
  lastname: nameSchema("Last name"),
  password: passwordSchema,
  confirmPassword: passwordSchema,
});