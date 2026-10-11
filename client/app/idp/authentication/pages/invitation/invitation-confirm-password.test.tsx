import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router";

const authenticate = vi.fn();
const resetCaptcha = vi.fn();
const captchaState = { code: "" };

vi.mock("@blocks-idp/authentication/services/signup-link.service", () => ({
  signupLinkService: {
    authenticate: (...a: unknown[]) => authenticate(...a),
  },
}));

vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: { captcha: { key: "cfg-key", provider: "recaptcha" } } }),
}));

vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ captcha: {}, code: captchaState.code, reset: resetCaptcha }),
}));

vi.mock("@/components/captcha", () => ({
  Captcha: () => <div data-testid="captcha" />,
}));

import {
  CONFIRM_PASSWORD_COPY,
  InvitationConfirmPassword,
} from "./invitation-confirm-password";

function renderStep(overrides: Partial<{ onSuccess: () => void; onExit: () => void }> = {}) {
  const onSuccess = overrides.onSuccess ?? vi.fn();
  const onExit = overrides.onExit ?? vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const view = render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/oidc/invitation/t1"]}>
        <InvitationConfirmPassword
          tenantId="t1"
          redemptionId="rid-1"
          maskedEmail="as•••@example.com"
          onSuccess={onSuccess}
          onExit={onExit}
        />
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return { ...view, onSuccess, onExit, client };
}

const passwordInput = () => screen.getByLabelText("Password") as HTMLInputElement;
const submit = () => fireEvent.click(screen.getByRole("button", { name: /continue/i }));

function typeAndSubmit(value: string) {
  fireEvent.change(passwordInput(), { target: { value } });
  submit();
}

describe("InvitationConfirmPassword", () => {
  beforeEach(() => {
    authenticate.mockReset();
    resetCaptcha.mockReset();
    captchaState.code = "";
  });

  it("shows the masked email, focuses the password and links to forgot password", () => {
    renderStep();
    expect(screen.getByTestId("masked-email")).toHaveTextContent("as•••@example.com");
    expect(passwordInput()).toHaveFocus();
    expect(passwordInput()).toHaveAttribute("autocomplete", "current-password");
    expect(screen.getByRole("link", { name: "Forgot password?" })).toBeInTheDocument();
  });

  it("toggles password visibility from a labelled button", () => {
    renderStep();
    expect(passwordInput().type).toBe("password");
    fireEvent.click(screen.getByRole("button", { name: "Show password" }));
    expect(passwordInput().type).toBe("text");
    fireEvent.click(screen.getByRole("button", { name: "Hide password" }));
    expect(passwordInput().type).toBe("password");
  });

  it("does not call the server for an empty password", async () => {
    renderStep();
    submit();
    await waitFor(() => expect(screen.getByText("Enter your password.")).toBeInTheDocument());
    expect(authenticate).not.toHaveBeenCalled();
  });

  it("sends the redemption id and password in the body and hands the result back", async () => {
    authenticate.mockResolvedValue({ authorizeUrl: "https://iam.example/authorize" });
    const { onSuccess, client } = renderStep();
    typeAndSubmit("Correct#1");
    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    expect(authenticate).toHaveBeenCalledWith(
      { redemptionId: "rid-1", password: "Correct#1", captchaCode: undefined },
      "t1",
    );
    const keys = JSON.stringify(client.getMutationCache().getAll().map((m) => m.options.mutationKey));
    expect(keys).not.toContain("Correct#1");
    expect(window.location.href).not.toContain("Correct#1");
  });

  it("shows a pending state while verifying", async () => {
    authenticate.mockReturnValue(new Promise(() => {}));
    renderStep();
    typeAndSubmit("Correct#1");
    await waitFor(() => expect(screen.getByText("Verifying…")).toBeInTheDocument());
    expect(passwordInput()).toBeDisabled();
  });

  it("wrong password: announces the error, clears the field and marks it invalid", async () => {
    authenticate.mockRejectedValue({ errors: { error: "invalid_username_password" } });
    renderStep();
    typeAndSubmit("nope");
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(CONFIRM_PASSWORD_COPY.wrongCredential);
    expect(passwordInput().value).toBe("");
    expect(passwordInput()).toHaveAttribute("aria-invalid", "true");
    expect(passwordInput()).toHaveAttribute("aria-describedby", alert.id);
    await waitFor(() => expect(passwordInput()).toBeEnabled());
    expect(passwordInput()).toHaveFocus();
  });

  it("locked account: replaces the form with the locked view", async () => {
    authenticate.mockRejectedValue({ errors: { error: "account_locked" } });
    renderStep();
    typeAndSubmit("nope");
    await waitFor(() => expect(screen.getByTestId("confirm-password-locked")).toBeInTheDocument());
    expect(screen.getByText(CONFIRM_PASSWORD_COPY.locked)).toBeInTheDocument();
    expect(screen.queryByLabelText("Password")).not.toBeInTheDocument();
  });

  it("captcha required: shows the captcha, blocks submit until solved, then sends the code", async () => {
    authenticate.mockRejectedValueOnce({
      errors: { error: "captcha_enabled", captcha_required: true, captcha_site_key: "srv-key" },
    });
    const { rerender, client } = renderStep();
    typeAndSubmit("Correct#1");
    await waitFor(() => expect(screen.getByTestId("captcha")).toBeInTheDocument());
    expect(screen.getByText(CONFIRM_PASSWORD_COPY.captchaRequired)).toBeInTheDocument();
    expect(resetCaptcha).toHaveBeenCalled();
    expect(screen.getByRole("button", { name: /continue/i })).toBeDisabled();

    captchaState.code = "solved";
    authenticate.mockResolvedValueOnce({ authorizeUrl: "https://iam.example/authorize" });
    rerender(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={["/oidc/invitation/t1"]}>
          <InvitationConfirmPassword
            tenantId="t1"
            redemptionId="rid-1"
            maskedEmail="as•••@example.com"
            onSuccess={vi.fn()}
            onExit={vi.fn()}
          />
        </MemoryRouter>
      </QueryClientProvider>,
    );
    typeAndSubmit("Correct#1");
    await waitFor(() =>
      expect(authenticate).toHaveBeenLastCalledWith(
        { redemptionId: "rid-1", password: "Correct#1", captchaCode: "solved" },
        "t1",
      ),
    );
  });

  it("captcha invalid: shows the retry copy", async () => {
    authenticate.mockRejectedValue({ errors: { error: "captcha_invalid" } });
    renderStep();
    typeAndSubmit("Correct#1");
    await waitFor(() => expect(screen.getByText(CONFIRM_PASSWORD_COPY.captchaInvalid)).toBeInTheDocument());
  });

  it.each(["invalid_redemption", "invalid_link"])("%s ends the step", async (code) => {
    authenticate.mockRejectedValue({ errors: { error: code } });
    const { onExit } = renderStep();
    typeAndSubmit("Correct#1");
    await waitFor(() => expect(onExit).toHaveBeenCalledWith(code));
  });

  it("unknown errors show the generic copy", async () => {
    authenticate.mockRejectedValue(new Error("network"));
    renderStep();
    typeAndSubmit("Correct#1");
    await waitFor(() => expect(screen.getByText(CONFIRM_PASSWORD_COPY.generic)).toBeInTheDocument());
  });
});
