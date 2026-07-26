import { z } from "zod";
import {
  PASSWORD_COMPLEXITY_MESSAGE,
  PASSWORD_COMPLEXITY_REGEX,
} from "@blocks-idp/authentication/utils/password-strength.util";

export const resetPasswordFormSchema = z
  .object({
    password: z
      .string()
      .min(8, "Password must be at least 8 characters long")
      .max(30, "Password must be at most 30 characters long")
      .regex(PASSWORD_COMPLEXITY_REGEX, PASSWORD_COMPLEXITY_MESSAGE),
    confirmPassword: z
      .string()
      .min(8, "Confirm password must be at least 8 characters long"),
    logoutFromAllDevices: z.boolean().optional(),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: "Passwords must be matched",
    path: ["confirmPassword"],
  });

export type ResetPasswordFormValuesType = z.infer<
  typeof resetPasswordFormSchema
>;

export const resetPasswordFormDefaultValue: ResetPasswordFormValuesType = {
  password: "",
  confirmPassword: "",
  logoutFromAllDevices: true,
};
