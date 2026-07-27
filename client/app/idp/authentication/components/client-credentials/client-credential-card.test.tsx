import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IClientCredentialsConfig } from "@blocks-idp/authentication/models/auth.oidc.model";

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
vi.mock("@blocks-idp/authentication/hooks/use-auth-clients", () => ({
  useDeleteAuthClient: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));

import { ClientCredentialsCard } from "./client-credential-card";

const credential: IClientCredentialsConfig = {
  itemId: "cc-123456",
  name: "CI Pipeline",
  clientSecret: "secret-value",
  isActive: true,
  audiences: ["https://api.example.com"],
  roles: ["admin"],
  createdDate: "2025-02-01T09:00:00Z",
} as IClientCredentialsConfig;

const openDeleteAndConfirm = async () => {
  fireEvent.click(screen.getByRole("button", { name: "Delete" }));
  const dialog = (await screen.findAllByText("Delete")).map((n) => n.closest("[role='dialog']")).find(Boolean) as HTMLElement;
  fireEvent.click(within(dialog).getByRole("button", { name: /yes/i }));
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("ClientCredentialsCard", () => {
  it("renders the credential details and active badge", () => {
    render(<ClientCredentialsCard clientCredential={credential} />);
    expect(screen.getByText("CI Pipeline")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("Client Id")).toBeInTheDocument();
    expect(screen.getByText("admin")).toBeInTheDocument();
  });

  it("renders N/A when there are no roles or audiences", () => {
    render(
      <ClientCredentialsCard clientCredential={{ ...credential, roles: [], audiences: [] }} />,
    );
    expect(screen.getAllByText("N/A").length).toBeGreaterThan(0);
  });

  it("deletes the credential and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<ClientCredentialsCard clientCredential={credential} />);

    await openDeleteAndConfirm();

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    expect(h.mutateAsync.mock.calls[0][0]).toMatchObject({ itemId: "cc-123456", projectKey: "t1" });
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "Client credential deleted successfully",
    });
  });

  it("shows an error toast when the delete is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, error: "denied" });
    render(<ClientCredentialsCard clientCredential={credential} />);

    await openDeleteAndConfirm();

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "denied" }));
  });

  it("shows a generic error toast when the delete throws", async () => {
    h.mutateAsync.mockRejectedValue(new Error("boom"));
    render(<ClientCredentialsCard clientCredential={credential} />);

    await openDeleteAndConfirm();

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });
});
