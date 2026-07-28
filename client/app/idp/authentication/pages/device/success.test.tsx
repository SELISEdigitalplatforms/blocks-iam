import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";

vi.mock("@blocks-idp/authentication/pages/oidc/oidc-auth-shell", () => ({
  OidcAuthShell: ({
    children,
    heading,
    successTitle,
    successSubtitle,
    footerNote,
  }: {
    children: React.ReactNode;
    heading: string;
    successTitle: string;
    successSubtitle: string;
    footerNote: React.ReactNode;
  }) => (
    <div>
      <h1>{heading}</h1>
      <p>{successTitle}</p>
      <p>{successSubtitle}</p>
      <div>{footerNote}</div>
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
    expect(screen.getByText("Device Authorized")).toBeInTheDocument();
    expect(screen.getByText("Device Flow Complete")).toBeInTheDocument();
    expect(screen.getByText("t1")).toBeInTheDocument();
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
    expect(screen.getByText("You may close this tab")).toBeInTheDocument();
  });

  it("renders the use-another-code link to the device route", () => {
    renderAt("/device/t1/success");
    expect(screen.getByRole("link", { name: "Use another code" })).toHaveAttribute(
      "href",
      "/device/t1",
    );
  });
});
