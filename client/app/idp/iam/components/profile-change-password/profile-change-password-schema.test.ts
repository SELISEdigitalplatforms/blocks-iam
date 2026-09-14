import { describe, expect, it } from "vitest";
import { buildChangePasswordSchema } from "./change-password-schema";

const values = { oldPassword: "old", newPassword: "a", confirmNewPassword: "a" };

describe("buildChangePasswordSchema", () => {
  it("accepts a one-character new password when no policy is active", () => {
    expect(buildChangePasswordSchema(null).safeParse(values).success).toBe(true);
  });

  it("uses the active tenant policy message", () => {
    const policy = { test: (password: string) => password === "allowed", message: "Use allowed" };
    const result = buildChangePasswordSchema(policy).safeParse(values);
    expect(result.success).toBe(false);
    if (!result.success) expect(result.error.issues[0]?.message).toBe("Use allowed");
  });
});
