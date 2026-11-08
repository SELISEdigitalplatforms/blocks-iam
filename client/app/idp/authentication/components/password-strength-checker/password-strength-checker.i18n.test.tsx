import { act, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { useI18nStore } from "@seliseblocks/genesis-os/i18n";
import { useAppSettingsStore } from "@seliseblocks/genesis-os/store";
import { PasswordStrengthChecker } from "./password-strength-checker";
import { getSsoStartErrorMessage } from "@blocks-idp/authentication/utils/sso-start-errors";

const setLanguage = (language: "en" | "xx") =>
  act(() => useAppSettingsStore.getState().setSettings({ language }));

afterEach(() => {
  setLanguage("en");
  useI18nStore.setState({ remote: {} });
});

describe("iam.shared in another language", () => {
  it("shows the remote dictionary once it has arrived", () => {
    setLanguage("xx");
    act(() =>
      useI18nStore.getState().setRemote("xx", "iam.shared", {
        "PASSWORD_STRENGTH_CHECKER.TITLE": "[xx] Password Strength",
        "PASSWORD_STRENGTH_CHECKER.PASSWORDS_MATCH": "[xx] Passwords match",
        "SSO_START_ERRORS.INVALID_CLIENT":
          "[xx] The application that sent you here is not recognised.",
      }),
    );

    render(
      <PasswordStrengthChecker
        password="abc"
        confirmPassword="abc"
        onRequirementsMet={() => {}}
        policy={null}
      />,
    );

    expect(screen.getByText("[xx] Password Strength")).toBeInTheDocument();
    expect(screen.getByText("[xx] Passwords match")).toBeInTheDocument();
    // Text outside React reads the same dictionary.
    expect(getSsoStartErrorMessage({ error: "invalid_client" })).toBe(
      "[xx] The application that sent you here is not recognised.",
    );
  });

  it("falls back to bundled English for a key the remote dictionary lacks", () => {
    setLanguage("xx");
    render(
      <PasswordStrengthChecker
        password="abc"
        confirmPassword="abc"
        onRequirementsMet={() => {}}
        policy={null}
      />,
    );
    expect(screen.getByText("Password Strength")).toBeInTheDocument();
  });
});
