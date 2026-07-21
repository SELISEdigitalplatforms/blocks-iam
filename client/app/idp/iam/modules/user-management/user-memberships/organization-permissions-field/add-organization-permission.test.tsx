import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { IPermission } from "@blocks-idp/iam/models/permission";

const h = vi.hoisted(() => {
  const makePermission = (index: number): IPermission =>
    ({
      itemId: `perm-${index}`,
      name: `Permission ${index}`,
      resource: `blocks-iam::perm-${index}`,
      resourceGroup: "blocks-iam",
      type: 1,
      description: "",
      projectKey: "",
      tags: [],
      roles: [],
      dependentPermissions: [],
      isArchived: false,
      isBuiltIn: true,
      language: null,
      organizationIds: [],
      permissionSeverity: 0,
    }) as IPermission;

  return { showErrorToast: vi.fn(), makePermission };
});

vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetPermissions: (filter: { page: number }) => {
    const start = filter.page * 5;
    return {
      data: {
        totalCount: 10,
        data: Array.from({ length: 5 }, (_, index) => h.makePermission(start + index + 1)),
      },
      isLoading: false,
    };
  },
}));

vi.mock("@seliseblocks/blocks-kit", () => ({
  useProjectStore: () => ({
    selectedProject: { tenantId: "default" },
  }),
}));

vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: h.showErrorToast,
}));

import { AddOrganizationPermission } from "./add-organization-permission";

const makePermission = h.makePermission;

const openDialog = () => {
  fireEvent.click(screen.getByRole("button", { name: /manage permissions/i }));
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe("AddOrganizationPermission UI", () => {
  it("TC-12: shows live selected count badge", async () => {
    render(<AddOrganizationPermission permissions={[]} onAdd={vi.fn()} />, {
      wrapper: createWrapper(),
    });

    openDialog();

    expect(await screen.findByLabelText("0 out of 5 permissions selected")).toHaveTextContent(
      "0/5 selected",
    );

    fireEvent.click(screen.getByRole("checkbox", { name: /select permission 1/i }));

    await waitFor(() => {
      expect(screen.getByLabelText("1 out of 5 permissions selected")).toHaveTextContent(
        "1/5 selected",
      );
    });
  });

  it("TC-03/TC-09: disables extra permissions when five are already assigned", async () => {
    const assigned = Array.from({ length: 5 }, (_, index) => makePermission(index + 1));

    render(<AddOrganizationPermission permissions={assigned} onAdd={vi.fn()} />, {
      wrapper: createWrapper(),
    });

    openDialog();

    expect(await screen.findByLabelText("5 out of 5 permissions selected")).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", {
        name: /permission 1 already assigned/i,
      }),
    ).toBeDisabled();
  });

  it("TC-19: cancel closes without calling onAdd", async () => {
    const onAdd = vi.fn();

    render(<AddOrganizationPermission permissions={[]} onAdd={onAdd} />, {
      wrapper: createWrapper(),
    });

    openDialog();
    fireEvent.click(await screen.findByRole("checkbox", { name: /select permission 1/i }));
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));

    expect(onAdd).not.toHaveBeenCalled();
  });

  it("TC-30: add button stays disabled until a new permission is selected", async () => {
    render(<AddOrganizationPermission permissions={[]} onAdd={vi.fn()} />, {
      wrapper: createWrapper(),
    });

    openDialog();

    expect(await screen.findByRole("button", { name: /^add$/i })).toBeDisabled();

    fireEvent.click(screen.getByRole("checkbox", { name: /select permission 1/i }));

    await waitFor(() => {
      expect(screen.getByRole("button", { name: /^add$/i })).toBeEnabled();
    });
  });

  it("TC-07: keeps selections when paginating", async () => {
    render(<AddOrganizationPermission permissions={[]} onAdd={vi.fn()} />, {
      wrapper: createWrapper(),
    });

    openDialog();
    fireEvent.click(await screen.findByRole("checkbox", { name: /select permission 1/i }));

    const pagination = screen.getByText(/page 1 of 2/i).closest("div")?.parentElement;
    expect(pagination).toBeTruthy();

    const navButtons = within(pagination as HTMLElement).getAllByRole("button");
    fireEvent.click(navButtons[2]);

    await waitFor(() => {
      expect(screen.getByText(/page 2 of 2/i)).toBeInTheDocument();
      expect(screen.getByLabelText("1 out of 5 permissions selected")).toBeInTheDocument();
    });
  });
});
