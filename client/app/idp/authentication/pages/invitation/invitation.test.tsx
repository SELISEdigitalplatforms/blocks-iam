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

type ActivationProps = {
  code?: string;
  tenantId?: string;
  collectPassword?: boolean;
  onActivated?: (res: { isSuccess: boolean; errors: unknown; authorizeUrl?: string }) => boolean;
};
let activationProps: ActivationProps | null = null;

vi.mock("../activation", () => ({
  Activation: (props: ActivationProps) => {
    activationProps = props;
    return <div data-testid="activation" />;
  },
}));

import InvitationPage, { EMBEDDED_LINK_COPY } from "./invitation";

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
    activationProps = null;
  });

  it("refuses an Embedded link without redeeming it", async () => {
    getContext.mockResolvedValue({
      valid: true,
      credentialMode: "Passwordless",
      firstName: "Asif",
      maskedEmail: "as•••@example.com",
      applicationName: "Partner Portal",
      mode: "Embedded",
    });

    renderJoin("#link=abc");

    // Redeem would spend the code on a page that cannot use the tokens it answers with.
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: EMBEDDED_LINK_COPY.heading })).toBeInTheDocument(),
    );
    expect(screen.getByText(EMBEDDED_LINK_COPY.message)).toBeInTheDocument();
    expect(redeem).not.toHaveBeenCalled();
  });

  it("sets the password on the page when redeem hands back an activation key", async () => {
    getContext.mockResolvedValue({
      valid: true,
      credentialMode: "PasswordRequired",
      firstName: "Asif",
      maskedEmail: "as•••@example.com",
      applicationName: "Construct",
    });
    redeem.mockResolvedValue({ activationKey: "act-key-1", credentialMode: "PasswordRequired" });

    renderJoin("#link=abc");

    // The key used to stop here behind "Complete password setup with your application".
    await waitFor(() => expect(screen.getByTestId("activation")).toBeInTheDocument());
    expect(activationProps).toMatchObject({ code: "act-key-1", tenantId: "t1", collectPassword: true });
  });

  it("follows the authorize URL the activation answers with", async () => {
    getContext.mockResolvedValue({
      valid: true,
      credentialMode: "PasswordRequired",
      firstName: "Asif",
      maskedEmail: "as•••@example.com",
      applicationName: "Construct",
    });
    redeem.mockResolvedValue({ activationKey: "act-key-1", credentialMode: "PasswordRequired" });
    const assign = vi.fn();
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

    try {
      renderJoin("#link=abc");
      await waitFor(() => expect(activationProps).not.toBeNull());

      expect(activationProps!.onActivated!({ isSuccess: true, errors: null })).toBe(false);
      expect(assign).not.toHaveBeenCalled();

      const url = "https://iam.example/api/oidc/authorize?client_id=construct-web";
      expect(activationProps!.onActivated!({ isSuccess: true, errors: null, authorizeUrl: url })).toBe(true);
      expect(assign).toHaveBeenCalledWith(url);
    } finally {
      Object.defineProperty(window, "location", { configurable: true, value: original });
    }
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
