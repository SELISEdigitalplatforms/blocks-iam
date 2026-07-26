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
  h.orgs = { data: { organizations: [] as unknown[] }, isLoading: false };
  h.config = { data: { isMultiOrgEnabled: false }, isLoading: false };
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

  it("does not collect the name — it is provided by the user at activation", async () => {
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "member@org.com");
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /send invite/i })).not.toBeDisabled(),
    );
    expect(screen.queryByPlaceholderText("Enter first name")).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Enter last name")).not.toBeInTheDocument();
  });

  it("invites a new member with empty names and shows a success toast", async () => {
    h.createUser.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "member@org.com");

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() => expect(h.createUser).toHaveBeenCalled());
    expect(h.createUser.mock.calls[0][0]).toMatchObject({
      email: "member@org.com",
      firstName: "",
      lastName: "",
      platform: "blocks_portal",
    });
    expect(h.showSuccessToast).toHaveBeenCalledWith({ description: "Invitation is sent" });
  });

  const fillNewMember = async (user: ReturnType<typeof userEvent.setup>) => {
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "member@org.com");
    await user.type(await screen.findByPlaceholderText("Enter first name"), "Grace");
    await user.type(screen.getByPlaceholderText("Enter last name"), "Hopper");
  };

  it("shows the first error message when the invite is unsuccessful", async () => {
    h.createUser.mockResolvedValue({ isSuccess: false, errors: { email: "already a member" } });
    const user = userEvent.setup();
    renderInvite();
    await fillNewMember(user);

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "already a member" }),
    );
  });

  it("shows a string error message directly when the invite fails", async () => {
    h.createUser.mockResolvedValue({ isSuccess: false, errors: "server rejected" });
    const user = userEvent.setup();
    renderInvite();
    await fillNewMember(user);

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "server rejected" }));
  });

  it("shows the mapped error toast when the invite throws with errors", async () => {
    h.createUser.mockRejectedValue({ errors: { email: "boom" } });
    const user = userEvent.setup();
    renderInvite();
    await fillNewMember(user);

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: { email: "boom" } }),
    );
  });

  it("shows a generic error toast when the invite throws a plain error", async () => {
    h.createUser.mockRejectedValue(new Error("network"));
    const user = userEvent.setup();
    renderInvite();
    await fillNewMember(user);

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });

  it("grants an existing user access to the organization", async () => {
    h.checkExists = { data: { userId: "u1", organizationIds: [] }, isFetching: false };
    h.updateUserAccess.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "existing@org.com");

    const submit = await screen.findByRole("button", { name: /grant access/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() => expect(h.updateUserAccess).toHaveBeenCalled());
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "User granted access to the organization",
    });
  });

  it("shows the first array error when granting access fails", async () => {
    h.checkExists = { data: { userId: "u1", organizationIds: [] }, isFetching: false };
    h.updateUserAccess.mockResolvedValue({ isSuccess: false, errors: ["denied by policy"] });
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "existing@org.com");

    const submit = await screen.findByRole("button", { name: /grant access/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "denied by policy" }),
    );
  });

  it("closes the dialog from the Cancel button", async () => {
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    await screen.findByText("Add a member to this organization.");

    await user.click(screen.getByRole("button", { name: /cancel/i }));
    await waitFor(() =>
      expect(screen.queryByText("Add a member to this organization.")).toBeNull(),
    );
  });

  it("falls back to a generic error message when the invite fails with no errors", async () => {
    h.createUser.mockResolvedValue({ isSuccess: false });
    const user = userEvent.setup();
    renderInvite();
    await fillNewMember(user);

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Failed to invite member" }),
    );
  });

  it("shows the organization picker and lets a real org be selected", async () => {
    h.config = { data: { isMultiOrgEnabled: true }, isLoading: false };
    h.orgs = {
      data: {
        organizations: [
          { itemId: "org-1", name: "Acme", isDisabled: false },
          { itemId: "org-2", name: "Beta", isDisabled: false },
        ],
      },
      isLoading: false,
    };
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "member@org.com");

    const combobox = await screen.findByRole("combobox");
    await user.click(combobox);
    await user.click(await screen.findByText("Beta"));

    await waitFor(() =>
      expect(screen.getByRole("combobox")).toHaveTextContent("Beta"),
    );
  });

  it("selects the default organization when the workspace has no real orgs", async () => {
    h.config = { data: { isMultiOrgEnabled: true }, isLoading: false };
    h.orgs = { data: { organizations: [] }, isLoading: false };
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "member@org.com");

    const combobox = await screen.findByRole("combobox");
    await waitFor(() => expect(combobox).toHaveTextContent("Default"));
  });

  it("drops the selection when the existing user already belongs to that org", async () => {
    h.config = { data: { isMultiOrgEnabled: true }, isLoading: false };
    h.orgs = {
      data: { organizations: [{ itemId: "org-1", name: "Acme", isDisabled: false }] },
      isLoading: false,
    };
    h.checkExists = {
      data: { userId: "u1", organizationIds: ["org-1"] },
      isFetching: false,
    };
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite member/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "existing@org.com");

    const combobox = await screen.findByRole("combobox");
    await waitFor(() => expect(combobox).toHaveTextContent("Select organization"));
  });
});
