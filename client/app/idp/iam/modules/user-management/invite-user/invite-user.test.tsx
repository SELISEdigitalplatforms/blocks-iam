import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const h = vi.hoisted(() => ({
  createUser: vi.fn(),
  updateUserAccess: vi.fn(),
  checkExists: { data: undefined as unknown, isFetching: false },
  orgs: { data: { organizations: [] as unknown[] }, isLoading: false },
  // Multi-org OFF keeps the org picker hidden, so the happy path is just
  // email + first name + last name.
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

import { InviteUser } from "./invite-user";

const renderInvite = () => render(<InviteUser />, { wrapper: createWrapper() });

beforeEach(() => {
  vi.clearAllMocks();
  h.checkExists = { data: undefined, isFetching: false };
});

describe("InviteUser", () => {
  it("renders the trigger button", () => {
    renderInvite();
    expect(screen.getByRole("button", { name: /invite user/i })).toBeInTheDocument();
  });

  it("opens the dialog with the email field", async () => {
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite user/i }));
    expect(await screen.findByText("Add a user to an organization.")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("name@company.com")).toBeInTheDocument();
  });

  it("does not collect the name — it is provided by the user at activation", async () => {
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite user/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "new@user.com");
    // The send button becomes enabled from the email alone, and no name inputs appear.
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /send invite/i })).not.toBeDisabled(),
    );
    expect(screen.queryByPlaceholderText("Enter first name")).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Enter last name")).not.toBeInTheDocument();
  });

  it("creates a new user with empty names and shows a success toast", async () => {
    h.createUser.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite user/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "new@user.com");

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() => expect(h.createUser).toHaveBeenCalled());
    const payload = h.createUser.mock.calls[0][0];
    expect(payload).toMatchObject({
      email: "new@user.com",
      firstName: "",
      lastName: "",
      userPassType: 1,
      userCreationType: 1,
      platform: "blocks_portal",
    });
    expect(payload).not.toHaveProperty("organizationIds");
    expect(h.showSuccessToast).toHaveBeenCalledWith({ description: "Invitation is sent" });
  });

  it("creates a new user with organizationId when multi-org is enabled", async () => {
    h.config = { data: { isMultiOrgEnabled: true }, isLoading: false };
    h.orgs = {
      data: { organizations: [{ itemId: "org-1", name: "Acme Org", isDisabled: false }] },
      isLoading: false,
    };
    h.createUser.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite user/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "new@user.com");

    await user.click(await screen.findByRole("combobox"));
    await user.click(await screen.findByText("Acme Org"));

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() => expect(h.createUser).toHaveBeenCalled());
    const payload = h.createUser.mock.calls[0][0];
    expect(payload).toMatchObject({
      email: "new@user.com",
      organizationId: "org-1",
    });
    expect(payload).not.toHaveProperty("organizationIds");
  });

  const fillNewUser = async (user: ReturnType<typeof userEvent.setup>) => {
    await user.click(screen.getByRole("button", { name: /invite user/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "new@user.com");
  };

  it("shows an error toast when user creation is unsuccessful", async () => {
    h.createUser.mockResolvedValue({ isSuccess: false, errors: { email: "already invited" } });
    const user = userEvent.setup();
    renderInvite();
    await fillNewUser(user);

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "already invited" }));
    expect(h.showSuccessToast).not.toHaveBeenCalled();
  });

  it("shows the mapped error toast when creation throws with errors", async () => {
    h.createUser.mockRejectedValue({ errors: { email: "boom" } });
    const user = userEvent.setup();
    renderInvite();
    await fillNewUser(user);

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: { email: "boom" } }),
    );
  });

  it("shows a generic error toast when creation throws a plain error", async () => {
    h.createUser.mockRejectedValue(new Error("network"));
    const user = userEvent.setup();
    renderInvite();
    await fillNewUser(user);

    const submit = screen.getByRole("button", { name: /send invite/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });

  it("warns and blocks submit when an existing user is found with multi-org disabled", async () => {
    h.checkExists = { data: { userId: "u1", organizationIds: [] }, isFetching: false };
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite user/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "existing@user.com");

    expect(
      await screen.findByText("A user with this email already exists in the system."),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /grant access/i })).toBeDisabled();
  });

  it("grants an existing user access to a selected organization", async () => {
    h.config = { data: { isMultiOrgEnabled: true }, isLoading: false };
    h.orgs = {
      data: { organizations: [{ itemId: "org-1", name: "Acme Org", isDisabled: false }] },
      isLoading: false,
    };
    h.checkExists = { data: { userId: "u1", organizationIds: [] }, isFetching: false };
    h.updateUserAccess.mockResolvedValue({ isSuccess: true });
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite user/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "existing@user.com");

    await user.click(await screen.findByRole("combobox"));
    await user.click(await screen.findByText("Acme Org"));

    const submit = screen.getByRole("button", { name: /grant access/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() => expect(h.updateUserAccess).toHaveBeenCalled());
    expect(h.updateUserAccess.mock.calls[0][0]).toMatchObject({ organizationId: "org-1" });
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "User granted access to the organization",
    });
  });

  it("shows an error toast when granting access is unsuccessful", async () => {
    h.config = { data: { isMultiOrgEnabled: true }, isLoading: false };
    h.orgs = {
      data: { organizations: [{ itemId: "org-1", name: "Acme Org", isDisabled: false }] },
      isLoading: false,
    };
    h.checkExists = { data: { userId: "u1", organizationIds: [] }, isFetching: false };
    h.updateUserAccess.mockResolvedValue({ isSuccess: false, errors: { org: "denied" } });
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite user/i }));
    await user.type(screen.getByPlaceholderText("name@company.com"), "existing@user.com");

    await user.click(await screen.findByRole("combobox"));
    await user.click(await screen.findByText("Acme Org"));

    const submit = screen.getByRole("button", { name: /grant access/i });
    await waitFor(() => expect(submit).not.toBeDisabled());
    await user.click(submit);

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "denied" }));
  });

  it("closes the dialog from the Cancel button", async () => {
    const user = userEvent.setup();
    renderInvite();
    await user.click(screen.getByRole("button", { name: /invite user/i }));
    await screen.findByText("Add a user to an organization.");

    await user.click(screen.getByRole("button", { name: /cancel/i }));
    await waitFor(() =>
      expect(screen.queryByText("Add a user to an organization.")).toBeNull(),
    );
  });
});
