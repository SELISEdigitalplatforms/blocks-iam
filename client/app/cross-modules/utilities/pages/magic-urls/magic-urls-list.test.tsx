import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  setSortQueryParams: vi.fn(),
  deactivateMagicUrl: vi.fn(),
  projectStore: { selectedProject: { tenantId: "t1", itemId: "p1" } },
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigateMock };
});
vi.mock("./magic-urls-filter-toolbar", () => ({
  useMagicUrlSortQueryParams: vi.fn(() => ({
    sortQueryParams: {},
    setSortQueryParams: h.setSortQueryParams,
  })),
}));
vi.mock("@/components/filter-toolbar", () => ({
  FilterControls: {
    SortHeader: ({ label }: { label: string }) => <span>{label}</span>,
  },
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => h.projectStore),
}));
vi.mock("@blocks-utilities/hooks/use-deactivate-magic-url", () => ({
  useDeactivateMagicUrl: vi.fn(() => ({
    deactivateMagicUrl: h.deactivateMagicUrl,
    isRemoving: false,
  })),
}));

import { MagicUrlsList } from "./magic-urls-list";

const row = {
  itemId: "mu1",
  uri: "https://example.com/very/long/destination",
  shortUri: "https://s.io/abc",
  name: "My Link",
  usageLimit: 0,
  usageCount: 0,
  status: "Active",
  createdAt: "2024-01-01T00:00:00Z",
  requestMethod: "GET",
  clientCredential: "cred-1",
};

const renderList = (props: { data: typeof row[]; isLoading: boolean }) =>
  render(
    <MemoryRouter>
      <MagicUrlsList {...props} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
});

describe("MagicUrlsList", () => {
  it("renders a row from the provided data", () => {
    renderList({ data: [row], isLoading: false });

    expect(screen.getByText("https://s.io/abc")).toBeInTheDocument();
    expect(screen.getByText("My Link")).toBeInTheDocument();
    // usageLimit 0 => "Unlimited"
    expect(screen.getByText("Unlimited")).toBeInTheDocument();
    // status badge
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("renders the empty state when there are no rows", () => {
    renderList({ data: [], isLoading: false });
    expect(screen.getByText("No results.")).toBeInTheDocument();
  });

  it("does not render the empty state while loading", () => {
    renderList({ data: [], isLoading: true });
    expect(screen.queryByText("No results.")).not.toBeInTheDocument();
  });

  it("navigates to the details page when a row is clicked", () => {
    renderList({ data: [row], isLoading: false });
    fireEvent.click(screen.getByText("My Link"));
    expect(h.navigateMock).toHaveBeenCalledWith(
      "/utilities/magic-url/details/mu1",
    );
  });

  const openRowMenu = () => {
    const buttons = screen.getAllByRole("button");
    fireEvent.pointerDown(
      buttons[buttons.length - 1],
      { button: 0, ctrlKey: false, pointerType: "mouse" },
    );
  };

  it("navigates to details from the row action menu", async () => {
    renderList({ data: [row], isLoading: false });
    openRowMenu();
    fireEvent.click(await screen.findByText("View Details"));
    expect(h.navigateMock).toHaveBeenCalledWith("/utilities/magic-url/details/mu1");
  });

  it("opens the short link in a new tab", async () => {
    const openSpy = vi.spyOn(window, "open").mockImplementation(() => null);
    renderList({ data: [row], isLoading: false });
    openRowMenu();
    fireEvent.click(await screen.findByText("Go to Link"));
    expect(openSpy).toHaveBeenCalledWith("https://s.io/abc", "_blank");
    openSpy.mockRestore();
  });

  it("deactivates a magic url after confirmation", async () => {
    renderList({ data: [row], isLoading: false });
    openRowMenu();
    fireEvent.click(await screen.findByText("Deactivate"));
    fireEvent.click(await screen.findByRole("button", { name: "Deactivate" }));
    expect(h.deactivateMagicUrl).toHaveBeenCalledWith("mu1", "t1", expect.any(Function));
  });
});
