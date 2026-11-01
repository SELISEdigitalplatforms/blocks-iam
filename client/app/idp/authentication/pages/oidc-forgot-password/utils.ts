import { tStatic } from "@seliseblocks/genesis-os/i18n";
import { z } from "zod";

export const forgotPasswordFormDefaultValue = {
  email: "",
};

export const forgotPasswordFormSchema = z.object({
  email: z.string().trim().pipe(z.email({ error: () => tStatic("iam.password", "FORGOT_PASSWORD_SCHEMA.INVALID_EMAIL") })),
});
