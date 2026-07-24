import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

const h = vi.hoisted(() => ({
  validate: vi.fn(),
  resend: vi.fn(),
  isActivationPending: false,
  isResendPending: false,
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
  OidcAuthShell: ({ heading, children }: { heading: string; children: React.ReactNode }) => (
    <div>
      <h1>{heading}</h1>
      {children}
    </div>
  ),
}));
vi.mock("../oidc/oidc-panel-config", () => ({ ACTIVATE_PANEL: {} }));

import { Activation } from "./activation";

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
