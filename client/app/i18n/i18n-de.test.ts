import { afterEach, describe, expect, it } from "vitest";
import { useI18nStore } from "@seliseblocks/genesis-os/i18n";
import { useAppSettingsStore } from "@seliseblocks/genesis-os/store";
import { getErrorMessage } from "@/lib/error";
import { OIDC_LOGIN_PANEL } from "@blocks-idp/authentication/pages/oidc/oidc-panel-config";
import { normalizeOidcUiTemplate, DEFAULT_OIDC_UI_TEMPLATE } from "@blocks-idp/authentication/models/oidc-ui-template";

const useGerman = () => useAppSettingsStore.getState().setSettings({ language: "de" });

describe("German text for shared and oidc modules", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("shows English by default", () => {
    expect(getErrorMessage({})).toBe("Something went wrong.");
  });

  it("shows German shared text when the language is de", () => {
    useI18nStore.getState().setRemote("de", "iam.shared", {
      "ERROR.SOMETHING_WENT_WRONG": "Etwas ist schiefgelaufen.",
      "OIDC_UI_TEMPLATE.MFA_HEADING": "Bestätigen Sie Ihre Identität",
    });
    useGerman();
    expect(getErrorMessage({})).toBe("Etwas ist schiefgelaufen.");
    expect(normalizeOidcUiTemplate(DEFAULT_OIDC_UI_TEMPLATE).pages.mfa.heading).toBe(
      "Bestätigen Sie Ihre Identität",
    );
  });

  it("shows German oidc text when the language is de", () => {
    useI18nStore.getState().setRemote("de", "iam.oidc", {
      "OIDC_LOGIN_PANEL.HEADING": "Anmeldeablauf",
    });
    useGerman();
    expect(OIDC_LOGIN_PANEL.heading).toBe("Anmeldeablauf");
  });
});
