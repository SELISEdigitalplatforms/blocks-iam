import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ showErrorToast: vi.fn() }));

vi.mock("@/hooks/use-toast", () => ({ showErrorToast: h.showErrorToast }));
vi.mock("@blocks-idp/authentication/services/auth.service", () => ({ authService: {} }));

import { OidcAccountSelector } from "./oidc-account-selector";

const accounts = [
  { user_id: "u1", tenant_id: "t1", email: "a@x.com", display_name: "Alice" },
  { user_id: "u2", tenant_id: "t2", email: "b@x.com" },
];

beforeEach(() => {
  vi.clearAllMocks();
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
