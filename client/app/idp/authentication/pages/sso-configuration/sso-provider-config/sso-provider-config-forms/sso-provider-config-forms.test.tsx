import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => {
  const React = require("react");
  // Each provider form is stubbed to a button that invokes the injected save
  // handler. Defined inside vi.hoisted so the mock factories can reference it.
  const stubForm = (label: string) => {
    const StubForm = ({ save }: { save: (data: unknown) => void }) =>
      React.createElement(
        "button",
        {
          type: "button",
          onClick: () =>
            save({
              audience: "aud",
              clientId: "cid",
              clientSecret: "secret",
              userPermissions: [{ resource: "res-1" }],
              userRoles: [{ slug: "role-1" }],
              provider: label,
              redirectUrl: "https://app/cb",
            }),
        },
        `save-${label}`,
      );
    StubForm.displayName = `StubForm(${label})`;
    return StubForm;
  };
  return {
    stubForm,
    useGetSsoCredentialById: vi.fn(),
    useSaveSsoCredential: vi.fn(),
    mutateAsync: vi.fn(),
    navigate: vi.fn(),
    showErrorToast: vi.fn(),
    showSuccessToast: vi.fn(),
  };
});
vi.mock("./sso-provider-config-google-form", () => ({
  SSOProviderConfigGoogleForm: h.stubForm("google"),
}));
vi.mock("./sso-provider-config-github-form", () => ({
  SSOProviderConfigGithubForm: h.stubForm("github"),
}));
vi.mock("./sso-provider-config-linkedin-form", () => ({
  SSOProviderConfigLinkedINForm: h.stubForm("linkedin"),
}));
vi.mock("./sso-provider-config-microsoft-form", () => ({
  SSOProviderConfigMicrosoftForm: h.stubForm("microsoft"),
}));
vi.mock("./sso-provider-config-x-form", () => ({
  SSOProviderConfigXForm: h.stubForm("x"),
}));
vi.mock("./sso-provider-config-blocks-own-sso-form", () => ({
  SSOProviderConfigOwnSSOForm: h.stubForm("ownsso"),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "tenant-1" } })),
}));
vi.mock("@blocks-idp/authentication/hooks/use-sso", () => ({
  useGetSsoCredentialById: h.useGetSsoCredentialById,
  useSaveSsoCredential: h.useSaveSsoCredential,
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: h.showErrorToast,
  showSuccessToast: h.showSuccessToast,
}));
vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/hooks/use-scoped-path", () => ({
  useScopedPath: () => (segment: string) => `/scoped/${segment}`,
}));

import { SsoProviderConfigForms } from "./sso-provider-config-forms";
import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";

const renderForms = (provider: SSO_PROVIDERS, id = "") =>
  render(
    <MemoryRouter>
      <SsoProviderConfigForms provider={provider} id={id} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.useGetSsoCredentialById.mockReturnValue({ data: null });
  h.useSaveSsoCredential.mockReturnValue({ mutateAsync: h.mutateAsync });
});

describe("SsoProviderConfigForms", () => {
  it("renders the matching form for each known provider", () => {
    const { rerender } = renderForms(SSO_PROVIDERS.google);
    expect(screen.getByText("save-google")).toBeInTheDocument();

    rerender(
      <MemoryRouter>
        <SsoProviderConfigForms provider={SSO_PROVIDERS.microsoft} id="" />
      </MemoryRouter>,
    );
    expect(screen.getByText("save-microsoft")).toBeInTheDocument();

    rerender(
      <MemoryRouter>
        <SsoProviderConfigForms provider={SSO_PROVIDERS.ownsso} id="" />
      </MemoryRouter>,
    );
    expect(screen.getByText("save-ownsso")).toBeInTheDocument();
  });

  it("renders nothing for a provider without a form (apple)", () => {
    const { container } = renderForms(SSO_PROVIDERS.apple);
    expect(container).toBeEmptyDOMElement();
  });

  it("saves and navigates to the new config when there is no id", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true, itemId: "new-id" });
    renderForms(SSO_PROVIDERS.github, "");

    fireEvent.click(screen.getByText("save-github"));

    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          itemId: "",
          clientId: "cid",
          initialPermissions: ["res-1"],
          initialRoles: ["role-1"],
          projectKey: "tenant-1",
        }),
      ),
    );
    expect(h.navigate).toHaveBeenCalledWith(
      "/scoped/sso-configuration?provider=github&id=new-id",
    );
    expect(h.showSuccessToast).toHaveBeenCalled();
  });

  it("does not navigate on save when editing an existing id", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true, itemId: "existing" });
    renderForms(SSO_PROVIDERS.x, "existing");

    fireEvent.click(screen.getByText("save-x"));

    await waitFor(() => expect(h.showSuccessToast).toHaveBeenCalled());
    expect(h.navigate).not.toHaveBeenCalled();
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "server error" });
    renderForms(SSO_PROVIDERS.linkedin, "");

    fireEvent.click(screen.getByText("save-linkedin"));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "server error" }),
    );
    expect(h.showSuccessToast).not.toHaveBeenCalled();
  });
});
