import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router";

const getContext = vi.fn();
const redeem = vi.fn();

vi.mock("@blocks-idp/authentication/services/signup-link.service", () => ({
  signupLinkService: {
    getContext: (...a: unknown[]) => getContext(...a),
    redeem: (...a: unknown[]) => redeem(...a),
  },
}));

vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: { template: { theme: {}, branding: { brandName: "Blocks" }, pages: { shared: { footerText: "" } } } } }),
}));

vi.mock("../oidc/oidc-auth-shell", () => ({
  OidcAuthShell: ({ children, heading }: { children?: React.ReactNode; heading?: string }) => (
    <div data-testid="shell"><h1>{heading}</h1>{children}</div>
  ),
  OidcFooter: () => null,
}));

vi.mock("@/hooks/use-toast", () => ({ showErrorToast: vi.fn() }));

import InvitationPage from "./invitation";

function renderJoin(hash = "#link=abc123") {
  window.history.replaceState(null, "", `/oidc/invitation/t1${hash}`);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/oidc/invitation/t1${hash}`]}>
        <Routes>
          <Route path="/oidc/invitation/:tenantId" element={<InvitationPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("InvitationPage", () => {
  beforeEach(() => {
    getContext.mockReset();
    redeem.mockReset();
  });

  it("clears the hash and shows invalid when context is false", async () => {
    getContext.mockResolvedValue({ valid: false });
    renderJoin("#link=deadbeef");
    await waitFor(() => expect(screen.getByText("This link is no longer valid.")).toBeInTheDocument());
    expect(window.location.hash).toBe("");
    expect(getContext).toHaveBeenCalled();
  });

  it("auto-redeems Passwordless and navigates to authorizeUrl", async () => {
    getContext.mockResolvedValue({
      valid: true,
      credentialMode: "Passwordless",
      firstName: "Asif",
      maskedEmail: "as•••@example.com",
      applicationName: "Construct",
    });
    redeem.mockResolvedValue({ authorizeUrl: "https://iam.example/api/oidc/authorize?x=1" });
    const assign = vi.fn();
    vi.stubGlobal("location", { ...window.location, assign, hash: "#link=abc", pathname: "/oidc/invitation/t1", search: "" });

    // Use real location for hash clear; spy assign via window.location.assign
    const original = window.location;
    Object.defineProperty(window, "location", {
      configurable: true,
      value: {
        ...original,
        hash: "#link=abc",
        pathname: "/oidc/invitation/t1",
        search: "",
        assign,
        href: "http://localhost/oidc/invitation/t1#link=abc",
      },
    });

    renderJoin("#link=abc");
    await waitFor(() => expect(redeem).toHaveBeenCalledWith("abc", "t1"));
    await waitFor(() => expect(assign).toHaveBeenCalledWith("https://iam.example/api/oidc/authorize?x=1"));
  });
});
