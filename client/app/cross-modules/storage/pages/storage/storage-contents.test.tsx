import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  storageResult: {} as Record<string, unknown>,
  lastDetailsProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-storage/hooks/use-storage-configuration", () => ({
  useGetStorageConfigurations: () => h.storageResult,
}));
vi.mock("../storage-configuration/save-storage-configuration/save-storage-configuration", () => ({
  SaveStorageConfiguration: () => <div data-testid="save-storage" />,
}));
vi.mock("./components/storage-card", () => ({
  StorageCard: ({ data, onViewDetails }: { data: { id: string; title: string }; onViewDetails: (id: string) => void }) => (
    <button onClick={() => onViewDetails(data.id)}>card-{data.title}</button>
  ),
}));
vi.mock("./components/storage-filters-toolbar", () => ({
  StorageFiltersToolbar: ({ onAddConfiguration }: { onAddConfiguration: () => void }) => (
    <button onClick={onAddConfiguration}>add-config</button>
  ),
}));
vi.mock("./components/storage-details-drawer", () => ({
  StorageDetailsDrawer: (props: Record<string, unknown>) => {
    h.lastDetailsProps = props;
    return <div data-testid="details-drawer" data-open={String(props.open)} />;
  },
}));
vi.mock("@/components/filter-toolbar", () => ({}));

import { StorageContents } from "./storage-contents";

beforeEach(() => {
  vi.clearAllMocks();
  h.lastDetailsProps = null;
  h.storageResult = {
    data: [
      { itemId: "s1", name: "Default", storageStrategy: "Amazon" },
      { itemId: "s2", name: "Azure Blob", storageStrategy: "Azure" },
    ],
    isLoading: false,
    isFetching: false,
  };
});

describe("StorageContents", () => {
  it("renders a card per storage configuration (Default first)", () => {
    render(<StorageContents />);
    expect(screen.getByText("card-Default")).toBeInTheDocument();
    expect(screen.getByText("card-Azure Blob")).toBeInTheDocument();
  });

  it("renders the loading skeletons while fetching", () => {
    h.storageResult = { data: undefined, isLoading: true, isFetching: false };
    const { container } = render(<StorageContents />);
    expect(container.querySelectorAll(".grid > div").length).toBeGreaterThan(0);
    expect(screen.queryByText("card-Default")).toBeNull();
  });

  it("shows the empty state when there are no configurations", () => {
    h.storageResult = { data: [], isLoading: false, isFetching: false };
    render(<StorageContents />);
    expect(screen.getByText("No storage configurations found.")).toBeInTheDocument();
  });

  it("opens the details drawer when a card requests details", () => {
    render(<StorageContents />);
    fireEvent.click(screen.getByText("card-Azure Blob"));
    expect(h.lastDetailsProps?.open).toBe(true);
    expect(
      (h.lastDetailsProps?.storage as { itemId: string }).itemId,
    ).toBe("s2");
  });
});
