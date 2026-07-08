import { z } from "zod";

export const inviteUserFormDefaultValue = {
  email: "",
  firstName: "",
  lastName: "",
  organizationId: "default",
};

export const inviteUserFormSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, "Email is required")
    .email({ message: "Please enter a valid email address" }),
  firstName: z.string().trim().max(150, "First name must be at most 150 characters").optional(),
  lastName: z.string().trim().max(150, "Last name must be at most 150 characters").optional(),
  organizationId: z.string().min(1, "Organization is required"),
});