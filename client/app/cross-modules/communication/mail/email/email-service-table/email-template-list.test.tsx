import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  setSortQueryParams: vi.fn(),
  cloneTemplate: vi.fn(),
  deleteTemplate: vi.fn(),
  toast: vi.fn(),
  projectStore: { selectedProject: { tenantId: "t1", itemId: "p1" } },
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigateMock };
});
vi.mock("./template-filter-toolbar", () => ({
  useTemplatesSortQueryParams: vi.fn(() => ({
    sortQueryParams: {},
    setSortQueryParams: h.setSortQueryParams,
  })),
}));
vi.mock("@/components/filter-toolbar", () => ({
  FilterControls: {
    SortHeader: ({ label }: { label: string }) => <span>{label}</span>,
  },
}));
vi.mock("@blocks-communication/mail/hooks/use-email-template", () => ({
  useCloneTemplate: vi.fn(() => ({
    isPending: false,
    mutateAsync: h.cloneTemplate,
  })),
  useDeleteEmailTemplate: vi.fn(() => ({
    isPending: false,
    mutateAsync: h.deleteTemplate,
  })),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => h.projectStore),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: h.toast }));

import { EmailTemplateList } from "./email-template-list";

const template = {
  itemId: "tmpl-1",
  name: "Welcome Email",
  templateSubject: "Welcome aboard",
  lastUpdatedDate: "2024-01-02T00:00:00Z",
  mailConfigurationId: "cfg-1",
  generatedBy: "System",
};

const emailConfigsData = [{ itemId: "cfg-1", name: "Primary Config" }];

const renderList = (props: {
  templates: (typeof template)[];
  isLoading: boolean;
  onRowClick?: (id: number | string) => void;
}) =>
  render(
    <MemoryRouter>
      <EmailTemplateList
        templates={props.templates}
        isLoading={props.isLoading}
        emailConfigsData={emailConfigsData as never}
        onRowClick={props.onRowClick ?? vi.fn()}
      />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
});

describe("EmailTemplateList", () => {
  it("renders a template row with its resolved configuration name", () => {
    renderList({ templates: [template], isLoading: false });

    expect(screen.getByText("Welcome Email")).toBeInTheDocument();
    expect(screen.getByText("Welcome aboard")).toBeInTheDocument();
    expect(screen.getByText("Primary Config")).toBeInTheDocument();
  });

  it("renders the empty state when there are no templates", () => {
    renderList({ templates: [], isLoading: false });
    expect(screen.getByText("No templates found.")).toBeInTheDocument();
  });

  it("does not render the empty state while loading", () => {
    renderList({ templates: [], isLoading: true });
    expect(screen.queryByText("No templates found.")).not.toBeInTheDocument();
  });

  it("calls onRowClick with the template id when a row is clicked", () => {
    const onRowClick = vi.fn();
    renderList({ templates: [template], isLoading: false, onRowClick });

    fireEvent.click(screen.getByText("Welcome Email"));
    expect(onRowClick).toHaveBeenCalledWith("tmpl-1");
  });

  const openRowMenu = () => {
    fireEvent.pointerDown(
      screen.getByRole("button", { name: "Open menu" }),
      { button: 0, ctrlKey: false, pointerType: "mouse" },
    );
  };

  it("clones a template and navigates to the new template on success", async () => {
    h.cloneTemplate.mockResolvedValue({ isSuccess: true, itemId: "new-id" });
    renderList({ templates: [template], isLoading: false });
    openRowMenu();
    fireEvent.click(await screen.findByText("Clone Template"));
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await vi.waitFor(() =>
      expect(h.cloneTemplate).toHaveBeenCalledWith({ projectKey: "t1", itemId: "tmpl-1" }),
    );
    await vi.waitFor(() =>
      expect(h.navigateMock).toHaveBeenCalledWith("/utilities/email/communications/new-id"),
    );
  });

  it("shows an error toast when cloning fails", async () => {
    h.cloneTemplate.mockResolvedValue({ isSuccess: false, errors: "nope" });
    renderList({ templates: [template], isLoading: false });
    openRowMenu();
    fireEvent.click(await screen.findByText("Clone Template"));
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await vi.waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "destructive" })),
    );
  });

  it("deletes a non-tenant template on confirmation", async () => {
    h.deleteTemplate.mockResolvedValue({ isSuccess: true });
    renderList({ templates: [template], isLoading: false });
    openRowMenu();
    fireEvent.click(await screen.findByText("Delete"));
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await vi.waitFor(() =>
      expect(h.deleteTemplate).toHaveBeenCalledWith({ projectKey: "t1", itemId: "tmpl-1" }),
    );
    await vi.waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "success" })),
    );
  });

  it("hides the delete action for tenant-generated templates", async () => {
    renderList({
      templates: [{ ...template, generatedBy: "Tenant" }],
      isLoading: false,
    });
    openRowMenu();
    expect(await screen.findByText("Clone Template")).toBeInTheDocument();
    expect(screen.queryByText("Delete")).not.toBeInTheDocument();
  });
});
