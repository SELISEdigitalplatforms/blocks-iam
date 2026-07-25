import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { IRole } from "@blocks-idp/iam/models/role";

vi.mock("./add-sso-role", () => ({
  AddSSORole: ({ onAdd }: { onAdd: (r: IRole[]) => void }) => (
    <button onClick={() => onAdd([{ slug: "new", name: "New" } as IRole])}>add-role</button>
  ),
}));
vi.mock("./sso-roles-list", () => ({
  SSORolesList: ({ roles, onDelete }: { roles: IRole[]; onDelete: (r: IRole) => void }) => (
    <div>
      {roles.map((r) => (
        <button key={r.slug} onClick={() => onDelete(r)}>
          delete-{r.slug}
        </button>
      ))}
    </div>
  ),
}));

import { SSOInitialRoles } from "./sso-initial-roles";

const role = (i: number) => ({ slug: `r${i}`, name: `Role ${i}` }) as IRole;

describe("SSOInitialRoles", () => {
  it("appends new roles from the add control", () => {
    const onChange = vi.fn();
    render(<SSOInitialRoles roles={[role(1)]} onChange={onChange} />);
    fireEvent.click(screen.getByText("add-role"));
    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ slug: "r1" }),
      expect.objectContaining({ slug: "new" }),
    ]);
  });

  it("removes a role by slug", () => {
    const onChange = vi.fn();
    render(<SSOInitialRoles roles={[role(1), role(2)]} onChange={onChange} />);
    fireEvent.click(screen.getByText("delete-r1"));
    expect(onChange).toHaveBeenCalledWith([expect.objectContaining({ slug: "r2" })]);
  });

  it("shows pagination when there are more roles than a page", () => {
    const roles = Array.from({ length: 7 }, (_, i) => role(i));
    render(<SSOInitialRoles roles={roles} onChange={vi.fn()} />);
    // First page shows the first five roles.
    expect(screen.getByText("delete-r0")).toBeInTheDocument();
    expect(screen.queryByText("delete-r6")).not.toBeInTheDocument();
  });
});
