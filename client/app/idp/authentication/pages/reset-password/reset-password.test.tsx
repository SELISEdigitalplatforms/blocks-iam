import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { OIDC_UI_TEMPLATE_FIXTURE } from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

const h = vi.hoisted(() => ({
  oidcUiConfig: undefined as unknown,
  shellProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig }),
}));
vi.mock("../oidc/oidc-auth-shell", () => ({
  OidcAuthShell: (props: Record<string, unknown>) => {
    h.shellProps = props;
    return <div><h1>{props.heading as string}</h1>{props.children as React.ReactNode}</div>;
  },
  OidcFooter: ({ footerText }: { footerText: string }) => <span>{footerText}</span>,
}));
vi.mock("../oidc/oidc-panel-config", () => ({ RESET_PASSWORD_PANEL: {} }));
vi.mock("./reset-password-form", () => ({
  ResetPasswordForm: () => <div data-testid="reset-password-form" />,
}));

import { ResetPassword } from "./reset-password";

const renderPage = () =>
  render(
    <MemoryRouter>
      <ResetPassword code="reset-code" tenantId="tenant-1" />
    </MemoryRouter>,
  );

beforeEach(() => {
  h.oidcUiConfig = undefined;
  h.shellProps = null;
});

describe("ResetPassword", () => {
  it("renders the default template copy", () => {
    renderPage();
    expect(screen.getByText("Set a new password")).toBeInTheDocument();
    expect(screen.getByTestId("reset-password-form")).toBeInTheDocument();
    expect(h.shellProps).toEqual(expect.objectContaining({
      successTitle: "Password Updated",
      successSubtitle: "Your password has been reset successfully.",
    }));
  });

  it("passes tenant-defined heading and success copy to the shell", () => {
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...OIDC_UI_TEMPLATE_FIXTURE,
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          resetPassword: {
            ...OIDC_UI_TEMPLATE_FIXTURE.pages.resetPassword,
            heading: "Choose a new Acme secret",
            successTitle: "Secret changed",
            successSubtitle: "Your Acme sessions are protected",
          },
        },
      },
    };
    renderPage();
    expect(screen.getByText("Choose a new Acme secret")).toBeInTheDocument();
    expect(h.shellProps).toEqual(expect.objectContaining({
      successTitle: "Secret changed",
      successSubtitle: "Your Acme sessions are protected",
    }));
  });
});
