import { describe, expect, it } from "vitest";
import { buildResetPasswordFormSchema } from "./utils";
import type { IOidcPasswordPolicy } from "@blocks-idp/authentication/utils/password-policy.util";

const policy: IOidcPasswordPolicy = {
  minLength: 10,
  maxLength: 64,
  requireUppercase: false,
  requireLowercase: false,
  requireNumbers: true,
  requireSpecialChars: false,
  message: "At least 10 characters including one number.",
};

describe("buildResetPasswordFormSchema", () => {
  it("applies the same active policy as the on-screen checker", () => {
    // "Sunflower" is 9 characters (below minLength 10) and has no digit.
    const result = buildResetPasswordFormSchema(policy).safeParse({
      password: "Sunflower",
      confirmPassword: "Sunflower",
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues[0]?.message).toBe(
        "Password must be at least 10 characters long",
      );
    }
  });

  it("accepts a password that satisfies every active rule", () => {
    const result = buildResetPasswordFormSchema(policy).safeParse({
      password: "Sunflower7",
      confirmPassword: "Sunflower7",
    });
    expect(result.success).toBe(true);
  });

  it("accepts a one-character matching password without a policy", () => {
    expect(
      buildResetPasswordFormSchema(null).safeParse({ password: "a", confirmPassword: "a" }).success,
    ).toBe(true);
  });

  it("still rejects empty, over-limit, and mismatched passwords", () => {
    const schema = buildResetPasswordFormSchema(null);
    expect(schema.safeParse({ password: "", confirmPassword: "" }).success).toBe(false);
    expect(schema.safeParse({ password: "a".repeat(257), confirmPassword: "a".repeat(257) }).success).toBe(false);
    const mismatch = schema.safeParse({ password: "a", confirmPassword: "b" });
    expect(mismatch.success).toBe(false);
    if (!mismatch.success) expect(mismatch.error.issues[0]?.message).toBe("Passwords must be matched");
  });
});
