import { act, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { runOutsideAct, settle, typeNative } from "@/test-utils/react-burst";

/*
 * Reset-password uses the same panel checker as activation, so it carried the same React #185
 * risk (blocks-iam#682, H6). Renders the real ResetPassword -> OidcAuthShell -> ResetPasswordForm
 * -> panel PasswordStrengthChecker tree; only the network edge and captcha are mocked.
 */

const h = vi.hoisted(() => ({
  reset: vi.fn(),
  renders: { shell: 0, form: 0 },
}));

vi.mock("react-router", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react-router")>()),
  useNavigate: () => vi.fn(),
}));
vi.mock("@/components/captcha", () => ({ Captcha: () => null }));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ captcha: {}, code: "", reset: vi.fn() }),
}));
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountResetPassword: () => ({ isPending: false, mutateAsync: h.reset }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", async () => {
  const { OIDC_UI_TEMPLATE_FIXTURE } = await import(
    "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture"
  );
  const policy = {
    minLength: 8,
    maxLength: 64,
    requireUppercase: true,
    requireLowercase: true,
    requireNumbers: true,
    requireSpecialChars: true,
  };
  const data = { captcha: null, template: OIDC_UI_TEMPLATE_FIXTURE };
  return {
    useOidcUiConfig: () => ({ data, captchaEnabled: false, passwordPolicy: policy, isLoading: false }),
  };
});
vi.mock("@/components/appearance-controls/appearance-controls", () => ({
  AppearanceControls: () => {
    h.renders.shell++;
    return null;
  },
}));
vi.mock("../oidc/oidc-auth-shell", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../oidc/oidc-auth-shell")>();
  return {
    ...actual,
    useOidcAuthAnimation: () => {
      h.renders.form++;
      return actual.useOidcAuthAnimation();
    },
  };
});

import { ResetPassword } from "./reset-password";

// The bursts drive hundreds of real renders on purpose; under a loaded CI runner that takes
// longer than the default 5 s.
const BURST_TIMEOUT_MS = 30_000;

const MET = "Abcdef1!";
const NOT_MET = "abc";

const renderReset = async () => {
  const view = render(
    <MemoryRouter>
      <ResetPassword code="reset-code" tenantId="tenant-1" />
    </MemoryRouter>,
  );
  await screen.findByRole("heading", { name: "Password Strength" });
  const [password, confirm] = Array.from(
    view.container.querySelectorAll<HTMLInputElement>('input[type="password"]'),
  );
  return { password, confirm };
};

beforeEach(() => {
  vi.clearAllMocks();
  h.renders.shell = 0;
  h.renders.form = 0;
  h.reset.mockResolvedValue({ isSuccess: true });
});

describe("Reset password step: React #185 regression (#682, H6)", () => {
  it("survives a same-task burst of password input events", async () => {
    const { password, confirm } = await renderReset();

    const errors = await runOutsideAct(() => {
      for (let i = 0; i < 120; i++) {
        const value = i % 2 === 0 ? MET : NOT_MET;
        typeNative(password, value);
        typeNative(confirm, value);
      }
    });

    expect(errors).toEqual([]);
  }, BURST_TIMEOUT_MS);

  it("renders the form at most twice and the shell at most once per character typed", async () => {
    const { password } = await renderReset();

    for (let i = 1; i <= MET.length; i++) {
      h.renders.shell = 0;
      h.renders.form = 0;
      await act(async () => {
        typeNative(password, MET.slice(0, i));
      });
      await act(async () => {
        await settle(10);
      });
      expect(h.renders.form).toBeLessThanOrEqual(2);
      expect(h.renders.shell).toBeLessThanOrEqual(1);
    }
  });

  it("shows the panel checker in step with both fields", async () => {
    const { password, confirm } = await renderReset();

    await act(async () => {
      typeNative(password, MET);
      typeNative(confirm, MET);
    });

    const matchRow = screen.getByText("Passwords match").closest("li")!;
    await waitFor(() => expect(matchRow.querySelector("svg.text-success")).not.toBeNull());
  });
});
