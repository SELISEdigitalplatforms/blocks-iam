import { afterEach, describe, expect, it } from "vitest";
import { useI18nStore } from "@seliseblocks/genesis-os/i18n";
import { useAppSettingsStore } from "@seliseblocks/genesis-os/store";
import { getErrorMessage } from "@/lib/error";
import { OIDC_LOGIN_PANEL } from "@blocks-idp/authentication/pages/oidc/oidc-panel-config";
import {
  normalizeOidcUiTemplate,
  DEFAULT_OIDC_UI_TEMPLATE,
} from "@blocks-idp/authentication/models/oidc-ui-template";

const useOtherLanguage = () => useAppSettingsStore.getState().setSettings({ language: "xx" });

describe("Remote text for shared and oidc modules", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("shows English by default", () => {
    expect(getErrorMessage({})).toBe("Something went wrong.");
  });

  it("shows remote shared text for another language", () => {
    useI18nStore.getState().setRemote("xx", "iam.shared", {
      "ERROR.SOMETHING_WENT_WRONG": "[xx] Something went wrong.",
      "OIDC_UI_TEMPLATE.MFA_HEADING": "[xx] Verify it's you",
    });
    useOtherLanguage();
    expect(getErrorMessage({})).toBe("[xx] Something went wrong.");
    expect(normalizeOidcUiTemplate(DEFAULT_OIDC_UI_TEMPLATE).pages.mfa.heading).toBe(
      "[xx] Verify it's you",
    );
  });

  it("shows remote oidc text for another language", () => {
    useI18nStore.getState().setRemote("xx", "iam.oidc", {
      "OIDC_LOGIN_PANEL.HEADING": "[xx] OIDC Authentication Pipeline",
    });
    useOtherLanguage();
    expect(OIDC_LOGIN_PANEL.heading).toBe("[xx] OIDC Authentication Pipeline");
  });
});
