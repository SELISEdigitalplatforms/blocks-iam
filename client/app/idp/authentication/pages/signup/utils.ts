import { z } from "zod";

export const signupFormDefaultValue = {
  firstName: "",
  lastName: "",
  email: "",
  organizationName: "",
};

export const signupFormSchema = z.object({
  firstName: z.string().trim().min(1, { message: "First name is required" }),
  lastName: z.string().trim().min(1, { message: "Last name is required" }),
  email: z.string().trim().email({ message: "Invalid email" }),
  organizationName: z.string().trim().optional(),
});

/**
 * The organization name is only collected when the tenant allows org creation
 * from signup, so the requirement has to be applied at render time rather than
 * baked into the base schema.
 */
export const buildSignupFormSchema = (organizationNameRequired: boolean) =>
  organizationNameRequired
    ? signupFormSchema.extend({
        organizationName: z
          .string()
          .trim()
          .min(1, { message: "Organization name is required" }),
      })
    : signupFormSchema;
