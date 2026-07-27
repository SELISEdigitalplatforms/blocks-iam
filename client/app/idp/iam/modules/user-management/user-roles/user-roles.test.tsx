import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  roles: [] as { slug: string; name: string }[],
  isLoading: false,
  deleteRoles: vi.fn(),
  showError: vi.fn(),
  showSuccess: vi.fn(),
  lastListProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useUserRoles: () => ({ isLoading: h.isLoading, roles: h.roles, deleteRoles: h.deleteRoles }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));
vi.mock("./add-user-role", () => ({ AddUserRole: () => <div data-testid="add-user-role" /> }));
vi.mock("./user-roles-list", () => ({
  UserRolesList: (props: Record<string, unknown>) => {
    h.lastListProps = props;
    const roles = props.roles as { slug: string; name: string }[];
    return (
      <div>
        {roles.map((r) => (
          <button key={r.slug} onClick={() => (props.onRemoveRole as (s: string) => void)(r.slug)}>
            remove-{r.slug}
          </button>
        ))}
      </div>
    );
  },
}));

import { UserRoles } from "./user-roles";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.roles = [{ slug: "admin", name: "Admin" }, { slug: "viewer", name: "Viewer" }];
});

describe("UserRoles", () => {
  it("renders the roles card and add-role control", () => {
    render(<UserRoles id="u1" projectKey="p1" />);
    expect(screen.getByText("Roles")).toBeInTheDocument();
    expect(screen.getByTestId("add-user-role")).toBeInTheDocument();
  });

  it("reveals Save and Reset actions after a role is removed", () => {
    render(<UserRoles id="u1" projectKey="p1" />);
    expect(screen.queryByRole("button", { name: "Save" })).toBeNull();
    fireEvent.click(screen.getByText("remove-admin"));
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reset" })).toBeInTheDocument();
  });

  it("saves removed roles and shows a success toast", async () => {
    h.deleteRoles.mockResolvedValue({ isSuccess: true });
    render(<UserRoles id="u1" projectKey="p1" />);
    fireEvent.click(screen.getByText("remove-admin"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(h.deleteRoles).toHaveBeenCalledWith(["admin"]));
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when the save fails", async () => {
    h.deleteRoles.mockResolvedValue({ isSuccess: false, errors: "nope" });
    render(<UserRoles id="u1" projectKey="p1" />);
    fireEvent.click(screen.getByText("remove-viewer"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "nope" }));
  });

  it("restores the original roles on reset", () => {
    render(<UserRoles id="u1" projectKey="p1" />);
    fireEvent.click(screen.getByText("remove-admin"));
    fireEvent.click(screen.getByRole("button", { name: "Reset" }));
    expect(screen.getByText("remove-admin")).toBeInTheDocument();
  });
});
