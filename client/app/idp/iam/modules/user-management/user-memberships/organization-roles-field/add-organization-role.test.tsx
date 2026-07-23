import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { IRole } from "@blocks-idp/iam/models/role";

const h = vi.hoisted(() => ({
  showErrorToast: vi.fn(),
  roles: [
    { itemId: "role-1", name: "System User", slug: "clouduser", description: "" },
    { itemId: "role-2", name: "Test", slug: "test", description: "" },
  ] as IRole[],
}));

vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: h.showErrorToast,
}));

vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useGetRoles: () => ({
    data: { data: h.roles, totalCount: h.roles.length },
    isLoading: false,
  }),
}));

vi.mock("@seliseblocks/blocks-kit", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@seliseblocks/blocks-kit")>()),
  useProjectStore: () => ({ selectedProject: { tenantId: "default" } }),
}));

import { AddOrganizationRole } from "./add-organization-role";
import { OrganizationRolesField } from "./organization-roles-field";

const openDialog = () => {
  fireEvent.click(screen.getByRole("button", { name: /manage roles/i }));
};

beforeEach(() => vi.clearAllMocks());

describe("AddOrganizationRole", () => {
  it("shows the minimum-role requirement beside the modal title", async () => {
    render(
      <AddOrganizationRole roles={[h.roles[0]]} onChange={vi.fn()} />,
      { wrapper: createWrapper() },
    );

    openDialog();
    expect(await screen.findByPlaceholderText("Search by role name")).toHaveFocus();

    const infoButton = await screen.findByRole("button", {
      name: /role assignment requirement/i,
    });
    expect(infoButton).not.toHaveFocus();
    fireEvent.mouseEnter(infoButton);

    const tooltip = await screen.findByRole("tooltip");
    expect(tooltip).toHaveTextContent(
      "At least one role must remain assigned to this user to access resources.",
    );
    expect(tooltip).toHaveClass("max-w-[240px]");
  });

  it("prevents the final assigned role from being deselected", async () => {
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
      expect(screen.getByLabelText("1 out of 5 roles selected")).toHaveTextContent(
        "1/5 selected",
      );
      expect(h.showErrorToast).toHaveBeenCalledWith({
        errors: "At least one role must remain assigned to this user to access resources.",
      });
    });
    expect(screen.getByRole("button", { name: /^add$/i })).toBeDisabled();
    expect(onChange).not.toHaveBeenCalled();
    expect(onSave).not.toHaveBeenCalled();
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
    fireEvent.click(await screen.findByRole("checkbox", { name: /select test/i }));
    fireEvent.click(screen.getByRole("checkbox", { name: /deselect system user/i }));

    expect(onChange).not.toHaveBeenCalled();
    expect(onSave).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /^add$/i }));

    expect(onChange).toHaveBeenCalledWith([h.roles[1]]);
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
  });
});

describe("OrganizationRolesField", () => {
  it("shows the minimum-role requirement beside the Roles title", async () => {
    render(
      <OrganizationRolesField roles={[h.roles[0]]} onChange={vi.fn()} />,
      { wrapper: createWrapper() },
    );

    const infoButton = screen.getByRole("button", {
      name: /role assignment requirement/i,
    });
    fireEvent.mouseEnter(infoButton);

    const tooltip = await screen.findByRole("tooltip");
    expect(tooltip).toHaveTextContent(
      "At least one role must remain assigned to this user to access resources.",
    );
    expect(tooltip).toHaveClass("max-w-[240px]");
  });

  it("prevents removing the final table role and skips saving", async () => {
    const onChange = vi.fn();
    const onSave = vi.fn();

    render(
      <OrganizationRolesField
        roles={[h.roles[0]]}
        onChange={onChange}
        onSave={onSave}
      />,
      { wrapper: createWrapper() },
    );

    fireEvent.click(screen.getByRole("button", { name: /remove system user/i }));
    fireEvent.click(await screen.findByRole("button", { name: /^yes$/i }));

    expect(h.showErrorToast).toHaveBeenCalledWith({
      errors: "At least one role must remain assigned to this user to access resources.",
    });
    expect(onChange).not.toHaveBeenCalled();
    expect(onSave).not.toHaveBeenCalled();
  });

  it("still removes and saves a table role when another role remains", async () => {
    const onChange = vi.fn();
    const onSave = vi.fn();

    render(
      <OrganizationRolesField
        roles={h.roles}
        onChange={onChange}
        onSave={onSave}
      />,
      { wrapper: createWrapper() },
    );

    fireEvent.click(screen.getByRole("button", { name: /remove system user/i }));
    fireEvent.click(await screen.findByRole("button", { name: /^yes$/i }));

    const updateRoles = onChange.mock.calls[0][0] as (roles: IRole[]) => IRole[];
    expect(updateRoles(h.roles)).toEqual([h.roles[1]]);
    expect(h.showErrorToast).not.toHaveBeenCalled();
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
  });
});
