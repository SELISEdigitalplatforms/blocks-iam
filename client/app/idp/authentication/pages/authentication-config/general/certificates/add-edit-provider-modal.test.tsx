import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IGetPublicCertificateResponse } from "@blocks-identifier/models/project.model";

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

  it("shows an Edit trigger and prefills fields from existing data", async () => {
    const existing = {
      jwksUrl: "https://idp.example.com/jwks",
      issuer: "issuer-1",
      audiences: ["aud-a", "aud-b"],
      providerName: "Okta",
    } as IGetPublicCertificateResponse;

    render(<AddEditProviderModal existingData={existing} />);
    fireEvent.click(screen.getByRole("button", { name: /edit/i }));

    expect(await screen.findByText("Edit provider")).toBeInTheDocument();
    expect(screen.getByDisplayValue("https://idp.example.com/jwks")).toBeInTheDocument();
    expect(screen.getByDisplayValue("issuer-1")).toBeInTheDocument();
    expect(screen.getByDisplayValue("aud-a, aud-b")).toBeInTheDocument();
  });

  it("requires a JWKS URL before saving for a non-Others provider", async () => {
    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");

    // Dirty the form via the issuer field while leaving the URL empty.
    fireEvent.change(screen.getByLabelText(/Issuer/), {
      target: { value: "issuer-x" },
    });

    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    expect(await screen.findByText("JWKS URL is required")).toBeInTheDocument();
    expect(h.savePublicCertificates).not.toHaveBeenCalled();
  });

  it("blocks the save when the JWKS URL fails validation", async () => {
    h.validateJwksUrl.mockResolvedValue({ isValid: false, error: "bad url" });
    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");

    fireEvent.change(screen.getByLabelText("URL"), {
      target: { value: "https://bad.example.com/jwks" },
    });
    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    expect(await screen.findByText("bad url")).toBeInTheDocument();
    expect(h.savePublicCertificates).not.toHaveBeenCalled();
  });

  it("surfaces an error toast when the save is unsuccessful", async () => {
    h.validateJwksUrl.mockResolvedValue({ isValid: true });
    h.savePublicCertificates.mockResolvedValue({ isSuccess: false, errors: "server" });
    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");

    fireEvent.change(screen.getByLabelText("URL"), {
      target: { value: "https://good.example.com/jwks" },
    });
    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "server" }),
    );
  });

  it("reveals the upload-file option and password field for the Others provider", async () => {
    const user = userEvent.setup();
    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");

    await user.click(screen.getByLabelText("Others"));

    expect(await screen.findByText("Upload file")).toBeInTheDocument();
    expect(screen.getByLabelText(/Password/)).toBeInTheDocument();
    expect(
      screen.getByPlaceholderText("Enter public certificate url"),
    ).toBeInTheDocument();
  });
});
