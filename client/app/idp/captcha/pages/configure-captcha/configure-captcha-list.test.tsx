import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ICaptchaConfig } from "../../models/captcha";

vi.mock("../../modals/configure-captcha-modal/", async () => {
  const { Dialog } = await import("@/components/ui-kits/dialog/dialog");
  return {
    ConfigureCaptchaModal: ({ children }: { children: React.ReactNode }) => (
      <Dialog>{children}</Dialog>
    ),
  };
});
vi.mock("@blocks-idp/captcha/modals/toggle-captcha-status-modal", () => ({
  ToggleCaptchaStatusModal: () => <div data-testid="toggle-modal" />,
}));
vi.mock("@/components/masked-text", () => ({
  MaskedText: ({ text }: { text: string }) => <span>{text}</span>,
}));
vi.mock("@/components/copy-to-clipboard-button", () => ({
  CopyToClipboardButton: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
}));

import { ConfigureCaptchaList } from "./configure-captcha-list";

const config = {
  itemId: "c1",
  provider: "recaptcha",
  isEnable: true,
  captchaKey: "site-key",
  captchaSecret: "secret-key",
} as unknown as ICaptchaConfig;

describe("ConfigureCaptchaList", () => {
  it("renders a loading skeleton while loading", () => {
    const { container } = render(<ConfigureCaptchaList isLoading={true} configurations={[]} />);
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("renders the empty state when there are no configurations", () => {
    render(<ConfigureCaptchaList isLoading={false} configurations={[]} />);
    expect(screen.getByText(/No configurations found/)).toBeInTheDocument();
  });

  it("renders a card per known provider configuration", () => {
    render(<ConfigureCaptchaList isLoading={false} configurations={[config]} />);
    expect(screen.getByText("Google reCAPTCHA")).toBeInTheDocument();
    // Enabled badge is shown for an enabled config.
    expect(screen.getByText("Enable")).toBeInTheDocument();
    expect(screen.getByText("Site Key")).toBeInTheDocument();
    expect(screen.getByText("Secret Key")).toBeInTheDocument();
  });

  it("skips configurations with an unknown provider", () => {
    render(
      <ConfigureCaptchaList
        isLoading={false}
        configurations={[{ ...config, provider: "unknown" } as unknown as ICaptchaConfig]}
      />,
    );
    expect(screen.queryByText("Site Key")).not.toBeInTheDocument();
  });
});
