import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  saveMutateAsync: vi.fn(),
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "tenant-1", itemId: "p1" } })),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth-oidc", () => ({
  useGetAuthOidcCredential: vi.fn(() => ({ data: undefined, isLoading: false })),
  useSaveAuthOidc: vi.fn(() => ({ mutateAsync: h.saveMutateAsync, isPending: false })),
}));
vi.mock("@blocks-storage/hooks/use-storage-file", () => ({
  useGetPreSignedUrlForUpload: vi.fn(() => ({ mutateAsync: vi.fn() })),
  useUploadFile: vi.fn(() => ({ mutateAsync: vi.fn() })),
}));
vi.mock("@blocks-storage/services/storage.service", () => ({
  storageService: { file: { getFileByFileId: vi.fn() } },
}));

import { CreateOIDC } from "./create-oidc";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("CreateOIDC", () => {
  it("opens the create dialog with its fields and a disabled add button", async () => {
    render(<CreateOIDC />);
    fireEvent.click(screen.getByRole("button", { name: /create/i }));

    expect(await screen.findByText("New OIDC Client")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter client name")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("https://example.com/oidc")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^add$/i })).toBeDisabled();
  });

  it("saves the OIDC client with the entered values", async () => {
    h.saveMutateAsync.mockResolvedValue({ isSuccess: true });

    render(<CreateOIDC />);
    fireEvent.click(screen.getByRole("button", { name: /create/i }));
    await screen.findByText("New OIDC Client");

    fireEvent.change(screen.getByPlaceholderText("Enter client name"), {
      target: { value: "My OIDC App" },
    });
    fireEvent.change(screen.getByPlaceholderText("https://example.com/oidc"), {
      target: { value: "https://app.example.com/callback" },
    });
    fireEvent.change(screen.getByPlaceholderText("https://example.com"), {
      target: { value: "https://api.example.com" },
    });

    const add = screen.getByRole("button", { name: /^add$/i });
    await waitFor(() => expect(add).not.toBeDisabled());
    fireEvent.click(add);

    await waitFor(() => expect(h.saveMutateAsync).toHaveBeenCalled());
    const payload = h.saveMutateAsync.mock.calls[0][0];
    expect(payload).toMatchObject({
      clientDisplayName: "My OIDC App",
      redirectUri: "https://app.example.com/callback",
      audience: "https://api.example.com",
      projectKey: "tenant-1",
    });
    await waitFor(() =>
      expect(h.showSuccessToast).toHaveBeenCalledWith({
        description: "OIDC Client created successfully",
      }),
    );
  });
});
