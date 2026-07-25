import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  mutateAsync: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useRevokeAccess: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));
vi.mock("@/hooks/use-scoped-path", () => ({
  useScopedPath: () => (segment: string) => `/base/${segment}`,
}));
vi.mock("./organization-users-filter-toolbar", () => ({
  useOrganizationUsersSortQueryParams: () => ({
    sortQueryParams: { property: "FirstName", isDescending: false },
    setSortQueryParams: vi.fn(),
  }),
}));
vi.mock("@/components/filter-toolbar", () => ({
  FilterControls: { SortHeader: ({ label }: { label: string }) => <span>{label}</span> },
}));

import { OrganizationUsersTable } from "./organization-users-table";

const user = (over: Record<string, unknown> = {}) =>
  ({
    itemId: "u1",
    firstName: "Ada",
    lastName: "Lovelace",
    email: "ada@example.com",
    active: true,
    lastLoggedInTime: "",
    ...over,
  }) as unknown as Parameters<typeof OrganizationUsersTable>[0]["users"][number];

const renderTable = (props: Partial<Parameters<typeof OrganizationUsersTable>[0]> = {}) =>
  render(
    <MemoryRouter>
      <OrganizationUsersTable
        users={props.users ?? [user()]}
        isLoading={props.isLoading ?? false}
        organizationId={props.organizationId ?? "org-1"}
        projectKey={props.projectKey ?? "p1"}
      />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("OrganizationUsersTable", () => {
  it("renders a row per user with name and status", () => {
    renderTable({ users: [user({ itemId: "u1", firstName: "Ada", lastName: "Lovelace", active: true })] });
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("shows the empty state when there are no users", () => {
    renderTable({ users: [] });
    expect(screen.getByText("No users found.")).toBeInTheDocument();
  });

  it("navigates to the user detail when a row is clicked", () => {
    renderTable({ users: [user({ itemId: "u9" })] });
    fireEvent.click(screen.getByText("Ada Lovelace"));
    expect(h.navigate).toHaveBeenCalledWith("/base/user-detail/u9");
  });

  it("opens the revoke dialog and confirms the revoke", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderTable({ users: [user()] });
    fireEvent.click(screen.getAllByLabelText("Revoke from organization")[0]);
    await waitFor(() => expect(screen.getByText("Revoke access")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Revoke" }));
    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalledWith({ organizationId: "org-1" }));
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("renders the loading skeleton while loading", () => {
    const { container } = renderTable({ isLoading: true });
    expect(container.querySelectorAll(".rounded-xl").length).toBeGreaterThan(0);
    expect(screen.queryByText("Ada Lovelace")).not.toBeInTheDocument();
  });

  it("navigates when a row receives an Enter keypress", () => {
    renderTable({ users: [user({ itemId: "uk" })] });
    fireEvent.keyDown(screen.getByText("Ada Lovelace").closest('[role="button"]')!, { key: "Enter" });
    expect(h.navigate).toHaveBeenCalledWith("/base/user-detail/uk");
  });

  it("shows an error toast when the revoke response is not successful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { m: "no" } });
    renderTable({ users: [user()] });
    fireEvent.click(screen.getAllByLabelText("Revoke from organization")[0]);
    await waitFor(() => expect(screen.getByText("Revoke access")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Revoke" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: { m: "no" } }));
    expect(h.showSuccess).not.toHaveBeenCalled();
  });

  it("shows an error toast when the revoke mutation throws", async () => {
    h.mutateAsync.mockRejectedValue({ errors: "boom" });
    renderTable({ users: [user()] });
    fireEvent.click(screen.getAllByLabelText("Revoke from organization")[0]);
    await waitFor(() => expect(screen.getByText("Revoke access")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Revoke" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "boom" }));
  });

  it("closes the revoke dialog when cancel is clicked", async () => {
    renderTable({ users: [user()] });
    fireEvent.click(screen.getAllByLabelText("Revoke from organization")[0]);
    await waitFor(() => expect(screen.getByText("Revoke access")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(screen.queryByText("Revoke access")).not.toBeInTheDocument());
  });
});
