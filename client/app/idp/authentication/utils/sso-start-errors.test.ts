import { describe, expect, it } from "vitest";
import { getSsoStartErrorMessage } from "./sso-start-errors";

describe("getSsoStartErrorMessage", () => {
  it("maps an unregistered redirect URI to a setup message", () => {
    expect(getSsoStartErrorMessage({ error: "invalid_redirect_uri" })).toBe(
      "This sign-in option is not set up for this page. Contact your administrator.",
    );
  });

  it("reads the code from a thrown error's body", () => {
    expect(getSsoStartErrorMessage({ errors: { error: "invalid_client" } })).toBe(
      "The application that sent you here is not recognised.",
    );
  });

  it("maps a failed start to a fixed message", () => {
    expect(
      getSsoStartErrorMessage({ error: "authorization_url_generation_failed", error_description: "x" }),
    ).toBe("Could not start sign-in with this provider. Please try again.");
  });

  it("uses the server description for an unknown code", () => {
    expect(getSsoStartErrorMessage({ error: "other", error_description: "Readable reason" })).toBe(
      "Readable reason",
    );
  });

  it("falls back to the code when there is no description", () => {
    expect(getSsoStartErrorMessage({ error: "provider down" })).toBe("provider down");
  });

  it("keeps a thrown string message", () => {
    expect(getSsoStartErrorMessage({ errors: "Network error" })).toBe("Network error");
  });

  it("falls back to a general message", () => {
    expect(getSsoStartErrorMessage(null)).toBe(
      "Could not start sign-in with this provider. Please try again.",
    );
  });
});
