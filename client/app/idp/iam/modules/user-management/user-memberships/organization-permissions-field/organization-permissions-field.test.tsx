import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IPermission } from "@blocks-idp/iam/models/permission";

const h = vi.hoisted(() => ({ addProps: null as Record<string, unknown> | null }));

vi.mock("./add-organization-permission", () => ({
  AddOrganizationPermission: (props: Record<string, unknown>) => {
    h.addProps = props;
    return (
      <button
        onClick={() =>
          (props.onAdd as (p: IPermission[]) => void)([
            { itemId: "n1", resource: "new:res", name: "New" } as IPermission,
          ])
        }
      >
        add-permission
      </button>
    );
  },
}));
vi.mock("./organization-permissions-list", () => ({
  OrganizationPermissionsList: ({
    permissions,
    onDelete,
  }: {
    permissions: IPermission[];
    onDelete: (p: IPermission) => void;
  }) => (
    <div>
      {permissions.map((p) => (
        <button key={p.itemId} onClick={() => onDelete(p)}>
          delete-{p.resource}
        </button>
      ))}
    </div>
  ),
}));

import { OrganizationPermissionsField } from "./organization-permissions-field";

function perm(i: number): IPermission {
  return { itemId: `p${i}`, resource: `res:${i}`, name: `Perm ${i}` } as IPermission;
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("OrganizationPermissionsField", () => {
  it("renders the empty state when there are no permissions", () => {
    render(<OrganizationPermissionsField permissions={[]} onChange={vi.fn()} />);
    expect(screen.getByText("No permissions added")).toBeInTheDocument();
  });

  it("appends new permissions when the add control fires", () => {
    const onChange = vi.fn();
    render(<OrganizationPermissionsField permissions={[perm(1)]} onChange={onChange} />);
    fireEvent.click(screen.getByText("add-permission"));
    expect(onChange).toHaveBeenCalledWith([
      expect.objectContaining({ itemId: "p1" }),
      expect.objectContaining({ resource: "new:res" }),
    ]);
  });

  it("removes a permission when the list fires delete", () => {
    const onChange = vi.fn();
    render(
      <OrganizationPermissionsField permissions={[perm(1), perm(2)]} onChange={onChange} />,
    );
    fireEvent.click(screen.getByText("delete-res:1"));
    expect(onChange).toHaveBeenCalledWith([expect.objectContaining({ itemId: "p2" })]);
  });

  it("shows a pagination summary when there are more permissions than a page", () => {
    const permissions = Array.from({ length: 7 }, (_, i) => perm(i));
    render(<OrganizationPermissionsField permissions={permissions} onChange={vi.fn()} />);
    expect(screen.getByText(/of 7 permissions/)).toBeInTheDocument();
    // The count badge next to the label shows the total.
    expect(screen.getByText("7")).toBeInTheDocument();
  });
});
