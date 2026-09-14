import { describe, expect, it } from "vitest";
import { resolveActivationCodeStatus } from "./activation-status";

describe("resolveActivationCodeStatus", () => {
  it("takes the status the server sent", () => {
    expect(resolveActivationCodeStatus({ status: "AlreadyActivated" })).toBe("AlreadyActivated");
    expect(resolveActivationCodeStatus({ status: "Expired" })).toBe("Expired");
  });

  it("prefers the status over what the older fields would have implied", () => {
    expect(
      resolveActivationCodeStatus({ status: "AlreadyActivated", isSuccess: false, errors: null }),
    ).toBe("AlreadyActivated");
  });

  describe("against a server predating the status field", () => {
    it("reads a usable code as valid", () => {
      expect(resolveActivationCodeStatus({ isSuccess: true })).toBe("Valid");
    });

    it("reads reported errors as an unissued code", () => {
      expect(resolveActivationCodeStatus({ isSuccess: false, errors: { code: "bad" } })).toBe(
        "Invalid",
      );
    });

    it("reads a silent refusal as expired, which is all that server could say", () => {
      expect(resolveActivationCodeStatus({ isSuccess: false, errors: null })).toBe("Expired");
    });
  });

  it("treats a missing response as invalid", () => {
    expect(resolveActivationCodeStatus(undefined)).toBe("Invalid");
    expect(resolveActivationCodeStatus(null)).toBe("Invalid");
  });
});
