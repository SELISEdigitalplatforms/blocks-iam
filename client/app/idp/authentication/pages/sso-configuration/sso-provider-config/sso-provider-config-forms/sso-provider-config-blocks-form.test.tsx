import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  existing: undefined as unknown,
  mutateAsync: vi.fn(),
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
vi.mock("@blocks-idp/authentication/hooks/use-sso", () => ({
  useSaveGetOIDCCredential: vi.fn(() => ({ data: h.existing })),
  useSaveOIDCCredential: vi.fn(() => ({ mutateAsync: h.mutateAsync })),
}));

import { SSOProviderConfigBlocksForm } from "./sso-provider-config-blocks-form";

const renderForm = () =>
  render(<SSOProviderConfigBlocksForm configuration={null} save={vi.fn()} />);

const existingConfig = {
  itemId: "cfg-1",
  audience: "https://aud.example.com",
  redirectUri: "https://redir.example.com",
  clientSecret: "secret-value",
  scope: "openid email",
  isAutoRedirect: true,
};

beforeEach(() => {
  vi.clearAllMocks();
  h.existing = existingConfig;
});

describe("SSOProviderConfigBlocksForm", () => {
  it("renders the general card and save button with prefilled values", () => {
    renderForm();
    expect(screen.getByText("General")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
    expect(screen.getByDisplayValue("https://aud.example.com")).toBeInTheDocument();
    expect(screen.getByDisplayValue("cfg-1")).toBeInTheDocument();
  });

  it("saves the configuration and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderForm();

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    expect(h.mutateAsync.mock.calls[0][0]).toMatchObject({
      redirectUri: "https://redir.example.com",
      audience: "https://aud.example.com",
      scope: "openid email",
      isAutoRedirect: true,
      itemId: "cfg-1",
      projectKey: "t1",
    });
    await waitFor(() =>
      expect(h.showSuccessToast).toHaveBeenCalledWith({
        description: "Blocks OIDC is configured successfully",
      }),
    );
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "save failed" });
    renderForm();

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "save failed" }));
  });
});
