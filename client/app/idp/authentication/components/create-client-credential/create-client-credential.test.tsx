import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  saveClient: vi.fn(),
  isPending: false,
  rolesResult: {} as Record<string, unknown>,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@seliseblocks/blocks-kit", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth-clients", () => ({
  useSaveAuthClient: () => ({ mutateAsync: h.saveClient, isPending: h.isPending }),
}));
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useGetRoles: () => h.rolesResult,
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { CreateClientCredential } from "./create-client-credential";

const fillValidForm = () => {
  fireEvent.input(screen.getByPlaceholderText("Enter client name"), {
    target: { value: "My Service" },
  });
  fireEvent.input(screen.getByPlaceholderText("Enter audience URL"), {
    target: { value: "https://api.example.com" },
  });
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.rolesResult = {
    data: { data: [{ slug: "admin", name: "Admin" }] },
    isLoading: false,
  };
});

describe("CreateClientCredential", () => {
  it("opens the dialog when the create trigger is clicked", async () => {
    render(<CreateClientCredential />);
    fireEvent.click(screen.getByText("Create"));
    await waitFor(() => expect(screen.getByText("New Access Token")).toBeInTheDocument());
    expect(screen.getByText("admin")).toBeInTheDocument();
  });

  it("shows the empty roles message when no roles match", async () => {
    h.rolesResult = { data: { data: [] }, isLoading: false };
    render(<CreateClientCredential />);
    fireEvent.click(screen.getByText("Create"));
    await waitFor(() => expect(screen.getByText("No roles found")).toBeInTheDocument());
  });

  it("creates a service client on submit and shows a success toast", async () => {
    h.saveClient.mockResolvedValue({ isSuccess: true });
    render(<CreateClientCredential />);
    fireEvent.click(screen.getByText("Create"));
    await waitFor(() => expect(screen.getByText("New Access Token")).toBeInTheDocument());
    fillValidForm();
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() =>
      expect(h.saveClient).toHaveBeenCalledWith(
        expect.objectContaining({ name: "My Service", projectKey: "tenant-1" }),
      ),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when creation fails", async () => {
    h.saveClient.mockResolvedValue({ isSuccess: false, error: "bad request" });
    render(<CreateClientCredential />);
    fireEvent.click(screen.getByText("Create"));
    await waitFor(() => expect(screen.getByText("New Access Token")).toBeInTheDocument());
    fillValidForm();
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "bad request" }));
  });
});
