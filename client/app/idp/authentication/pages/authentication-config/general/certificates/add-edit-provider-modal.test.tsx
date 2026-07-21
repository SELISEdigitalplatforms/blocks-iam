import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  savePublicCertificates: vi.fn(),
  validateJwksUrl: vi.fn(),
  uploadFile: vi.fn(),
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "tenant-1" } })),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@blocks-idp/authentication/hooks/use-identifier", () => ({
  useSavePublicCertificates: vi.fn(() => ({ mutateAsync: h.savePublicCertificates })),
  useValidateJwksUrl: vi.fn(() => ({ mutateAsync: h.validateJwksUrl })),
}));
vi.mock("@blocks-storage/hooks/use-storage-file", () => ({
  usePublicCertificateFile: vi.fn(() => ({ mutateAsync: h.uploadFile })),
}));

import { AddEditProviderModal } from "./add-edit-provider-modal";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("AddEditProviderModal", () => {
  it("opens the add dialog with provider options and a disabled save button", async () => {
    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));

    expect(await screen.findByText("Add provider")).toBeInTheDocument();
    expect(screen.getByText("Public URL")).toBeInTheDocument();
    expect(screen.getByLabelText("URL")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /save/i })).toBeDisabled();
  });

  it("validates the JWKS URL and saves the public certificate", async () => {
    h.validateJwksUrl.mockResolvedValue({ isValid: true });
    h.savePublicCertificates.mockResolvedValue({ isSuccess: true });

    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");

    fireEvent.change(screen.getByLabelText("URL"), {
      target: { value: "https://idp.example.com/jwks" },
    });

    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() =>
      expect(h.validateJwksUrl).toHaveBeenCalledWith("https://idp.example.com/jwks"),
    );
    await waitFor(() => expect(h.savePublicCertificates).toHaveBeenCalled());
    expect(h.savePublicCertificates.mock.calls[0][0]).toMatchObject({
      projectKey: "tenant-1",
      jwksUrl: "https://idp.example.com/jwks",
      providerName: "Keycloak",
    });
    await waitFor(() =>
      expect(h.showSuccessToast).toHaveBeenCalledWith({
        description: "Public certificate saved successfully.",
      }),
    );
  });
});
