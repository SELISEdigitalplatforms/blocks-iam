import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { IPermission } from "@blocks-idp/iam/models/permission";

vi.mock("./add-dependent-permission", () => ({
  AddDependentPermission: ({ onAdd }: { onAdd: (p: IPermission[]) => void }) => (
    <button onClick={() => onAdd([{ resource: "new:res" } as IPermission])}>add-dep</button>
  ),
}));

import { DependentPermissions } from "./dependent-permissions";

describe("DependentPermissions", () => {
  it("renders a badge per resource", () => {
    render(
      <DependentPermissions permissionsResource={["a:read", "b:write"]} onChange={vi.fn()} />,
    );
    expect(screen.getByText("a:read")).toBeInTheDocument();
    expect(screen.getByText("b:write")).toBeInTheDocument();
  });

  it("appends newly added permission resources", () => {
    const onChange = vi.fn();
    render(<DependentPermissions permissionsResource={["a:read"]} onChange={onChange} />);
    fireEvent.click(screen.getByText("add-dep"));
    expect(onChange).toHaveBeenCalledWith(["a:read", "new:res"]);
  });

  it("removes a resource when its badge remove icon is clicked", () => {
    const onChange = vi.fn();
    const { container } = render(
      <DependentPermissions permissionsResource={["a:read", "b:write"]} onChange={onChange} />,
    );
    const removeIcons = container.querySelectorAll("svg.cursor-pointer");
    fireEvent.click(removeIcons[0]);
    expect(onChange).toHaveBeenCalledWith(["b:write"]);
  });
});
