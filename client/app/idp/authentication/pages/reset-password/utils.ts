import { z } from "zod";

const passwordRegex =
  /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&;[\]{}|:(),.])[A-Za-z\d@$!%*?&;[\]{}|:(),.]{8,30}$/;

export const resetPasswordFormSchema = z
  .object({
    password: z
      .string()
      .min(8, "Password must be at least 8 characters long")
      .regex(passwordRegex),
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
