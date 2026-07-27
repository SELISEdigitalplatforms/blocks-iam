import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  verify: vi.fn(),
  decide: vi.fn(),
}));

vi.mock("@blocks-idp/authentication/services/device.service", () => ({
  deviceService: { verify: h.verify, decide: h.decide },
}));

// Shell rendered as a transparent passthrough exposing heading + children, with
// a no-op animation context so the flow logic runs deterministically.
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-auth-shell", () => ({
  OidcAuthShell: ({
    children,
    heading,
  }: {
    children: React.ReactNode;
    heading: string;
  }) => (
    <div>
      <h1>{heading}</h1>
      {children}
    </div>
  ),
  useOidcAuthAnimation: () => ({
    phase: "idle",
    startAnimation: vi.fn(),
    succeedAnimation: vi.fn().mockResolvedValue(undefined),
    failAnimation: vi.fn().mockResolvedValue(undefined),
    resetAnimation: vi.fn(),
  }),
}));

import { DeviceEntryPage } from "./entry";

const readyPayload = {
  clientName: "Acme CLI",
  clientId: "cli-1",
  scopes: ["openid", "email"],
  tenant: "tenant-1",
  userCode: "ABCD-EFGH",
};

let assignSpy: ReturnType<typeof vi.fn>;

const renderPage = (tenantId = "tenant-1", search = "") =>
  render(
    <MemoryRouter initialEntries={[`/device/${tenantId}${search}`]}>
      <Routes>
        <Route path="/device/:tenantId" element={<DeviceEntryPage />} />
      </Routes>
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  assignSpy = vi.fn();
  Object.defineProperty(window, "location", {
    configurable: true,
    value: { href: "http://localhost/device/tenant-1", assign: assignSpy, search: "" },
  });
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("DeviceEntryPage", () => {
  it("shows the missing-tenant error when the tenant param is absent", () => {
    render(
      <MemoryRouter initialEntries={["/device/"]}>
        <Routes>
          <Route path="/device/:tenantId?" element={<DeviceEntryPage />} />
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByText("Missing Tenant")).toBeInTheDocument();
  });

  it("renders the code entry form for a valid tenant", () => {
    renderPage();
    expect(screen.getByLabelText("Verification Code")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /continue/i })).toBeInTheDocument();
    expect(screen.getByText("tenant-1")).toBeInTheDocument();
  });

  it("rejects an invalid code without calling the service", async () => {
    renderPage();
    fireEvent.change(screen.getByLabelText("Verification Code"), {
      target: { value: "123" },
    });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));

    expect(
      await screen.findByText(/Enter the 8-character verification code/i),
    ).toBeInTheDocument();
    expect(h.verify).not.toHaveBeenCalled();
  });

  it("verifies a valid code and renders the consent screen", async () => {
    h.verify.mockResolvedValue({ status: "ready", payload: readyPayload });
    renderPage();

    fireEvent.change(screen.getByLabelText("Verification Code"), {
      target: { value: "ABCDEFGH" },
    });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));

    expect(await screen.findByText("Authorize this device?")).toBeInTheDocument();
    expect(h.verify).toHaveBeenCalledWith("ABCD-EFGH", "tenant-1");
    expect(screen.getByText("Acme CLI")).toBeInTheDocument();
    // scope descriptions
    expect(
      screen.getByText("Authenticate you with your Blocks account"),
    ).toBeInTheDocument();
    expect(screen.getByText("Access your email address")).toBeInTheDocument();
  });

  it("redirects when the server requires login first", async () => {
    h.verify.mockResolvedValue({
      status: "login_required",
      returnUrl: "https://iam.example.com/oidc/login",
    });
    renderPage();

    fireEvent.change(screen.getByLabelText("Verification Code"), {
      target: { value: "ABCDEFGH" },
    });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));

    await waitFor(() =>
      expect(assignSpy).toHaveBeenCalledWith("https://iam.example.com/oidc/login"),
    );
  });

  it("shows the expired screen when the code has expired", async () => {
    h.verify.mockRejectedValue({ errors: { error: "expired_token" } });
    renderPage();

    fireEvent.change(screen.getByLabelText("Verification Code"), {
      target: { value: "ABCDEFGH" },
    });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));

    expect(await screen.findByText("Device code expired")).toBeInTheDocument();
  });

  it("shows a generic error message when verification fails", async () => {
    h.verify.mockRejectedValue({ errors: { error: "invalid_grant" } });
    renderPage();

    fireEvent.change(screen.getByLabelText("Verification Code"), {
      target: { value: "ABCDEFGH" },
    });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));

    expect(await screen.findByText("Invalid or expired code.")).toBeInTheDocument();
  });

  it("renders the tenant-mismatch screen when the payload tenant differs", async () => {
    h.verify.mockResolvedValue({
      status: "ready",
      payload: { ...readyPayload, tenant: "other-tenant" },
    });
    renderPage();

    fireEvent.change(screen.getByLabelText("Verification Code"), {
      target: { value: "ABCDEFGH" },
    });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));

    expect(await screen.findByText("Tenant mismatch")).toBeInTheDocument();
  });

  it("submits an allow decision and redirects", async () => {
    h.verify.mockResolvedValue({ status: "ready", payload: readyPayload });
    h.decide.mockResolvedValue({ redirect: "https://app.example.com/done", status: "Approved" });
    renderPage();

    fireEvent.change(screen.getByLabelText("Verification Code"), {
      target: { value: "ABCDEFGH" },
    });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));

    const allow = await screen.findByRole("button", { name: "Allow" });
    fireEvent.click(allow);

    await waitFor(() =>
      expect(h.decide).toHaveBeenCalledWith("ABCD-EFGH", "allow", "tenant-1"),
    );
    await waitFor(() =>
      expect(assignSpy).toHaveBeenCalledWith("https://app.example.com/done"),
    );
  });

  it("submits a deny decision", async () => {
    h.verify.mockResolvedValue({ status: "ready", payload: readyPayload });
    h.decide.mockResolvedValue({ redirect: "https://app.example.com/denied", status: "Denied" });
    renderPage();

    fireEvent.change(screen.getByLabelText("Verification Code"), {
      target: { value: "ABCDEFGH" },
    });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));

    const deny = await screen.findByRole("button", { name: "Deny" });
    fireEvent.click(deny);

    await waitFor(() =>
      expect(h.decide).toHaveBeenCalledWith("ABCD-EFGH", "deny", "tenant-1"),
    );
  });

  it("shows the expired screen when a decision is no longer pending", async () => {
    h.verify.mockResolvedValue({ status: "ready", payload: readyPayload });
    h.decide.mockRejectedValue({ errors: { error: "request_not_pending" } });
    renderPage();

    fireEvent.change(screen.getByLabelText("Verification Code"), {
      target: { value: "ABCDEFGH" },
    });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));

    fireEvent.click(await screen.findByRole("button", { name: "Allow" }));

    expect(await screen.findByText("Device code expired")).toBeInTheDocument();
  });

  it("auto-submits a user_code supplied in the query string", async () => {
    h.verify.mockResolvedValue({ status: "ready", payload: readyPayload });
    renderPage("tenant-1", "?user_code=ABCDEFGH");

    await waitFor(() => expect(h.verify).toHaveBeenCalledWith("ABCD-EFGH", "tenant-1"));
    expect(await screen.findByText("Authorize this device?")).toBeInTheDocument();
  });

  it("redirects to a constructed login url when no returnUrl is provided", async () => {
    h.verify.mockResolvedValue({ status: "login_required" });
    renderPage();
    fireEvent.change(screen.getByLabelText("Verification Code"), { target: { value: "ABCDEFGH" } });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));
    await waitFor(() =>
      expect(assignSpy).toHaveBeenCalledWith(expect.stringContaining("/oidc/login?returnUrl=")),
    );
  });

  it("shows an unexpected-response error for an unknown status", async () => {
    h.verify.mockResolvedValue({ status: "something_else" });
    renderPage();
    fireEvent.change(screen.getByLabelText("Verification Code"), { target: { value: "ABCDEFGH" } });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));
    expect(await screen.findByText("Unexpected response from server.")).toBeInTheDocument();
  });

  it("shows a generic error when recording the decision fails unexpectedly", async () => {
    h.verify.mockResolvedValue({ status: "ready", payload: readyPayload });
    h.decide.mockRejectedValue({ errors: { error: "server_error" } });
    renderPage();
    fireEvent.change(screen.getByLabelText("Verification Code"), { target: { value: "ABCDEFGH" } });
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));
    fireEvent.click(await screen.findByRole("button", { name: "Allow" }));
    expect(
      await screen.findByText("We could not record your decision. Please try again."),
    ).toBeInTheDocument();
  });
});
