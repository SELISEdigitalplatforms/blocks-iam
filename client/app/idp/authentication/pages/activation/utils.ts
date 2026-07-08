import { z } from "zod";

export const activationFormDefaultValue = {
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

export const activationFormSchema = z.object({
  password: passwordSchema,
  confirmPassword: passwordSchema,
});

