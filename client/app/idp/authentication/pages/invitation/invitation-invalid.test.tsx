import { act, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useAppSettingsStore } from "@seliseblocks/genesis-os/store";
import { useI18nStore } from "@seliseblocks/genesis-os/i18n";

vi.mock("../oidc/oidc-auth-shell", () => ({
  OidcAuthShell: ({ heading, children }: { heading: string; children: React.ReactNode }) => (
    <div>
      <h1>{heading}</h1>
      {children}
    </div>
  ),
  OidcFooter: () => null,
}));

import { InvitationInvalid } from "./invitation-invalid";
import { DEFAULT_OIDC_UI_TEMPLATE_FIXTURE } from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

describe("InvitationInvalid", () => {
  afterEach(() => {
    act(() => useAppSettingsStore.getState().setSettings({ language: "en" }));
    useI18nStore.setState({ remote: {} });
  });

  it("shows English text by default", () => {
    render(
      <MemoryRouter>
        <InvitationInvalid template={DEFAULT_OIDC_UI_TEMPLATE_FIXTURE} />
      </MemoryRouter>,
    );
    expect(screen.getByRole("heading", { name: "Link unavailable" })).toBeInTheDocument();
  });

  it("shows the remote text for another language", () => {
    act(() => {
      useI18nStore.getState().setRemote("xx", "iam.invitation", {
        "INVITATION_INVALID.HEADING": "[xx] Link unavailable",
      });
      useAppSettingsStore.getState().setSettings({ language: "xx" });
    });
    render(
      <MemoryRouter>
        <InvitationInvalid template={DEFAULT_OIDC_UI_TEMPLATE_FIXTURE} />
      </MemoryRouter>,
    );
    expect(screen.getByRole("heading", { name: "[xx] Link unavailable" })).toBeInTheDocument();
  });
});
