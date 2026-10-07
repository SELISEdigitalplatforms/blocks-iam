import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router";

const getContext = vi.fn();
const redeem = vi.fn();
const authenticate = vi.fn();
const completeMfa = vi.fn();

vi.mock("@blocks-idp/authentication/services/signup-link.service", () => ({
  signupLinkService: {
    getContext: (...a: unknown[]) => getContext(...a),
    redeem: (...a: unknown[]) => redeem(...a),
    authenticate: (...a: unknown[]) => authenticate(...a),
    completeMfa: (...a: unknown[]) => completeMfa(...a),
  },
}));

vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({
    data: { template: { theme: {}, branding: { brandName: "Blocks" }, pages: { shared: { footerText: "" } } } },
  }),
}));

vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ captcha: {}, code: "", reset: vi.fn() }),
}));

vi.mock("@/components/captcha", () => ({ Captcha: () => null }));

vi.mock("../oidc/oidc-auth-shell", () => ({
  OidcAuthShell: ({ children, heading }: { children?: React.ReactNode; heading?: string }) => (
    <div data-testid="shell"><h1>{heading}</h1>{children}</div>
  ),
  OidcFooter: () => null,
}));

vi.mock("@/hooks/use-toast", () => ({ showErrorToast: vi.fn() }));

import InvitationPage from "./invitation";
import { NO_CREDENTIAL_YET_COPY, CONFIRM_STEP_TIMED_OUT } from "./invitation";
import { CONFIRM_PASSWORD_COPY } from "./invitation-confirm-password";

const originalLocation = globalThis.window.location;
let assign: ReturnType<typeof vi.fn>;

function renderJoin() {
  globalThis.window.history.replaceState(null, "", "/oidc/invitation/t1#link=abc123");
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/oidc/invitation/t1"]}>
        <Routes>
          <Route path="/oidc/invitation/:tenantId" element={<InvitationPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return client;
}

async function reachPasswordStep() {
  redeem.mockResolvedValue({
    error: "authentication_required",
    redemptionId: "rid-1",
    maskedEmail: "as•••@example.com",
    mode: "embedded",
  });
  const client = renderJoin();
  await waitFor(() => expect(screen.getByRole("heading", { name: CONFIRM_PASSWORD_COPY.heading })).toBeInTheDocument());
  return client;
}

function submitPassword(value: string) {
  fireEvent.change(screen.getByLabelText("Password"), { target: { value } });
  fireEvent.click(screen.getByRole("button", { name: /continue/i }));
}

describe("InvitationPage password step (#593)", () => {
  beforeEach(() => {
    getContext.mockReset();
    redeem.mockReset();
    authenticate.mockReset();
    completeMfa.mockReset();
    getContext.mockResolvedValue({
      valid: true,
      credentialMode: "Passwordless",
      firstName: "Asif",
      maskedEmail: "as•••@example.com",
      applicationName: "Construct",
    });
    assign = vi.fn();
    Object.defineProperty(globalThis.window, "location", {
      configurable: true,
      value: {
        get hash() {
          return originalLocation.hash;
        },
        get pathname() {
          return originalLocation.pathname;
        },
        get search() {
          return originalLocation.search;
        },
        get href() {
          return originalLocation.href;
        },
        get origin() {
          return originalLocation.origin;
        },
        assign,
      },
    });
  });

  afterEach(() => {
    Object.defineProperty(globalThis.window, "location", { configurable: true, value: originalLocation });
  });

  it("authentication_required shows the confirm step instead of signing in", async () => {
    await reachPasswordStep();
    expect(screen.getByTestId("masked-email")).toHaveTextContent("as•••@example.com");
    expect(assign).not.toHaveBeenCalled();
  });

  it("a correct password continues to the authorize URL", async () => {
    const client = await reachPasswordStep();
    authenticate.mockResolvedValue({ authorizeUrl: "https://iam.example/api/oidc/authorize?x=1" });
    submitPassword("Correct#1");
    await waitFor(() => expect(assign).toHaveBeenCalledWith("https://iam.example/api/oidc/authorize?x=1"));
    expect(authenticate).toHaveBeenCalledWith(
      expect.objectContaining({ redemptionId: "rid-1", password: "Correct#1" }),
      "t1",
    );
    const keys = JSON.stringify([
      ...client.getQueryCache().getAll().map((q) => q.queryKey),
      ...client.getMutationCache().getAll().map((m) => m.options.mutationKey),
    ]);
    expect(keys).not.toContain("Correct#1");
    expect(globalThis.window.location.href).not.toContain("Correct#1");
  });

  it("a correct password for an MFA user moves on to the code step", async () => {
    await reachPasswordStep();
    authenticate.mockResolvedValue({ mfaId: "mfa-1", userMfa: "email" });
    submitPassword("Correct#1");
    await waitFor(() => expect(screen.getByRole("heading", { name: "Verify it's you" })).toBeInTheDocument());
    expect(screen.getByLabelText("MFA code")).toBeInTheDocument();
  });

  it("an expired step shows the timed-out message", async () => {
    await reachPasswordStep();
    authenticate.mockRejectedValue({ errors: { error: "invalid_redemption" } });
    submitPassword("Correct#1");
    await waitFor(() => expect(screen.getByText(CONFIRM_STEP_TIMED_OUT)).toBeInTheDocument());
  });

  it("a link that stopped being valid shows the invalid view", async () => {
    await reachPasswordStep();
    authenticate.mockRejectedValue({ errors: { error: "invalid_link" } });
    submitPassword("Correct#1");
    await waitFor(() => expect(screen.getByText("This link is no longer valid.")).toBeInTheDocument());
  });

  it("password_not_set tells the user to sign in first", async () => {
    redeem.mockRejectedValue({ errors: { error: "password_not_set" } });
    renderJoin();
    await waitFor(() => expect(screen.getByText(NO_CREDENTIAL_YET_COPY)).toBeInTheDocument());
    expect(screen.getByRole("heading", { name: "Sign in first" })).toBeInTheDocument();
  });
});
