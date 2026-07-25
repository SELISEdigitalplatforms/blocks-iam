import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ICaptchaConfig } from "../models/captcha";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("../hooks/use-captcha-config", () => ({
  useToggleCaptchaConfigStatus: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { ToggleCaptchaStatusModal } from "./toggle-captcha-status-modal";

const config = {
  itemId: "c1",
  provider: "recaptcha",
  isEnable: false,
} as unknown as ICaptchaConfig;

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("ToggleCaptchaStatusModal", () => {
  it("shows an Enable trigger for a disabled config", () => {
    render(<ToggleCaptchaStatusModal configuration={config} />);
    expect(screen.getAllByRole("button", { name: /Enable/ }).length).toBeGreaterThan(0);
  });

  it("enables the captcha and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<ToggleCaptchaStatusModal configuration={config} />);
    fireEvent.click(screen.getAllByRole("button", { name: /Enable/ })[0]);
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith({
        projectKey: "t1",
        isEnable: true,
        itemId: "c1",
      }),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when the toggle fails", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "nope" });
    render(<ToggleCaptchaStatusModal configuration={config} />);
    fireEvent.click(screen.getAllByRole("button", { name: /Enable/ })[0]);
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "nope" }));
  });

  it("shows a Disable trigger for an enabled config", () => {
    render(
      <ToggleCaptchaStatusModal
        configuration={{ ...config, isEnable: true } as ICaptchaConfig}
      />,
    );
    expect(screen.getAllByRole("button", { name: /Disable/ }).length).toBeGreaterThan(0);
  });
});
