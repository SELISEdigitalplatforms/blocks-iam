import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  saveMutateAsync: vi.fn(),
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
  getPreSign: vi.fn(),
  uploadFile: vi.fn(),
  getFileByFileId: vi.fn(),
  existingOidc: undefined as unknown,
  isLoadingOidc: false,
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "tenant-1", itemId: "p1" } })),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth-oidc", () => ({
  useGetAuthOidcCredential: vi.fn(() => ({ data: h.existingOidc, isLoading: h.isLoadingOidc })),
  useSaveAuthOidc: vi.fn(() => ({ mutateAsync: h.saveMutateAsync, isPending: false })),
}));
vi.mock("@blocks-storage/hooks/use-storage-file", () => ({
  useGetPreSignedUrlForUpload: vi.fn(() => ({ mutateAsync: h.getPreSign })),
  useUploadFile: vi.fn(() => ({ mutateAsync: h.uploadFile })),
}));
vi.mock("@blocks-storage/services/storage.service", () => ({
  storageService: { file: { getFileByFileId: (...args: unknown[]) => h.getFileByFileId(...args) } },
}));

import { CreateOIDC } from "./create-oidc";

const fillValid = () => {
  fireEvent.change(screen.getByPlaceholderText("Enter client name"), {
    target: { value: "My OIDC App" },
  });
  fireEvent.change(screen.getByPlaceholderText("https://example.com/oidc"), {
    target: { value: "https://app.example.com/callback" },
  });
  fireEvent.change(screen.getByPlaceholderText("https://example.com"), {
    target: { value: "https://api.example.com" },
  });
};

beforeEach(() => {
  vi.clearAllMocks();
  h.existingOidc = undefined;
  h.isLoadingOidc = false;
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
    fillValid();

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

  it("prefills and updates an existing OIDC client in edit mode", async () => {
    h.existingOidc = {
      oIDCClientCredential: {
        redirectUri: "https://edit.example.com/cb",
        audience: "https://edit.example.com",
        scope: "openid",
        clientBrandColor: "#abcdef",
        clientDisplayName: "Existing Client",
        clientLogoUrl: "https://cdn.example.com/logo.png",
      },
    };
    h.saveMutateAsync.mockResolvedValue({ isSuccess: true });

    render(<CreateOIDC itemId="client-9" />);
    fireEvent.click(screen.getByRole("button"));

    expect(await screen.findByText("Edit OIDC Client")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Existing Client")).toBeInTheDocument();
    expect(screen.getByDisplayValue("https://edit.example.com/cb")).toBeInTheDocument();

    // Dirty the form so the Update button enables.
    fireEvent.change(screen.getByPlaceholderText("Enter client name"), {
      target: { value: "Renamed Client" },
    });
    const update = screen.getByRole("button", { name: /update/i });
    await waitFor(() => expect(update).not.toBeDisabled());
    fireEvent.click(update);

    await waitFor(() => expect(h.saveMutateAsync).toHaveBeenCalled());
    expect(h.saveMutateAsync.mock.calls[0][0]).toMatchObject({ itemId: "client-9" });
    await waitFor(() =>
      expect(h.showSuccessToast).toHaveBeenCalledWith({
        description: "OIDC Client updated successfully",
      }),
    );
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    h.saveMutateAsync.mockResolvedValue({ isSuccess: false, error: "conflict" });

    render(<CreateOIDC />);
    fireEvent.click(screen.getByRole("button", { name: /create/i }));
    await screen.findByText("New OIDC Client");
    fillValid();
    const add = screen.getByRole("button", { name: /^add$/i });
    await waitFor(() => expect(add).not.toBeDisabled());
    fireEvent.click(add);

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "conflict" }));
  });

  it("shows the mapped error toast when the save throws with errors", async () => {
    h.saveMutateAsync.mockRejectedValue({ errors: { redirectUri: "bad" } });

    render(<CreateOIDC />);
    fireEvent.click(screen.getByRole("button", { name: /create/i }));
    await screen.findByText("New OIDC Client");
    fillValid();
    const add = screen.getByRole("button", { name: /^add$/i });
    await waitFor(() => expect(add).not.toBeDisabled());
    fireEvent.click(add);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: { redirectUri: "bad" } }),
    );
  });

  it("shows a generic error toast when the save throws a plain error", async () => {
    h.saveMutateAsync.mockRejectedValue(new Error("network"));

    render(<CreateOIDC />);
    fireEvent.click(screen.getByRole("button", { name: /create/i }));
    await screen.findByText("New OIDC Client");
    fillValid();
    const add = screen.getByRole("button", { name: /^add$/i });
    await waitFor(() => expect(add).not.toBeDisabled());
    fireEvent.click(add);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });

  it("rejects an invalid logo file type", async () => {
    render(<CreateOIDC />);
    fireEvent.click(screen.getByRole("button", { name: /create/i }));
    await screen.findByText("New OIDC Client");

    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const bad = new File(["x"], "notes.txt", { type: "text/plain" });
    fireEvent.change(fileInput, { target: { files: [bad] } });

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({
        errors: "Invalid file type. Only JPG, JPEG, PNG, GIF, and WEBP files are allowed.",
      }),
    );
    expect(h.getPreSign).not.toHaveBeenCalled();
  });

  it("rejects a logo file over the size limit", async () => {
    render(<CreateOIDC />);
    fireEvent.click(screen.getByRole("button", { name: /create/i }));
    await screen.findByText("New OIDC Client");

    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const big = new File(["x"], "logo.png", { type: "image/png" });
    Object.defineProperty(big, "size", { value: 6 * 1024 * 1024 });
    fireEvent.change(fileInput, { target: { files: [big] } });

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Image size must be under 5 MB." }),
    );
  });

  it("uploads a logo and lets it be removed", async () => {
    h.getPreSign.mockResolvedValue({ isSuccess: true, uploadUrl: "https://up", fileId: "f1" });
    h.uploadFile.mockResolvedValue(undefined);
    h.getFileByFileId.mockResolvedValue({ url: "https://cdn.example.com/new-logo.png" });

    render(<CreateOIDC />);
    fireEvent.click(screen.getByRole("button", { name: /create/i }));
    await screen.findByText("New OIDC Client");

    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const good = new File(["x"], "logo.png", { type: "image/png" });
    fireEvent.change(fileInput, { target: { files: [good] } });

    await waitFor(() => expect(h.getFileByFileId).toHaveBeenCalled());
    expect(await screen.findByAltText("OIDC Logo")).toBeInTheDocument();
    await waitFor(() =>
      expect(h.showSuccessToast).toHaveBeenCalledWith({ description: "Logo uploaded successfully" }),
    );

    fireEvent.click(screen.getByRole("button", { name: /remove/i }));
    await waitFor(() => expect(screen.queryByAltText("OIDC Logo")).toBeNull());
  });

  it("shows a generic error when the presign fails during logo upload", async () => {
    h.getPreSign.mockResolvedValue({ isSuccess: false });

    render(<CreateOIDC />);
    fireEvent.click(screen.getByRole("button", { name: /create/i }));
    await screen.findByText("New OIDC Client");

    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const good = new File(["x"], "logo.png", { type: "image/png" });
    fireEvent.change(fileInput, { target: { files: [good] } });

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({
        errors: "Something went wrong uploading logo",
      }),
    );
  });

  it("closes the dialog from the Cancel button", async () => {
    render(<CreateOIDC />);
    fireEvent.click(screen.getByRole("button", { name: /create/i }));
    await screen.findByText("New OIDC Client");

    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    await waitFor(() => expect(screen.queryByText("New OIDC Client")).toBeNull());
  });
});
