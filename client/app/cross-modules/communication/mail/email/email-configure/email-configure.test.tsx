import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  configs: { isLoading: false, data: undefined as unknown },
}));

vi.mock("@blocks-communication/mail/hooks/use-email-config", () => ({
  useGetEmailConfigs: () => h.configs,
}));
vi.mock("@/components/ui-kits/stepper/use-media-query", () => ({
  useMediaQuery: () => false,
}));
vi.mock(
  "@blocks-communication/mail/components/email-service/modals/new-configuration/new-configuration",
  () => ({ default: () => <div data-testid="new-config" /> }),
);
vi.mock(
  "@blocks-communication/mail/components/email-service/modals/delete-email-config/delete-email-config",
  () => ({ default: () => <div data-testid="delete-config" /> }),
);

import { EmailConfiguration } from "./email-configure";

beforeEach(() => {
  vi.clearAllMocks();
  h.configs = { isLoading: false, data: undefined };
});

describe("EmailConfiguration", () => {
  it("shows the loading skeletons", () => {
    h.configs = { isLoading: true, data: undefined };
    const { container } = render(<EmailConfiguration />);
    expect(container.querySelectorAll("[class*='rounded']").length).toBeGreaterThan(0);
  });

  it("shows the empty state when there are no configurations", () => {
    h.configs = { isLoading: false, data: [] };
    render(<EmailConfiguration />);
    expect(screen.getByText(/No email configurations found/)).toBeInTheDocument();
  });

  it("renders an outbound configuration with its details", () => {
    h.configs = {
      isLoading: false,
      data: [
        {
          itemId: "cfg-1",
          name: "Primary SMTP",
          host: "smtp.example.com",
          port: "587",
          isInbound: false,
          isDefault: true,
          senderName: "Support",
          senderAddress: "support@example.com",
          senderUserName: "smtp-user",
          provider: 0,
        },
      ],
    };
    render(<EmailConfiguration />);
    expect(screen.getByText("Primary SMTP")).toBeInTheDocument();
    expect(screen.getByText("smtp.example.com")).toBeInTheDocument();
    expect(screen.getByText("587")).toBeInTheDocument();
    expect(screen.getByText("Outbound")).toBeInTheDocument();
    expect(screen.getByText("Support")).toBeInTheDocument();
  });

  it("renders an inbound configuration with server name and username", () => {
    h.configs = {
      isLoading: false,
      data: [
        {
          itemId: "cfg-2",
          name: "Inbound IMAP",
          host: "imap.example.com",
          port: "993",
          isInbound: true,
          isDefault: false,
          senderUserName: "inbox-user",
          provider: 0,
        },
      ],
    };
    render(<EmailConfiguration />);
    expect(screen.getByText("Inbound IMAP")).toBeInTheDocument();
    expect(screen.getByText("Server Name")).toBeInTheDocument();
    expect(screen.getByText("Inbound")).toBeInTheDocument();
    expect(screen.getByText("inbox-user")).toBeInTheDocument();
  });
});
