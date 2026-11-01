import { tStatic } from "@seliseblocks/genesis-os/i18n";
import { z } from "zod";

export const signinFormDefaultValue = {
  username: "",
  password: "",
};

export const signinFormSchema = z.object({
  username: z.string().min(1, { error: () => tStatic("iam.login", "SIGNIN_SCHEMA.EMAIL_REQUIRED") }).email({ error: () => tStatic("iam.login", "SIGNIN_SCHEMA.EMAIL_INVALID") }),
  password: z.string().min(1, { error: () => tStatic("iam.login", "SIGNIN_SCHEMA.PASSWORD_REQUIRED") }),
});
