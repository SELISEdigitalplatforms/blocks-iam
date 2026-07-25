import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  useGetGithubRepos: vi.fn(),
  revokeAccess: vi.fn(),
}));

vi.mock("@/cross-modules/devops/hooks/github-info", () => ({
  useGetGithubRepos: h.useGetGithubRepos,
}));
vi.mock("@/cross-modules/devops/services/github-info.service", () => ({
  githubInfoService: { revokeAccess: h.revokeAccess },
}));

import { RepositorySelectionModal } from "./repository-selection-modal";
import type { IRepository } from "@/cross-modules/devops/models/github-info";

const repo = (id: number, name: string): IRepository =>
  ({
    id,
    full_name: name,
    html_url: `https://github.com/${name}`,
  }) as IRepository;

const reposResponse = (items: IRepository[], total = items.length) => ({
  data: { items, total_count: total },
});

function setHook({
  data,
  isLoading = false,
  isFetching = false,
}: {
  data?: ReturnType<typeof reposResponse>;
  isLoading?: boolean;
  isFetching?: boolean;
}) {
  h.useGetGithubRepos.mockReturnValue({ data, isLoading, isFetching });
}

const renderModal = (props: Partial<React.ComponentProps<typeof RepositorySelectionModal>> = {}) =>
  render(
    <RepositorySelectionModal
      open
      onOpenChange={props.onOpenChange ?? vi.fn()}
      onSelectRepository={props.onSelectRepository ?? vi.fn()}
      {...props}
    />,
    { wrapper: createWrapper() },
  );

beforeEach(() => {
  vi.clearAllMocks();
  setHook({ data: reposResponse([repo(1, "org/alpha"), repo(2, "org/beta")]) });
});

