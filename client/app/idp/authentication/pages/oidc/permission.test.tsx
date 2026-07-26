import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  context: {} as Record<string, unknown>,
}));

vi.mock("@/layouts/oidc-layout", () => ({
  useOIDCContext: () => h.context,
}));

import { OIDCPermissionScreen } from "./permission";

const baseContext = {
  userName: "Jane Doe",
  themeColor: "#123456",
  state: "xyz-state",
  nonce: "n-once",
  scope: "openid email",
  redirectUri: "https://app.example.com/callback",
  clientId: "client-1",
  tenantId: "tenant-1",
};

let hrefSetter: ReturnType<typeof vi.fn>;

const renderScreen = () =>
  render(
    <MemoryRouter>
      <OIDCPermissionScreen />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.context = { ...baseContext };
  hrefSetter = vi.fn();
  Object.defineProperty(window, "location", {
    configurable: true,
    value: {
      origin: "https://iam.example.com",
      get href() {
        return "https://iam.example.com/oidc/permission";
      },
      set href(value: string) {
        hrefSetter(value);
      },
    },
  });
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("OIDCPermissionScreen", () => {
  it("renders the greeting, user name and consent copy", () => {
    renderScreen();
    expect(screen.getByText("Hello")).toBeInTheDocument();
    expect(screen.getByText("Jane Doe")).toBeInTheDocument();
    expect(
      screen.getByText(/about to connect your Blocks Account/i),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Allow" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deny" })).toBeInTheDocument();
  });

  it("omits the user name block when no user name is present", () => {
    h.context = { ...baseContext, userName: "" };
    renderScreen();
    expect(screen.queryByText("Jane Doe")).not.toBeInTheDocument();
  });

  it("redirects with an access_denied error when Deny is clicked", () => {
    renderScreen();
    fireEvent.click(screen.getByRole("button", { name: "Deny" }));

    expect(hrefSetter).toHaveBeenCalledTimes(1);
    const url = new URL(hrefSetter.mock.calls[0][0]);
    expect(url.origin + url.pathname).toBe("https://app.example.com/callback");
    expect(url.searchParams.get("error")).toBe("access_denied");
    expect(url.searchParams.get("state")).toBe("xyz-state");
  });

  it("does nothing on Deny when there is no redirect URI", () => {
    h.context = { ...baseContext, redirectUri: "" };
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    renderScreen();
    fireEvent.click(screen.getByRole("button", { name: "Deny" }));
    expect(hrefSetter).not.toHaveBeenCalled();
    expect(errorSpy).toHaveBeenCalledWith("No redirect URI available");
  });

  it("builds the PKCE authorize URL and redirects when Allow is clicked", async () => {
    renderScreen();
    fireEvent.click(screen.getByRole("button", { name: "Allow" }));

    await waitFor(() => expect(hrefSetter).toHaveBeenCalledTimes(1));
    const url = new URL(hrefSetter.mock.calls[0][0]);
    expect(url.origin + url.pathname).toBe(
      "https://iam.example.com/api/oidc/authorize",
    );
    expect(url.searchParams.get("client_id")).toBe("client-1");
    expect(url.searchParams.get("response_type")).toBe("code");
    expect(url.searchParams.get("code_challenge_method")).toBe("S256");
    expect(url.searchParams.get("code_challenge")).toBeTruthy();
    expect(url.searchParams.get("state")).toBe("xyz-state");
    expect(url.searchParams.get("nonce")).toBe("n-once");
    expect(url.searchParams.get("tenant_id")).toBe("tenant-1");
    // The verifier is stashed for the token exchange step.
    expect(sessionStorage.getItem("oidc-code-verifier")).toBeTruthy();
  });

  it("does not redirect on Allow when client id or redirect URI is missing", async () => {
    h.context = { ...baseContext, clientId: "" };
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    renderScreen();
    fireEvent.click(screen.getByRole("button", { name: "Allow" }));

    await waitFor(() =>
      expect(errorSpy).toHaveBeenCalledWith("Missing client ID or redirect URI"),
    );
    expect(hrefSetter).not.toHaveBeenCalled();
  });
});
