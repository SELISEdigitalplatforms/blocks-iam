import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  rolesResult: {} as Record<string, unknown>,
  tenantId: "tenant-1",
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("nuqs", () => ({
  parseAsInteger: { withDefault: (d: number) => ({ _d: d }) },
  useQueryStates: () => [{ page: 0, pageSize: 10 }, vi.fn()],
}));
vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: h.tenantId } }),
}));
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useGetRoles: () => h.rolesResult,
}));
vi.mock("@/hooks/use-scoped-path", () => ({
  useScopedPath: () => (segment: string) => `/base/${segment}`,
}));

import { PermissionRolesList } from "./permission-roles-list";

const renderList = (slugs: string[], result?: Record<string, unknown>) => {
  h.rolesResult = result ?? {
    data: {
      data: [{ itemId: "r1", name: "Admin", slug: "admin", description: "the admin" }],
      totalCount: 1,
    },
    isLoading: false,
  };
  return render(
    <MemoryRouter>
      <PermissionRolesList slugs={slugs} />
    </MemoryRouter>,
  );
};

beforeEach(() => vi.clearAllMocks());

describe("PermissionRolesList", () => {
  it("renders nothing when there are no slugs", () => {
    const { container } = renderList([]);
    expect(container.firstChild).toBeNull();
  });

  it("renders the assigned roles card with a row per role", () => {
    renderList(["admin"]);
    expect(screen.getByText("Assigned Roles")).toBeInTheDocument();
    expect(screen.getByText("Admin")).toBeInTheDocument();
    expect(screen.getByText("admin")).toBeInTheDocument();
  });

  it("shows the loading skeleton while roles load", () => {
    const { container } = renderList(["admin"], { data: undefined, isLoading: true });
    expect(container.querySelector(".grid")).not.toBeNull();
  });

  it("navigates to the role detail when a row is clicked", () => {
    renderList(["admin"]);
    fireEvent.click(screen.getByText("Admin"));
    expect(h.navigate).toHaveBeenCalledWith("/base/role-detail/r1");
  });
});
