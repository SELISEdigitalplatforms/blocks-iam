import { describe, expect, it } from "vitest";
import { buildResetPasswordFormSchema } from "./utils";

const policy = {
  test: (password: string) => /^(?=.*\d).{10,64}$/i.test(password),
  message: "At least 10 characters including one number.",
};

describe("buildResetPasswordFormSchema", () => {
  it("applies the same active policy and its message", () => {
    const result = buildResetPasswordFormSchema(policy).safeParse({
      password: "Sunflower",
      confirmPassword: "Sunflower",
    });
    expect(result.success).toBe(false);
    if (!result.success) expect(result.error.issues[0]?.message).toBe(policy.message);
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
