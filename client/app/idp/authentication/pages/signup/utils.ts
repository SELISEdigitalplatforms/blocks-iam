import { tStatic } from "@seliseblocks/genesis-os/i18n";
import { z } from "zod";

const IAM_SIGNUP_MODULE = "iam.signup";

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
  firstName: z.string().trim().min(1, { error: () => tStatic(IAM_SIGNUP_MODULE, "UTILS.FIRST_NAME_REQUIRED") }),
  lastName: z.string().trim().min(1, { error: () => tStatic(IAM_SIGNUP_MODULE, "UTILS.LAST_NAME_REQUIRED") }),
  email: z.string().trim().pipe(z.email({ error: () => tStatic(IAM_SIGNUP_MODULE, "UTILS.EMAIL_INVALID") })),
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
          code: "custom",
          path: ["organizationName"],
          message: tStatic(IAM_SIGNUP_MODULE, "UTILS.ORGANIZATION_NAME_REQUIRED"),
        });
      }
    });
  }
  return organizationNameRequired
    ? signupFormSchema.extend({
        organizationName: z
          .string()
          .trim()
          .min(1, { error: () => tStatic(IAM_SIGNUP_MODULE, "UTILS.ORGANIZATION_NAME_REQUIRED") }),
      })
    : signupFormSchema;
};
