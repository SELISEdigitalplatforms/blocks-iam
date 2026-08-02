import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  useGetEmailTemplate: vi.fn(),
  useGetUser: vi.fn(),
  useGetEmailConfigs: vi.fn(),
  useSendTestMail: vi.fn(),
  mutateAsync: vi.fn(),
  navigate: vi.fn(),
  toast: vi.fn(),
}));

vi.mock("@blocks-communication/mail/hooks/use-email-template", () => ({
  useGetEmailTemplate: h.useGetEmailTemplate,
  useSendTestMail: h.useSendTestMail,
}));
vi.mock("@blocks-communication/mail/hooks/use-email-config", () => ({
  useGetEmailConfigs: h.useGetEmailConfigs,
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({ useGetUser: h.useGetUser }));
vi.mock("@/hooks/use-toast", () => ({ toast: h.toast }));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "tenant-1" } })),
}));
vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/components/breadcrumb/breadcrumb", () => ({
  default: () => <nav data-testid="breadcrumb" />,
}));
vi.mock(
  "@blocks-communication/mail/components/email-service/modals/edit-communication/edit-communication",
  () => ({ default: () => <div data-testid="edit-communication" /> }),
);

import { EmailCommunicationDetails } from "./email-communication-details";

const template = {
  itemId: "tpl-1",
  name: "Welcome Email",
  templateSubject: "Welcome aboard",
  templateBody: "<p>Hi</p>",
  language: "en-US",
  mailConfigurationId: "cfg-1",
  createdDate: "2026-01-01T00:00:00Z",
  lastUpdatedDate: "2026-02-01T00:00:00Z",
};

function setup({
  data = template as Record<string, unknown> | null,
  isLoading = false,
  isFetching = false,
  isConfigsLoading = false,
  isConfigsFetching = false,
  isPending = false,
} = {}) {
  h.useGetEmailTemplate.mockReturnValue({ isLoading, isFetching, data });
  h.useGetUser.mockReturnValue({ data: { data: { email: "me@example.com" } } });
  h.useGetEmailConfigs.mockReturnValue({
    isLoading: isConfigsLoading,
    isFetching: isConfigsFetching,
    data: [{ itemId: "cfg-1", name: "Primary SMTP" }],
  });
  h.useSendTestMail.mockReturnValue({ isPending, mutateAsync: h.mutateAsync });
}

const renderDetails = () =>
  render(<EmailCommunicationDetails params={{ id: "tpl-1" }} />);

beforeEach(() => {
  vi.clearAllMocks();
  setup();
});

describe("EmailCommunicationDetails", () => {
  it("shows the skeleton while the template is loading", () => {
    setup({ isLoading: true, data: null });
    const { container } = renderDetails();
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("renders the template name, subject, language and configuration", () => {
    renderDetails();
    expect(screen.getByText("Welcome Email")).toBeInTheDocument();
    expect(screen.getByText("Welcome aboard")).toBeInTheDocument();
    // Language resolves from the localization dummy data (English).
    expect(screen.getByText(/English/)).toBeInTheDocument();
    expect(screen.getByText("Primary SMTP")).toBeInTheDocument();
  });

  it("navigates back when the back arrow is clicked", () => {
    renderDetails();
    // The first button is the back-arrow icon button.
    fireEvent.click(screen.getAllByRole("button")[0]);
    expect(h.navigate).toHaveBeenCalledWith(-1);
  });

  it("navigates to the edit route from the template Edit button", () => {
    renderDetails();
    const editButtons = screen.getAllByRole("button", { name: /edit/i });
    fireEvent.click(editButtons[0]);
    expect(h.navigate).toHaveBeenCalledWith(
      "/utilities/email/communications/tpl-1/edit",
    );
  });

  it("sends a test email and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderDetails();

    fireEvent.click(screen.getByRole("button", { name: /send test email/i }));
    fireEvent.click(await screen.findByRole("button", { name: "Send" }));

    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith({
        to: "me@example.com",
        purpose: "Welcome Email",
        language: "en-US",
        projectKey: "tenant-1",
      }),
    );
    expect(h.toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success" }),
    );
  });

  it("shows an error toast when the test email fails", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { x: "bad" } });
    renderDetails();

    fireEvent.click(screen.getByRole("button", { name: /send test email/i }));
    fireEvent.click(await screen.findByRole("button", { name: "Send" }));

    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "destructive" }),
      ),
    );
  });
});
