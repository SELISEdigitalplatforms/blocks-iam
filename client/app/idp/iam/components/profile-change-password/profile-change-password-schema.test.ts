import { describe, expect, it } from "vitest";
import { buildChangePasswordSchema } from "./change-password-schema";
import type { IOidcPasswordPolicy } from "@blocks-idp/authentication/utils/password-policy.util";

const values = { oldPassword: "old", newPassword: "a", confirmNewPassword: "a" };

describe("buildChangePasswordSchema", () => {
  it("falls back to the FE default policy for the new password when no tenant policy is active", () => {
    expect(buildChangePasswordSchema(null).safeParse(values).success).toBe(false);
    expect(
      buildChangePasswordSchema(null).safeParse({
        ...values,
        newPassword: "Sunflower7!",
        confirmNewPassword: "Sunflower7!",
      }).success,
    ).toBe(true);
  });

  it("applies the active tenant policy to the new password", () => {
    const policy: IOidcPasswordPolicy = {
      minLength: 8,
      maxLength: 64,
      requireUppercase: true,
      requireLowercase: false,
      requireNumbers: false,
      requireSpecialChars: false,
      message: null,
    };
    const result = buildChangePasswordSchema(policy).safeParse(values);
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues[0]?.message).toBe(
        "Password must be at least 8 characters long",
      );
    }
  });
});
