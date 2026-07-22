import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { IRole } from "@blocks-idp/iam/models/role";

const h = vi.hoisted(() => ({
  roles: [
    { itemId: "role-1", name: "System User", slug: "clouduser", description: "" },
    { itemId: "role-2", name: "Test", slug: "test", description: "" },
  ] as IRole[],
}));

vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useGetRoles: () => ({
    data: { data: h.roles, totalCount: h.roles.length },
    isLoading: false,
  }),
}));

vi.mock("@seliseblocks/blocks-kit", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "default" } }),
}));

import { AddOrganizationRole } from "./add-organization-role";

const openDialog = () => {
  fireEvent.click(screen.getByRole("button", { name: /manage roles/i }));
};

beforeEach(() => vi.clearAllMocks());

describe("AddOrganizationRole", () => {
  it("allows the final assigned role to be deselected and confirmed", async () => {
    const onChange = vi.fn();
    const onSave = vi.fn();

    render(
      <AddOrganizationRole
        roles={[h.roles[0]]}
        onChange={onChange}
        onSave={onSave}
      />,
      { wrapper: createWrapper() },
    );

    openDialog();
    expect(await screen.findByLabelText("1 out of 5 roles selected")).toHaveTextContent(
      "1/5 selected",
    );

    fireEvent.click(screen.getByRole("checkbox", { name: /deselect system user/i }));

    await waitFor(() => {
      expect(screen.getByLabelText("0 out of 5 roles selected")).toHaveTextContent(
        "0/5 selected",
      );
      expect(screen.getByRole("button", { name: /^add$/i })).toBeEnabled();
    });
    expect(onChange).not.toHaveBeenCalled();
    expect(onSave).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));
    expect(onChange).toHaveBeenCalledWith([]);
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
  });

  it("discards role removals when cancelled", async () => {
    const onChange = vi.fn();
    const onSave = vi.fn();

    render(
      <AddOrganizationRole
        roles={[h.roles[0]]}
        onChange={onChange}
        onSave={onSave}
      />,
      { wrapper: createWrapper() },
    );

    openDialog();
    fireEvent.click(await screen.findByRole("checkbox", { name: /deselect system user/i }));
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));

    expect(onChange).not.toHaveBeenCalled();
    expect(onSave).not.toHaveBeenCalled();
  });

  it("discards staged changes when closed with the top-right close button", async () => {
    const onChange = vi.fn();
    const onSave = vi.fn();

    render(
      <AddOrganizationRole
        roles={[h.roles[0]]}
        onChange={onChange}
        onSave={onSave}
      />,
      { wrapper: createWrapper() },
    );

    openDialog();
    fireEvent.click(await screen.findByRole("checkbox", { name: /deselect system user/i }));
    fireEvent.click(screen.getByRole("button", { name: /^close$/i }));

    expect(onChange).not.toHaveBeenCalled();
    expect(onSave).not.toHaveBeenCalled();

    openDialog();
    expect(
      await screen.findByRole("checkbox", { name: /deselect system user/i }),
    ).toBeChecked();
  });

  it("commits the complete staged selection only after Add is clicked", async () => {
    const onChange = vi.fn();
    const onSave = vi.fn();

    render(
      <AddOrganizationRole
        roles={[h.roles[0]]}
        onChange={onChange}
        onSave={onSave}
      />,
      { wrapper: createWrapper() },
    );

    openDialog();
    fireEvent.click(await screen.findByRole("checkbox", { name: /deselect system user/i }));
    fireEvent.click(screen.getByRole("checkbox", { name: /select test/i }));

    expect(onChange).not.toHaveBeenCalled();
    expect(onSave).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));

    expect(onChange).toHaveBeenCalledWith([h.roles[1]]);
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
  });
});
