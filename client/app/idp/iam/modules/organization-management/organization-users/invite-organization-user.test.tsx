import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const h = vi.hoisted(() => ({
  createUser: vi.fn(),
  updateUserAccess: vi.fn(),
  checkExists: { data: undefined as unknown, isFetching: false },
  orgs: { data: { organizations: [] as unknown[] }, isLoading: false },
  config: { data: { isMultiOrgEnabled: false }, isLoading: false },
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useAddUser: () => ({ isPending: false, mutateAsync: h.createUser }),
  useCheckUserExists: () => h.checkExists,
  useUpdateUserAccessControl: () => ({ mutateAsync: h.updateUserAccess, isPending: false }),
}));
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => h.orgs,
  useGetOrganizationConfig: () => h.config,
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({
    selectedProject: { tenantId: "t1", itemId: "p1" },
    selectedTenantGroup: "tg1",
  })),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));

import { InviteOrganizationUser } from "./invite-organization-user";

const renderInvite = () =>
  render(<InviteOrganizationUser organizationId="org-1" />, { wrapper: createWrapper() });

beforeEach(() => {
  vi.clearAllMocks();
  h.checkExists = { data: undefined, isFetching: false };
});

describe("InviteOrganizationUser", () => {
  it("renders the Invite Member trigger", () => {
    renderInvite();
    expect(screen.getByRole("button", { name: /invite member/i })).toBeInTheDocument();
  });

  it("opens the dialog with the email field", async () => {
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    expect(await screen.findByText("Add a member to this organization.")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("name@company.com")).toBeInTheDocument();
  });

  it("reveals the name fields once a valid email is entered", async () => {
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "member@org.com");
    expect(await screen.findByPlaceholderText("Enter first name")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter last name")).toBeInTheDocument();
  });

  it("invites a new member and shows a success toast", async () => {
    h.createUser.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "member@org.com");
    await user.type(await screen.findByPlaceholderText("Enter first name"), "Grace");
    await user.type(screen.getByPlaceholderText("Enter last name"), "Hopper");

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() => expect(h.createUser).toHaveBeenCalled());
    expect(h.createUser.mock.calls[0][0]).toMatchObject({
      email: "member@org.com",
      firstName: "Grace",
      lastName: "Hopper",
      platform: "blocks_portal",
    });
    expect(h.showSuccessToast).toHaveBeenCalledWith({ description: "Invitation is sent" });
  });
});
