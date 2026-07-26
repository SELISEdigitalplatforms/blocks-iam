import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IRole } from "@blocks-idp/iam/models/role";

const h = vi.hoisted(() => ({ navigate: vi.fn() }));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/hooks/use-scoped-path", () => ({ useScopedPath: () => (p: string) => `/scoped/${p}` }));
vi.mock("./delete-sso-role", () => ({
  DeleteSSORole: ({ role }: { role: IRole }) => <button>del-{role.slug}</button>,
}));

import { SSORolesList } from "./sso-roles-list";

const roles = [
  { itemId: "r1", name: "Administrator", slug: "admin" },
  { itemId: "r2", name: "Viewer", slug: "viewer" },
] as IRole[];

const renderList = (data: IRole[] = roles) =>
  render(
    <MemoryRouter>
      <SSORolesList roles={data} onDelete={vi.fn()} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
});

describe("SSORolesList", () => {
  it("shows the empty state when there are no roles", () => {
    renderList([]);
    expect(screen.getByText("No roles found")).toBeInTheDocument();
  });

  it("renders a row per role with name and slug", () => {
    renderList();
    expect(screen.getByText("Administrator")).toBeInTheDocument();
    expect(screen.getByText("admin")).toBeInTheDocument();
    expect(screen.getByText("Viewer")).toBeInTheDocument();
  });

  it("navigates to the role detail on row click", () => {
    renderList();
    fireEvent.click(screen.getByText("Administrator"));
    expect(h.navigate).toHaveBeenCalledWith("/scoped/role-detail/r1");
  });
});
