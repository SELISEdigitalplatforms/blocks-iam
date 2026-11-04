import { describe, expect, it } from "vitest";
import { revokeResultMessage } from "./revoke-message";

describe("revokeResultMessage", () => {
  it("reports a fresh sign-out", () => {
    expect(revokeResultMessage({ alreadyRevoked: false, revokedRefreshTokens: 2, warnings: [] })).toBe(
      "Device signed out successfully",
    );
  });

  it("says so when the session was no longer live", () => {
    expect(revokeResultMessage({ alreadyRevoked: true, revokedRefreshTokens: 0, warnings: [] })).toBe(
      "This device was already signed out",
    );
  });

  it("falls back to the plain message when the response has no body", () => {
    expect(revokeResultMessage(undefined)).toBe("Device signed out successfully");
    expect(revokeResultMessage(null)).toBe("Device signed out successfully");
  });
});
