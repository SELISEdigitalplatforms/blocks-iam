import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ oidcUiConfig: undefined as unknown }));

vi.mock("@blocks-idp/authentication/pages/oidc/oidc-auth-shell", () => ({
  OidcAuthShell: ({
    children,
    heading,
    successTitle,
    successSubtitle,
  }: {
    children: React.ReactNode;
    heading: string;
    successTitle: string;
    successSubtitle: string;
  }) => (
    <div>
      <h1>{heading}</h1>
      <p>{successTitle}</p>
      <p>{successSubtitle}</p>
      {children}
    </div>
  ),
  OidcFooter: ({ footerText }: { footerText: string }) => <span>{footerText}</span>,
  useOidcAuthAnimation: () => ({
    phase: "idle" as const,
    startAnimation: () => {},
    succeedAnimation: async () => {},
    failAnimation: async () => {},
    resetAnimation: () => {},
  }),
}));
vi.mock("./panel-config", () => ({ DEVICE_CONSENT_PANEL: {} }));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig }),
}));

import { DeviceSuccessPage } from "./success";
import { DEFAULT_OIDC_UI_TEMPLATE_FIXTURE } from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

const renderAt = (path: string) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/device/:tenantId/success" element={<DeviceSuccessPage />} />
      </Routes>
    </MemoryRouter>,
  );

beforeEach(() => {
  h.oidcUiConfig = { captcha: null, template: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE };
});

describe("DeviceSuccessPage", () => {
  it("shows the approved copy", () => {
    renderAt("/device/t1/success?outcome=approved");
    expect(screen.getByText("Success")).toBeInTheDocument();
    expect(screen.getByText("Device Flow Complete")).toBeInTheDocument();
    expect(
      screen.getByText("Your device has been authorized. You can close this window."),
    ).toBeInTheDocument();
  });

  it("shows the denied copy and heading", () => {
    renderAt("/device/t1/success?outcome=denied");
    expect(screen.getAllByText("Authorization Declined").length).toBeGreaterThan(0);
  });

  it("shows the expired copy", () => {
    renderAt("/device/t1/success?outcome=expired");
    // The shell renders the outcome twice, as the heading and as the success
    // title, the same way it does for the declined outcome above.
    expect(screen.getAllByText("Session Expired").length).toBeGreaterThan(0);
    expect(
      screen.getByText("The device code expired before approval. You can close this window."),
    ).toBeInTheDocument();
  });

  it("shows the neutral copy when the outcome is unknown", () => {
    renderAt("/device/t1/success?outcome=whatever");
    expect(screen.getByText("Success")).toBeInTheDocument();
  });

  it("does not render a use-another-code link", () => {
    renderAt("/device/t1/success");
    expect(screen.queryByRole("link", { name: "Use another code" })).not.toBeInTheDocument();
  });
});
