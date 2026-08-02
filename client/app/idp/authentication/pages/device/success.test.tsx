import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { describe, expect, it, vi } from "vitest";

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
}));
vi.mock("./panel-config", () => ({ DEVICE_CONSENT_PANEL: {} }));

import { DeviceSuccessPage } from "./success";

const renderAt = (path: string) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/device/:tenantId/success" element={<DeviceSuccessPage />} />
      </Routes>
    </MemoryRouter>,
  );

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
    expect(screen.getByText("Session Expired")).toBeInTheDocument();
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
