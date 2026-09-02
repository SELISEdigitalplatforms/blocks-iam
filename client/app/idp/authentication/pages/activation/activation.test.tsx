import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";

const h = vi.hoisted(() => ({
  validate: vi.fn(),
  resend: vi.fn(),
  isActivationPending: false,
  isResendPending: false,
  oidcUiConfig: undefined as unknown,
  shellProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountActivationCodeExpiration: () => ({
    isPending: h.isActivationPending,
    mutateAsync: h.validate,
  }),
  useAccountResendActivation: () => ({
    mutateAsync: h.resend,
    isPending: h.isResendPending,
  }),
}));
vi.mock("./activation-form", () => ({
  ActivationForm: ({ code }: { code: string }) => <div data-testid="activation-form">{code}</div>,
}));
vi.mock("../oidc/oidc-auth-shell", () => ({
  OidcAuthShell: (props: Record<string, unknown>) => {
    h.shellProps = props;
    return <div><h1>{props.heading as string}</h1>{props.children as React.ReactNode}</div>;
  },
  OidcFooter: ({ footerText }: { footerText: string }) => <span>{footerText}</span>,
}));
vi.mock("../oidc/oidc-panel-config", () => ({ ACTIVATE_PANEL: {} }));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig }),
}));

import { Activation } from "./activation";
import {
  DEFAULT_OIDC_UI_TEMPLATE_FIXTURE,
  OIDC_UI_TEMPLATE_FIXTURE,
} from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

const renderCmp = (props: Parameters<typeof Activation>[0] = {}) =>
  render(
    <MemoryRouter>
      <Activation {...props} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.isActivationPending = false;
  h.isResendPending = false;
  h.oidcUiConfig = { captcha: null, template: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE };
  h.shellProps = null;
});

describe("Activation", () => {
  it("shows the invalid state when no code is supplied", async () => {
    renderCmp({});
    await waitFor(() => expect(screen.getByText("Invalid Activation Link")).toBeInTheDocument());
    expect(screen.getByText("Back to login")).toBeInTheDocument();
  });

  it("renders the activation form when the code validates", async () => {
    h.validate.mockResolvedValue({ isSuccess: true, userId: "u1" });
    renderCmp({ code: "good", tenantId: "t1" });
    await waitFor(() => expect(screen.getByTestId("activation-form")).toHaveTextContent("good"));
  });

  it("passes tenant-defined activation and success copy to the shell", async () => {
    h.validate.mockResolvedValue({ isSuccess: true, userId: "u1" });
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...OIDC_UI_TEMPLATE_FIXTURE,
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          activation: {
            ...OIDC_UI_TEMPLATE_FIXTURE.pages.activation,
            heading: "Enable Acme account",
            successTitle: "Acme account enabled",
            successSubtitle: "You can now continue",
          },
        },
      },
    };
    renderCmp({ code: "good", tenantId: "t1" });
    await waitFor(() => expect(screen.getByText("Enable Acme account")).toBeInTheDocument());
    expect(h.shellProps).toEqual(expect.objectContaining({
      successTitle: "Acme account enabled",
      successSubtitle: "You can now continue",
    }));
  });

  it("shows the expired state and resends the activation link", async () => {
    h.validate.mockResolvedValue({ isSuccess: false, userId: "u9" });
    h.resend.mockResolvedValue({ isSuccess: true });
    renderCmp({ code: "old", tenantId: "t1" });
    await waitFor(() => expect(screen.getByText("Link Expired")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Resend activation link" }));
    await waitFor(() =>
      expect(h.resend).toHaveBeenCalledWith({ userId: "u9", tenantId: "t1" }),
    );
    await waitFor(() =>
      expect(
        screen.getByText("A new activation link has been sent to your email."),
      ).toBeInTheDocument(),
    );
  });

  it("marks the code invalid when validation reports errors", async () => {
    h.validate.mockResolvedValue({ errors: { code: "bad" } });
    renderCmp({ code: "bad", tenantId: "t1" });
    await waitFor(() => expect(screen.getByText("Invalid Activation Link")).toBeInTheDocument());
  });
});
