import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const h = vi.hoisted(() => ({
  useGetAssets: vi.fn(),
  useAddAssets: vi.fn(),
  useValidateAuthorization: vi.fn(),
  toast: vi.fn(),
  mutateAsync: vi.fn(),
  refetch: vi.fn(),
  refetchAuthorization: vi.fn(),
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedTenantGroup: "tg-1" })),
}));
vi.mock("@/hooks/use-project", () => ({
  useGetAssets: h.useGetAssets,
  useAddAssets: h.useAddAssets,
}));
vi.mock("@/cross-modules/devops/hooks/github-info", () => ({
  useValidateAuthorization: h.useValidateAuthorization,
}));
vi.mock("@/hooks/use-toast", () => ({ toast: h.toast }));
vi.mock(
  "@/cross-modules/devops/components/deployment-steps/render-repos/render-provider",
  () => ({
    default: ({ onClose }: { onClose: (v?: boolean) => void }) => (
      <button type="button" onClick={() => onClose(true)}>
        provider-connect
      </button>
    ),
  }),
);
vi.mock("@/components/repository-selection-modal/repository-selection-modal", () => ({
  RepositorySelectionModal: ({
    open,
    onSelectRepository,
  }: {
    open: boolean;
    onSelectRepository: (r: { id: number; full_name: string; html_url: string }) => void;
  }) =>
    open ? (
      <div data-testid="repo-select-modal">
        <button
          type="button"
          onClick={() =>
            onSelectRepository({
              id: 7,
              full_name: "org/service",
              html_url: "https://github.com/org/service",
            })
          }
        >
          pick-repo
        </button>
      </div>
    ) : null,
}));

import { RepositoriesPage } from "./repositories";

const asset = (name: string, link: string, id = "1") => ({
  resourceId: id,
  name,
  link,
});

function setAssets({
  resources = [] as ReturnType<typeof asset>[],
  totalCount = 0,
  isLoading = false,
  isFetching = false,
}: {
  resources?: ReturnType<typeof asset>[];
  totalCount?: number;
  isLoading?: boolean;
  isFetching?: boolean;
} = {}) {
  h.useGetAssets.mockReturnValue({
    data: { assets: { resources }, totalCount },
    isLoading,
    isFetching,
    refetch: h.refetch,
  });
}

const renderPage = () =>
  render(<RepositoriesPage />, { wrapper: createWrapper() });

beforeEach(() => {
  vi.clearAllMocks();
  setAssets();
  h.useAddAssets.mockReturnValue({ mutateAsync: h.mutateAsync });
  h.useValidateAuthorization.mockReturnValue({
    data: undefined,
    refetch: h.refetchAuthorization,
  });
});

describe("RepositoriesPage", () => {
  it("renders the title, search box and empty state", () => {
    renderPage();
    expect(screen.getByText("Repositories")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Search repositories...")).toBeInTheDocument();
    expect(
      screen.getByText("No repositories found. Add a repository to get started."),
    ).toBeInTheDocument();
  });

  it("renders repository rows from the assets response", () => {
    setAssets({
      resources: [asset("org/alpha", "https://github.com/org/alpha")],
      totalCount: 1,
    });
    renderPage();
    expect(screen.getByText("org/alpha")).toBeInTheDocument();
    expect(screen.getByText("https://github.com/org/alpha")).toBeInTheDocument();
    expect(screen.getByText("Github")).toBeInTheDocument();
  });

  it("shows skeleton rows while fetching assets", () => {
    setAssets({ isFetching: true });
    const { container } = renderPage();
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("opens the provider connect dialog when not yet authorized", async () => {
    h.refetchAuthorization.mockResolvedValue({ data: { isSuccess: false } });
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /add/i }));

    expect(
      await screen.findByText("Connect repository"),
    ).toBeInTheDocument();
    expect(screen.getByText("provider-connect")).toBeInTheDocument();
  });

  it("opens the repository selection modal directly when already authorized", async () => {
    h.refetchAuthorization.mockResolvedValue({ data: { isSuccess: true } });
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /add/i }));

    expect(await screen.findByTestId("repo-select-modal")).toBeInTheDocument();
  });

  it("adds a repository and shows a success toast when one is selected", async () => {
    h.refetchAuthorization.mockResolvedValue({ data: { isSuccess: true } });
    h.mutateAsync.mockResolvedValue(undefined);
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /add/i }));

    fireEvent.click(await screen.findByText("pick-repo"));

    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith({
        tenantGroupId: "tg-1",
        resource: {
          resourceId: "7",
          name: "org/service",
          link: "https://github.com/org/service",
        },
      }),
    );
    expect(h.toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success" }),
    );
  });

  it("shows an error toast when adding a repository fails", async () => {
    h.refetchAuthorization.mockResolvedValue({ data: { isSuccess: true } });
    h.mutateAsync.mockRejectedValue(new Error("nope"));
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /add/i }));
    fireEvent.click(await screen.findByText("pick-repo"));

    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "destructive", description: "nope" }),
      ),
    );
  });

  it("falls back to the provider dialog when the auth check throws", async () => {
    h.refetchAuthorization.mockRejectedValue(new Error("network"));
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /add/i }));

    expect(await screen.findByText("Connect repository")).toBeInTheDocument();
    errorSpy.mockRestore();
  });
});
