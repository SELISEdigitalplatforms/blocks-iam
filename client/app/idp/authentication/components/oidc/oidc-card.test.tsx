import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IOidcConfig } from "@blocks-idp/authentication/models/auth.oidc.model";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "t1" } })),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth-oidc", () => ({
  useDeleteAuthOidc: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("../create-oidc/create-oidc", () => ({
  CreateOIDC: () => <div data-testid="create-oidc" />,
}));

import { OIDCCard } from "./oidc-card";

const oidc: IOidcConfig = {
  itemId: "client-123456",
  clientDisplayName: "My OIDC App",
  clientSecret: "super-secret-value",
  redirectUri: "https://app.example.com/cb",
  audience: "https://api.example.com",
  scope: "openid",
  clientBrandColor: "#124091",
  clientLogoUrl: "https://cdn.example.com/logo.png",
  createdDate: "2025-01-15T10:30:00Z",
} as IOidcConfig;

const openDeleteAndConfirm = async (container: HTMLElement) => {
  const trash = container.querySelector('[class*="hover:text-error"]') as HTMLElement;
  fireEvent.click(trash);
  const dialog = (await screen.findByText("Delete")).closest("[role='dialog']") as HTMLElement;
  fireEvent.click(within(dialog).getByRole("button", { name: /yes/i }));
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("OIDCCard", () => {
  it("renders the client details", () => {
    render(<OIDCCard oidc={oidc} />);
    expect(screen.getByText("My OIDC App")).toBeInTheDocument();
    expect(screen.getByText("Client Id")).toBeInTheDocument();
    expect(screen.getByText("Redirect URL")).toBeInTheDocument();
    expect(screen.getByText("https://app.example.com/cb")).toBeInTheDocument();
    expect(screen.getByText("openid")).toBeInTheDocument();
  });

  it("deletes the credential and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    const { container } = render(<OIDCCard oidc={oidc} />);

    await openDeleteAndConfirm(container);

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    expect(h.mutateAsync.mock.calls[0][0]).toMatchObject({
      itemId: "client-123456",
      projectKey: "t1",
    });
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "OIDC credential deleted successfully",
    });
  });

  it("shows an error toast when the delete is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, error: "cannot delete" });
    const { container } = render(<OIDCCard oidc={oidc} />);

    await openDeleteAndConfirm(container);

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "cannot delete" }));
  });

  it("shows a generic error toast when the delete throws", async () => {
    h.mutateAsync.mockRejectedValue(new Error("boom"));
    const { container } = render(<OIDCCard oidc={oidc} />);

    await openDeleteAndConfirm(container);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });

  it("renders N/A scope when no scope is set", () => {
    render(<OIDCCard oidc={{ ...oidc, scope: "" }} />);
    expect(screen.getByText("N/A")).toBeInTheDocument();
  });
});
