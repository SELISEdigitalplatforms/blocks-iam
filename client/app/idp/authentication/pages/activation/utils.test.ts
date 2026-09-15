import { describe, expect, it } from "vitest";
import { buildActivationFormSchema } from "./utils";
import type { IOidcPasswordPolicy } from "@blocks-idp/authentication/utils/password-policy.util";

const values = {
  firstname: "Grace",
  lastname: "Hopper",
  password: "Sunflower",
  confirmPassword: "Sunflower",
};

describe("buildActivationFormSchema", () => {
  it("applies the active tenant policy to the submitted password", () => {
    const policy: IOidcPasswordPolicy = {
      minLength: 1,
      maxLength: 64,
      requireUppercase: false,
      requireLowercase: false,
      requireNumbers: true,
      requireSpecialChars: false,
      message: null,
    };
    // "Sunflower" has no digit, so requireNumbers is unmet.
    const result = buildActivationFormSchema(policy).safeParse(values);
    expect(result.success).toBe(false);
    if (!result.success) expect(result.error.issues[0]?.message).toBe("Must include a number");
  });

  it("does not apply the policy to the confirm-password field", () => {
    // The confirm field only needs to match the password on submit -- it is never itself
    // checked against the policy's character-class rules.
    const policy: IOidcPasswordPolicy = {
      minLength: 1,
      maxLength: 64,
      requireUppercase: false,
      requireLowercase: false,
      requireNumbers: true,
      requireSpecialChars: false,
      message: null,
    };
    const result = buildActivationFormSchema(policy).safeParse({
      ...values,
      password: "Sunflower7",
      confirmPassword: "Sunflower7",
    });
    expect(result.success).toBe(true);
  });

  it("keeps the no-whitespace rule and 256-character cap", () => {
    const schema = buildActivationFormSchema(null);
    expect(schema.safeParse({ ...values, password: "has space" }).success).toBe(false);
    expect(schema.safeParse({ ...values, password: "a".repeat(257) }).success).toBe(false);
  });
});
