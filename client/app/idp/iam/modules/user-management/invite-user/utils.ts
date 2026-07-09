import { z } from "zod";

export const inviteUserFormDefaultValue = {
  email: "",
  firstName: "",
  lastName: "",
  organizationIds: [] as string[],
  roles: [] as { slug: string }[],
  permissions: [] as { resource: string }[],
};

export const inviteUserFormSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, "Email is required")
    .email({ message: "Please enter a valid email address" }),
  firstName: z.string().trim().max(150, "First name must be at most 150 characters").optional(),
  lastName: z.string().trim().max(150, "Last name must be at most 150 characters").optional(),
  organizationIds: z
    .array(z.string().min(1))
    .min(1, "Please select an organization"),
  roles: z.array(z.object({ slug: z.string() })),
  permissions: z.array(z.object({ resource: z.string() })),
});