describe("RepositorySelectionModal", () => {
  it("renders the title, description and provider options with GitHub pre-selected", () => {
    renderModal({ title: "Pick a repo", description: "Choose wisely" });

    expect(screen.getByText("Pick a repo")).toBeInTheDocument();
    expect(screen.getByText("Choose wisely")).toBeInTheDocument();

    const github = screen.getByLabelText("GitHub") as HTMLInputElement;
    expect(github.checked).toBe(true);
    const gitlab = screen.getByLabelText("GitLab") as HTMLInputElement;
    expect(gitlab.disabled).toBe(true);
  });

  it("shows the total result count once data is available", () => {
    setHook({ data: reposResponse([repo(1, "org/alpha")], 42) });
    renderModal();
    expect(screen.getByText(/\(42 results\)/)).toBeInTheDocument();
  });

  it("disables the trigger and the Add button while loading", () => {
    setHook({ data: undefined, isLoading: true });
    renderModal();
    expect(screen.getByRole("combobox")).toBeDisabled();
    expect(screen.getByRole("button", { name: "Add" })).toBeDisabled();
  });

  it("closes the modal when Cancel is clicked", () => {
    const onOpenChange = vi.fn();
    renderModal({ onOpenChange });
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("lists repositories in the popover and selecting one enables Add", async () => {
    const user = userEvent.setup();
    const onSelectRepository = vi.fn();
    renderModal({ onSelectRepository });

    await user.click(screen.getByRole("combobox"));

    const option = await screen.findByText("org/beta");
    await user.click(option);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Add" })).toBeEnabled(),
    );

    await user.click(screen.getByRole("button", { name: "Add" }));
    expect(onSelectRepository).toHaveBeenCalledTimes(1);
    expect(onSelectRepository.mock.calls[0][0]).toMatchObject({ id: 2, full_name: "org/beta" });
  });

  it("warns when the chosen repository is already selected", async () => {
    const user = userEvent.setup();
    const onSelectRepository = vi.fn();
    renderModal({ onSelectRepository, selectedRepositories: [repo(1, "org/alpha")] });

    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByText("org/alpha"));

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Add" })).toBeEnabled(),
    );
    await user.click(screen.getByRole("button", { name: "Add" }));

    expect(await screen.findByText("Repository already selected.")).toBeInTheDocument();
    expect(onSelectRepository).not.toHaveBeenCalled();
  });

  it("shows the empty state when no repositories are returned", async () => {
    const user = userEvent.setup();
    setHook({ data: reposResponse([], 0) });
    renderModal();

    await user.click(screen.getByRole("combobox"));
    expect(await screen.findByText("No repositories found.")).toBeInTheDocument();
  });

  it("revokes GitHub access through the confirmation dialog", async () => {
    h.revokeAccess.mockResolvedValue(undefined);
    renderModal();

    fireEvent.click(screen.getByText("Revoke repository access"));

    const revokeTitle = await screen.findByText("Revoke Access");
    const dialog = revokeTitle.closest("[role='dialog']") as HTMLElement;
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(h.revokeAccess).toHaveBeenCalledTimes(1));
  });

  it("logs and recovers when revoking GitHub access fails", async () => {
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    h.revokeAccess.mockRejectedValue(new Error("revoke failed"));
    renderModal();

    fireEvent.click(screen.getByText("Revoke repository access"));
    const revokeTitle = await screen.findByText("Revoke Access");
    const dialog = revokeTitle.closest("[role='dialog']") as HTMLElement;
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm" }));

    await waitFor(() =>
      expect(errorSpy).toHaveBeenCalledWith("Error revoking GitHub access:", expect.any(Error)),
    );
    await waitFor(() => expect(screen.queryByText("Revoke Access")).toBeNull());
    errorSpy.mockRestore();
  });

  it("closes the revoke dialog from its Cancel button", async () => {
    renderModal();
    fireEvent.click(screen.getByText("Revoke repository access"));
    const revokeTitle = await screen.findByText("Revoke Access");
    const dialog = revokeTitle.closest("[role='dialog']") as HTMLElement;

    fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(screen.queryByText("Revoke Access")).toBeNull());
    expect(h.revokeAccess).not.toHaveBeenCalled();
  });

  it("refetches with the search term after the debounce", async () => {
    const user = userEvent.setup();
    renderModal();
    await user.click(screen.getByRole("combobox"));

    const searchInput = await screen.findByPlaceholderText("Search repositories...");
    fireEvent.change(searchInput, { target: { value: "alpha" } });

    await waitFor(
      () =>
        expect(
          h.useGetGithubRepos.mock.calls.some((c) => c[1] === "alpha"),
        ).toBe(true),
      { timeout: 2000 },
    );
  });

  it("resets to an empty list when the items payload is not an array", async () => {
    const user = userEvent.setup();
    setHook({ data: { data: { items: null as unknown as IRepository[], total_count: 5 } } });
    renderModal();
    await user.click(screen.getByRole("combobox"));
    expect(await screen.findByText("No repositories found.")).toBeInTheDocument();
  });

  it("resets to an empty list when an empty first page is returned with a count", async () => {
    const user = userEvent.setup();
    setHook({ data: reposResponse([], 5) });
    renderModal();
    await user.click(screen.getByRole("combobox"));
    expect(await screen.findByText("No repositories found.")).toBeInTheDocument();
  });

  it("clears its internal state when the modal is closed", async () => {
    const { rerender } = renderModal();
    rerender(
      <RepositorySelectionModal open={false} onOpenChange={vi.fn()} onSelectRepository={vi.fn()} />,
    );
    await waitFor(() => expect(screen.queryByText("Select repository")).toBeNull());
  });

  it("closes the revoke dialog via its onOpenChange when dismissed", async () => {
    renderModal();
    fireEvent.click(screen.getByText("Revoke repository access"));
    await screen.findByText("Revoke Access");
    fireEvent.keyDown(document.body, { key: "Escape" });
    await waitFor(() => expect(screen.queryByText("Revoke Access")).toBeNull());
  });

  it("loads the next page when the list is scrolled to the bottom", async () => {
    const firstPage = Array.from({ length: 10 }, (_, i) => repo(i + 1, `org/repo-${i + 1}`));
    const secondPage = Array.from({ length: 10 }, (_, i) => repo(i + 11, `org/repo-${i + 11}`));
    // Stable references per page so the accumulation effect only reruns when the
    // page actually changes (a fresh object every render would loop forever).
    const firstResp = { data: reposResponse(firstPage, 25), isLoading: false, isFetching: false };
    const secondResp = { data: reposResponse(secondPage, 25), isLoading: false, isFetching: false };
    h.useGetGithubRepos.mockImplementation(
      (_open: boolean, _search: string | undefined, page: number) =>
        page >= 2 ? secondResp : firstResp,
    );

    const user = userEvent.setup();
    renderModal();
    await user.click(screen.getByRole("combobox"));

    const list = (await screen.findByText("org/repo-1")).closest(
      '[class*="overflow-y-auto"]',
    ) as HTMLElement;
    fireEvent.wheel(list, { deltaY: 200 });
    fireEvent.scroll(list);

    await waitFor(() =>
      expect(h.useGetGithubRepos.mock.calls.some((c) => c[2] === 2)).toBe(true),
    );
  });
});
