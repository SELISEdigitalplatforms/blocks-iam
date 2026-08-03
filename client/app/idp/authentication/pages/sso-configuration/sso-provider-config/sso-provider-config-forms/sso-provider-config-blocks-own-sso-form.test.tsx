import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-sso", () => ({
  useSaveSsoCredential: () => ({ mutateAsync: h.mutateAsync }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));
vi.mock("./sso-provider-config-form-fields", () => ({
  SSOProviderConfigFormField: () => <div data-testid="config-fields" />,
}));

import { SSOProviderConfigOwnSSOForm } from "./sso-provider-config-blocks-own-sso-form";

const validConfig = {
  provider: "ownsso",
  audience: "https://aud.test",
  clientId: "client-id",
  clientSecret: "client-secret",
  redirectUrl: "https://cb.test",
  wellKnownUrl: "https://well-known.test",
} as never;

beforeEach(() => {
  vi.clearAllMocks();
});

describe("SSOProviderConfigOwnSSOForm", () => {
  it("renders the general config card with a save button", () => {
    render(<SSOProviderConfigOwnSSOForm configuration={validConfig} />);
    expect(screen.getByText("General")).toBeInTheDocument();
    expect(screen.getByTestId("config-fields")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
  });

  it("saves the SSO credential and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    const { container } = render(<SSOProviderConfigOwnSSOForm configuration={validConfig} />);
    fireEvent.submit(container.querySelector("form") as HTMLFormElement);
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ clientId: "client-id", ssoType: 1, projectKey: "t1" }),
      ),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when saving fails", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "nope" });
    const { container } = render(<SSOProviderConfigOwnSSOForm configuration={validConfig} />);
    fireEvent.submit(container.querySelector("form") as HTMLFormElement);
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "nope" }));
  });
});
