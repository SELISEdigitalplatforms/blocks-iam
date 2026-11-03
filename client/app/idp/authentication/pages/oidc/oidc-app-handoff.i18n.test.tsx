import { act, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { useI18nStore } from "@seliseblocks/genesis-os/i18n";
import { useAppSettingsStore } from "@seliseblocks/genesis-os/store";
import { OidcAppHandoff } from "./oidc-app-handoff";
import { OIDC_LOGIN_PANEL } from "./oidc-panel-config";

afterEach(() => {
  act(() => useAppSettingsStore.getState().setSettings({ language: "en" }));
  useI18nStore.setState({ remote: {} });
});

describe("iam.oidc in another language", () => {
  it("shows the remote text in the hand-off and in the pipeline panel", () => {
    act(() => useAppSettingsStore.getState().setSettings({ language: "xx" }));
    act(() =>
      useI18nStore.getState().setRemote("xx", "iam.oidc", {
        "OIDC_APP_HANDOFF.SIGNED_IN": "[xx] Signed in",
        "OIDC_LOGIN_PANEL.HEADING": "[xx] OIDC Authentication Pipeline",
      }),
    );

    render(<OidcAppHandoff url="com.example.app://cb?code=1" />);

    expect(screen.getByText("[xx] Signed in")).toBeInTheDocument();
    // English for a key without remote text.
    expect(screen.getByText("Return to the app")).toBeInTheDocument();
    expect(OIDC_LOGIN_PANEL.heading).toBe("[xx] OIDC Authentication Pipeline");
  });
});
