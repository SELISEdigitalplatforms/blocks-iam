import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  navigate: vi.fn(),
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
  lastOnSave: null as ((d: unknown) => void) | null,
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/hooks/use-scoped-path", () => ({ useScopedPath: () => (p: string) => `/scoped/${p}` }));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "t1" } })),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useAddPermission: () => ({ isPending: h.isPending, mutateAsync: h.mutateAsync }),
}));
vi.mock("@/components/breadcrumb/breadcrumb", () => ({ default: () => <nav /> }));
vi.mock("../permission-form", () => ({
  PermissionForm: ({ onSave }: { onSave: (d: unknown) => void }) => {
    h.lastOnSave = onSave;
    return (
      <button
        onClick={() =>
          onSave({ name: "Read", resource: "users:read", type: "2", dependentPermissions: ["p1"] })
        }
      >
        save-permission
      </button>
    );
  },
}));

import { AddPermission } from "./add-permission";

const renderPage = () => render(<MemoryRouter><AddPermission /></MemoryRouter>);

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("AddPermission", () => {
  it("renders the heading and permission form", () => {
    renderPage();
    expect(screen.getByText("New Permission")).toBeInTheDocument();
    expect(screen.getByText("save-permission")).toBeInTheDocument();
  });

  it("creates a permission and navigates on success", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderPage();

    fireEvent.click(screen.getByText("save-permission"));

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    expect(h.mutateAsync.mock.calls[0][0]).toMatchObject({
      name: "Read",
      type: 2,
      projectKey: "t1",
      isBuiltIn: false,
      dependentPermissions: ["p1"],
    });
    expect(h.showSuccessToast).toHaveBeenCalledWith({ description: "Permission created successfully" });
    expect(h.navigate).toHaveBeenCalledWith("/scoped/iam?tab=permissions");
  });

  it("shows an error toast when creation is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "dup" });
    renderPage();

    fireEvent.click(screen.getByText("save-permission"));
    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "dup" }));
  });

  it("shows a generic error toast when creation throws a plain error", async () => {
    h.mutateAsync.mockRejectedValue(new Error("network"));
    renderPage();

    fireEvent.click(screen.getByText("save-permission"));
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });
});
