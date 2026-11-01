import { act, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { useI18nStore } from "@seliseblocks/genesis-os/i18n";
import { useAppSettingsStore } from "@seliseblocks/genesis-os/store";
import { PasswordStrengthChecker } from "./password-strength-checker";
import { getSsoStartErrorMessage } from "@blocks-idp/authentication/utils/sso-start-errors";

const setLanguage = (language: "en" | "de") =>
  act(() => useAppSettingsStore.getState().setSettings({ language }));

afterEach(() => {
  setLanguage("en");
  useI18nStore.setState({ remote: {} });
});

describe("iam.shared in German", () => {
  it("shows the German dictionary once it has arrived", () => {
    setLanguage("de");
    act(() =>
      useI18nStore.getState().setRemote("de", "iam.shared", {
        "PASSWORD_STRENGTH_CHECKER.TITLE": "Passwortstärke",
        "PASSWORD_STRENGTH_CHECKER.PASSWORDS_MATCH": "Passwörter stimmen überein",
        "SSO_START_ERRORS.INVALID_CLIENT": "Die Anwendung wird nicht erkannt.",
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

    expect(screen.getByText("Passwortstärke")).toBeInTheDocument();
    expect(screen.getByText("Passwörter stimmen überein")).toBeInTheDocument();
    // Text outside React reads the same dictionary.
    expect(getSsoStartErrorMessage({ error: "invalid_client" })).toBe(
      "Die Anwendung wird nicht erkannt.",
    );
  });

  it("falls back to bundled English for a key the German dictionary lacks", () => {
    setLanguage("de");
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
