import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  useGetEmailTemplates: vi.fn(),
  useGetEmailConfigs: vi.fn(),
  useGetLanguages: vi.fn(),
  navigate: vi.fn(),
  setTemplateParams: vi.fn(),
  setEmailUsageParams: vi.fn(),
  setTabId: vi.fn(),
  tabId: "Emailstemplates",
}));

vi.mock("@blocks-communication/mail/hooks/use-email-template", () => ({
  useGetEmailTemplates: h.useGetEmailTemplates,
}));
vi.mock("@blocks-communication/mail/hooks/use-email-config", () => ({
  useGetEmailConfigs: h.useGetEmailConfigs,
}));
vi.mock("@blocks-localization/hooks/use-language-manager", () => ({
  useGetLanguages: h.useGetLanguages,
}));
vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("nuqs", () => ({
  useQueryState: () => [h.tabId, h.setTabId],
}));
vi.mock("./template-filter-toolbar", () => ({
  TemplateFilterToolbar: () => <div data-testid="template-toolbar" />,
  useTemplatesFilterQueryParams: () => ({
    queryParams: { pageNumber: 0, pageSize: 10, search: "" },
    setQueryParams: h.setTemplateParams,
  }),
  useTemplatesSortQueryParams: () => ({
    sortQueryParams: { property: "Name", isDescending: false },
  }),
}));
vi.mock("../email-usage/email-usage-filter-toolbar", () => ({
  useEmailUsageFilterQueryParams: () => ({ setQueryParams: h.setEmailUsageParams }),
}));
vi.mock("@blocks-communication/mail/email/email-service-table/email-template-list", () => ({
  EmailTemplateList: ({ onRowClick }: { onRowClick: (id: string) => void }) => (
    <button type="button" onClick={() => onRowClick("tpl-9")}>
      row-tpl-9
    </button>
  ),
}));
vi.mock("@blocks-communication/mail/email/email-usage/email-usage-list", () => ({
  EmailUsageList: ({ isInbound }: { isInbound: boolean }) => (
    <div data-testid="usage-list">inbound={String(isInbound)}</div>
  ),
}));
vi.mock("@blocks-lmt/components", () => ({ LogMenu: () => null }));

import { EmailServiceTable } from "./email-service-table";

const renderTable = () =>
  render(
    <MemoryRouter>
      <EmailServiceTable />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.tabId = "Emailstemplates";
  h.useGetEmailTemplates.mockReturnValue({
    isLoading: false,
    data: { templates: [{ itemId: "tpl-9" }], totalCount: 1 },
  });
  h.useGetEmailConfigs.mockReturnValue({ isLoading: false, data: [] });
  h.useGetLanguages.mockReturnValue({ isLoading: false, data: [] });
});

describe("EmailServiceTable", () => {
  it("renders the Email heading, configure link and template toolbar", () => {
    renderTable();
    expect(screen.getByText("Email")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /configure/i })).toHaveAttribute(
      "href",
      "/utilities/email/configure",
    );
    expect(screen.getByTestId("template-toolbar")).toBeInTheDocument();
  });

  it("navigates to the new-communication route when Add Template is clicked", () => {
    renderTable();
    fireEvent.click(screen.getByRole("button", { name: /add template/i }));
    expect(h.navigate).toHaveBeenCalledWith("/new-communication");
  });

  it("navigates to a template's detail page when a row is clicked", () => {
    renderTable();
    fireEvent.click(screen.getByRole("button", { name: "row-tpl-9" }));
    expect(h.navigate).toHaveBeenCalledWith("/utilities/email/communications/tpl-9");
  });

  it("shows a loading skeleton while templates are loading", () => {
    h.useGetEmailTemplates.mockReturnValue({ isLoading: true, data: undefined });
    const { container } = renderTable();
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("renders the inbound usage list on the Inbox tab", () => {
    h.tabId = "Inbox";
    renderTable();
    expect(screen.getByTestId("usage-list")).toHaveTextContent("inbound=true");
    // The Add Template button is hidden on non-template tabs.
    expect(
      screen.queryByRole("button", { name: /add template/i }),
    ).not.toBeInTheDocument();
  });
});
