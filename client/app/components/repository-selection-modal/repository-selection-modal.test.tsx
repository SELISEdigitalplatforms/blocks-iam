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
});
