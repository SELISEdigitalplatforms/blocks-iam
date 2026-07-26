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

  it("saves an Others URL as a jwksUrl when it validates", async () => {
    const user = userEvent.setup();
    h.validateJwksUrl.mockResolvedValue({ isValid: true });
    h.savePublicCertificates.mockResolvedValue({ isSuccess: true });

    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");
    await user.click(screen.getByLabelText("Others"));

    fireEvent.change(screen.getByPlaceholderText("Enter public certificate url"), {
      target: { value: "https://others.example.com/jwks" },
    });
    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.savePublicCertificates).toHaveBeenCalled());
    expect(h.savePublicCertificates.mock.calls[0][0]).toMatchObject({
      jwksUrl: "https://others.example.com/jwks",
      publicCertificatePath: "",
      providerName: "Others",
    });
  });

  it("saves an Others URL as a certificate path when it does not validate", async () => {
    const user = userEvent.setup();
    h.validateJwksUrl.mockResolvedValue({ isValid: false });
    h.savePublicCertificates.mockResolvedValue({ isSuccess: true });

    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");
    await user.click(screen.getByLabelText("Others"));

    fireEvent.change(screen.getByPlaceholderText("Enter public certificate url"), {
      target: { value: "https://others.example.com/cert.pem" },
    });
    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.savePublicCertificates).toHaveBeenCalled());
    expect(h.savePublicCertificates.mock.calls[0][0]).toMatchObject({
      jwksUrl: "",
      publicCertificatePath: "https://others.example.com/cert.pem",
    });
  });

  it("treats an Others URL as a certificate path when validation throws", async () => {
    const user = userEvent.setup();
    h.validateJwksUrl.mockRejectedValue(new Error("network"));
    h.savePublicCertificates.mockResolvedValue({ isSuccess: true });

    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");
    await user.click(screen.getByLabelText("Others"));

    fireEvent.change(screen.getByPlaceholderText("Enter public certificate url"), {
      target: { value: "https://others.example.com/broken" },
    });
    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.savePublicCertificates).toHaveBeenCalled());
    expect(h.savePublicCertificates.mock.calls[0][0]).toMatchObject({
      publicCertificatePath: "https://others.example.com/broken",
    });
  });

  it("toggles the Others password field visibility", async () => {
    const user = userEvent.setup();
    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");
    await user.click(screen.getByLabelText("Others"));

    const passwordInput = screen.getByPlaceholderText("********") as HTMLInputElement;
    expect(passwordInput.type).toBe("password");
    // The reveal toggle is the ghost button adjacent to the password input.
    const toggle = passwordInput.parentElement?.querySelector("button");
    fireEvent.click(toggle as HTMLButtonElement);
    expect(passwordInput.type).toBe("text");
  });

  it("surfaces an error toast when the public-url save throws", async () => {
    h.validateJwksUrl.mockResolvedValue({ isValid: true });
    h.savePublicCertificates.mockRejectedValue(new Error("boom"));

    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");
    fireEvent.change(screen.getByLabelText("URL"), {
      target: { value: "https://idp.example.com/jwks" },
    });
    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalled());
    expect(h.savePublicCertificates).toHaveBeenCalled();
  });

  it("shows an error when submitting the upload-file method without a file", async () => {
    const user = userEvent.setup();
    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");
    await user.click(screen.getByLabelText("Others"));

    // Dirty the form so Save is enabled, then switch to the upload method.
    fireEvent.change(screen.getByLabelText(/Issuer/), { target: { value: "iss" } });
    await user.click(screen.getByLabelText("Upload file"));

    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Please upload a certificate file" }),
    );
    expect(h.savePublicCertificates).not.toHaveBeenCalled();
  });

  it("uploads a certificate file and saves the returned path", async () => {
    const user = userEvent.setup();
    h.uploadFile.mockResolvedValue({ downloadUrl: "https://cdn.example.com/cert.crt" });
    h.savePublicCertificates.mockResolvedValue({ isSuccess: true });

    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");
    await user.click(screen.getByLabelText("Others"));
    fireEvent.change(screen.getByLabelText(/Issuer/), { target: { value: "iss" } });
    await user.click(screen.getByLabelText("Upload file"));

    await waitFor(() =>
      expect(document.querySelector('input[type="file"]')).not.toBeNull(),
    );
    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(["cert-bytes"], "server.crt", { type: "application/x-x509-ca-cert" });
    fireEvent.change(fileInput, { target: { files: [file] } });

    await screen.findByText("server.crt");
    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.uploadFile).toHaveBeenCalled());
    await waitFor(() => expect(h.savePublicCertificates).toHaveBeenCalled());
    expect(h.savePublicCertificates.mock.calls[0][0]).toMatchObject({
      publicCertificatePath: "https://cdn.example.com/cert.crt",
      jwksUrl: "",
    });
    await waitFor(() => expect(h.showSuccessToast).toHaveBeenCalled());
  });

  it("resets the form when the dialog is closed via Cancel", async () => {
    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");
    fireEvent.change(screen.getByLabelText("URL"), { target: { value: "https://x.example.com" } });

    fireEvent.click(screen.getAllByRole("button", { name: /cancel/i })[0]);
    await waitFor(() => expect(screen.queryByText("Add provider")).not.toBeInTheDocument());
  });

  it("resets the certificate method when switching away from Others", async () => {
    const user = userEvent.setup();
    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");

    await user.click(screen.getByLabelText("Others"));
    await user.click(screen.getByLabelText("Upload file"));
    expect(await screen.findByText("Click to upload or drag and drop")).toBeInTheDocument();

    // Switching back to a non-Others provider forces public-url and clears files.
    await user.click(screen.getByLabelText("Keycloak"));
    await waitFor(() => expect(screen.getByLabelText("URL")).toBeInTheDocument());
  });

  it("splits comma-separated audiences into a list when saving", async () => {
    h.validateJwksUrl.mockResolvedValue({ isValid: true });
    h.savePublicCertificates.mockResolvedValue({ isSuccess: true });
    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");

    fireEvent.change(screen.getByLabelText("URL"), {
      target: { value: "https://idp.example.com/jwks" },
    });
    fireEvent.change(screen.getByLabelText(/Audience/), {
      target: { value: "aud-1, aud-2 , " },
    });
    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.savePublicCertificates).toHaveBeenCalled());
    expect(h.savePublicCertificates.mock.calls[0][0].audiences).toEqual(["aud-1", "aud-2"]);
  });

  it("shows an error toast when the upload-file save is unsuccessful", async () => {
    const user = userEvent.setup();
    h.uploadFile.mockResolvedValue({ downloadUrl: "https://cdn.example.com/cert.crt" });
    h.savePublicCertificates.mockResolvedValue({ isSuccess: false, errors: "denied" });

    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");
    await user.click(screen.getByLabelText("Others"));
    fireEvent.change(screen.getByLabelText(/Issuer/), { target: { value: "iss" } });
    await user.click(screen.getByLabelText("Upload file"));

    await waitFor(() => expect(document.querySelector('input[type="file"]')).not.toBeNull());
    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(["b"], "server.crt", { type: "application/x-x509-ca-cert" });
    fireEvent.change(fileInput, { target: { files: [file] } });
    await screen.findByText("server.crt");

    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "denied" }));
  });

  it("surfaces an error toast when the upload-file save throws", async () => {
    const user = userEvent.setup();
    h.uploadFile.mockResolvedValue({ downloadUrl: "https://cdn.example.com/cert.crt" });
    h.savePublicCertificates.mockRejectedValue(new Error("upload-boom"));

    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");
    await user.click(screen.getByLabelText("Others"));
    fireEvent.change(screen.getByLabelText(/Issuer/), { target: { value: "iss" } });
    await user.click(screen.getByLabelText("Upload file"));

    await waitFor(() => expect(document.querySelector('input[type="file"]')).not.toBeNull());
    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(["b"], "server.crt", { type: "application/x-x509-ca-cert" });
    fireEvent.change(fileInput, { target: { files: [file] } });
    await screen.findByText("server.crt");

    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalled());
    expect(h.uploadFile).toHaveBeenCalled();
  });

  it("shows an error when the upload response has no download url", async () => {
    const user = userEvent.setup();
    h.uploadFile.mockResolvedValue({});

    render(<AddEditProviderModal />);
    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    await screen.findByText("Add provider");
    await user.click(screen.getByLabelText("Others"));
    fireEvent.change(screen.getByLabelText(/Issuer/), { target: { value: "iss" } });
    await user.click(screen.getByLabelText("Upload file"));

    await waitFor(() =>
      expect(document.querySelector('input[type="file"]')).not.toBeNull(),
    );
    const fileInput = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(["cert-bytes"], "server.crt", { type: "application/x-x509-ca-cert" });
    fireEvent.change(fileInput, { target: { files: [file] } });
    await screen.findByText("server.crt");

    const save = screen.getByRole("button", { name: /save/i });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Failed to get upload URL" }),
    );
    expect(h.savePublicCertificates).not.toHaveBeenCalled();
  });
});
