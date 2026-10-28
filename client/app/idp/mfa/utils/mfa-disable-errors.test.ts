import { describe, expect, it } from "vitest";
import { getDisableMfaErrorMessage } from "./mfa-disable-errors";

const err = (error: string, description?: string) => ({ errors: { error, error_description: description } });

describe("getDisableMfaErrorMessage", () => {
  it.each([
    ["invalid_username_password", "password is incorrect"],
    ["invalid_mfa_code", "wrong or has expired"],
    ["account_locked", "locked"],
    ["mfa_disable_not_allowed", "policy"],
  ])("%s gives its own message", (code, text) => {
    expect(getDisableMfaErrorMessage(err(code))).toContain(text);
  });

  it("uses the server description for other codes", () => {
    expect(getDisableMfaErrorMessage(err("invalid_request", "Password is required"))).toBe("Password is required");
  });

  it("falls back when the error has no body", () => {
    expect(getDisableMfaErrorMessage(new Error("x"))).toBe("Could not disable MFA. Please try again.");
  });
});
