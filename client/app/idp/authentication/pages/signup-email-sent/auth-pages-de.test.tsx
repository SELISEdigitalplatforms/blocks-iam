import { act, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useAppSettingsStore } from "@seliseblocks/genesis-os/store";
import { useI18nStore } from "@seliseblocks/genesis-os/i18n";

vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: undefined, collectPasswordOnActivation: true }),
}));

import { SignupEmailSent } from "./signup-email-sent";
import { ActivationSuccess } from "../activation-success/activation-success";

describe("signup and activation pages in German", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new Error("offline")));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    act(() => useAppSettingsStore.getState().setSettings({ language: "en" }));
    useI18nStore.setState({ remote: {} });
  });

  it("shows the German signup-email-sent text when a German dictionary is registered", () => {
    act(() => {
      useI18nStore.getState().setRemote("de", "iam.signup", { "SIGNUP_EMAIL_SENT.TITLE": "E-Mail gesendet" });
      useAppSettingsStore.getState().setSettings({ language: "de" });
    });
    render(
      <MemoryRouter>
        <SignupEmailSent email="jane@example.com" />
      </MemoryRouter>,
    );
    expect(screen.getByRole("heading", { name: "E-Mail gesendet" })).toBeInTheDocument();
  });

  it("shows the German activation success title", () => {
    act(() => {
      useI18nStore.getState().setRemote("de", "iam.activation", { "ACTIVATION_SUCCESS.TITLE": "Konto aktiviert" });
      useAppSettingsStore.getState().setSettings({ language: "de" });
    });
    render(
      <MemoryRouter>
        <ActivationSuccess />
      </MemoryRouter>,
    );
    expect(screen.getByRole("heading", { name: "Konto aktiviert" })).toBeInTheDocument();
  });
});
