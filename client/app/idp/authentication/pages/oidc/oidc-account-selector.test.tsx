import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ showErrorToast: vi.fn(), oidcUiConfig: undefined as unknown }));

vi.mock("@/hooks/use-toast", () => ({ showErrorToast: h.showErrorToast }));
vi.mock("@blocks-idp/authentication/services/auth.service", () => ({ authService: {} }));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig }),
}));

import { OidcAccountSelector } from "./oidc-account-selector";
import { OIDC_UI_TEMPLATE_FIXTURE } from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

const accounts = [
  { user_id: "u1", tenant_id: "t1", email: "a@x.com", display_name: "Alice" },
  { user_id: "u2", tenant_id: "t2", email: "b@x.com" },
];

beforeEach(() => {
  vi.clearAllMocks();
  h.oidcUiConfig = undefined;
});

describe("OidcAccountSelector", () => {
  it("renders the loading state", () => {
    const { container } = render(
      <OidcAccountSelector accounts={accounts} onAccountSelect={vi.fn()} isLoading />,
    );
    expect(screen.getByText("Select Account")).toBeInTheDocument();
    expect(container.querySelector(".animate-spin")).not.toBeNull();
  });

  it("renders an entry per account", () => {
    render(<OidcAccountSelector accounts={accounts} onAccountSelect={vi.fn()} />);
    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.getByText("a@x.com")).toBeInTheDocument();
    expect(screen.getByText("b@x.com")).toBeInTheDocument();
  });

  it("renders tenant-defined account-selector headings", () => {
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...OIDC_UI_TEMPLATE_FIXTURE,
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          accountSelector: { heading: "Acme Identity", subheading: "Choose your workspace" },
        },
      },
    };
    render(<OidcAccountSelector accounts={accounts} onAccountSelect={vi.fn()} />);
    expect(screen.getByText("Acme Identity")).toBeInTheDocument();
    expect(screen.getByText("Choose your workspace")).toBeInTheDocument();
  });

  it("invokes onAccountSelect when an account is chosen", async () => {
    const onAccountSelect = vi.fn().mockResolvedValue(undefined);
    render(<OidcAccountSelector accounts={accounts} onAccountSelect={onAccountSelect} />);

    fireEvent.click(screen.getByText("a@x.com"));
    await waitFor(() => expect(onAccountSelect).toHaveBeenCalledWith(accounts[0]));
  });

  it("shows an error toast when selection fails", async () => {
    const onAccountSelect = vi.fn().mockRejectedValue(new Error("no access"));
    render(<OidcAccountSelector accounts={accounts} onAccountSelect={onAccountSelect} />);

    fireEvent.click(screen.getByText("b@x.com"));
    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "no access" }));
  });

  it("shows a fallback error message for a non-Error rejection", async () => {
    const onAccountSelect = vi.fn().mockRejectedValue("nope");
    render(<OidcAccountSelector accounts={accounts} onAccountSelect={onAccountSelect} />);

    fireEvent.click(screen.getByText("a@x.com"));
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Failed to select account" }),
    );
  });
});
