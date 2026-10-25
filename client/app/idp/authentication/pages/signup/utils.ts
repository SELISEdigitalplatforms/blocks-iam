import { z } from "zod";

export type SignupOrganizationChoice = "join" | "create";

export const signupFormDefaultValue = {
  firstName: "",
  lastName: "",
  email: "",
  organizationName: "",
  // Only read when the page offers the create-organization opt-in; "join" is the unticked box.
  organizationChoice: "join" as SignupOrganizationChoice,
};

export const signupFormSchema = z.object({
  firstName: z.string().trim().min(1, { message: "First name is required" }),
  lastName: z.string().trim().min(1, { message: "Last name is required" }),
  email: z.string().trim().email({ message: "Invalid email" }),
  organizationName: z.string().trim().optional(),
  organizationChoice: z.enum(["join", "create"]).optional(),
});

/**
 * The organization name is only collected when the tenant allows org creation
 * from signup, so the requirement has to be applied at render time rather than
 * baked into the base schema.
 */
export const buildSignupFormSchema = (
  organizationNameRequired: boolean,
  offerOrganizationChoice = false,
) => {
  if (organizationNameRequired && offerOrganizationChoice) {
    // Create-organization opt-in: the name is required only once the user ticks the box.
    return signupFormSchema.superRefine((values, ctx) => {
      if (values.organizationChoice === "create" && !values.organizationName) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          path: ["organizationName"],
          message: "Organization name is required",
        });
      }
    });
  }
  return organizationNameRequired
    ? signupFormSchema.extend({
        organizationName: z
          .string()
          .trim()
          .min(1, { message: "Organization name is required" }),
      })
    : signupFormSchema;
};
