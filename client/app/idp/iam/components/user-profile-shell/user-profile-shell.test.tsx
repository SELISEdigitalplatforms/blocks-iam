import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ tabId: "overview", setTabId: vi.fn(), me: {} as Record<string, unknown> }));

vi.mock("nuqs", () => ({
  useQueryState: (_key: string, opts: { defaultValue: string }) => [h.tabId || opts.defaultValue, h.setTabId],
}));
vi.mock("../user-profile-sidebar", () => ({
  UserProfileSidebar: () => <div data-testid="sidebar" />,
}));
vi.mock("@blocks-idp/iam/modules/user-management/update-user", () => ({
  UpdateUser: () => <div data-testid="update-user" />,
}));
vi.mock("@/components/breadcrumb/breadcrumb", () => ({ default: () => <div data-testid="breadcrumb" /> }));
vi.mock("@/components/copy-to-clipboard-button", () => ({
  CopyToClipboardButton: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));
vi.mock("@/constants/breadcrumb-custom-title", () => ({ BREADCRUMB_CUSTOM_TITLES: {} }));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetMe: () => h.me,
  useGetUserById: () => ({ data: undefined }),
}));

import { UserProfileShell } from "./user-profile-shell";

const tabs = [
  { value: "overview", label: "Overview", render: () => <div>overview-content</div> },
  { value: "security", label: "Security", render: () => <div>security-content</div> },
];

beforeEach(() => {
  vi.clearAllMocks();
  h.tabId = "overview";
  h.me = { data: { data: { firstName: "Ada", lastName: "Lovelace", email: "ada@x.com" } } };
});

describe("UserProfileShell", () => {
  it("renders the profile heading with the display name and email", () => {
    render(<UserProfileShell id="u1" projectKey="p1" own tabs={tabs} />);
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("ada@x.com")).toBeInTheDocument();
    expect(screen.getByTestId("sidebar")).toBeInTheDocument();
  });

  it("renders the tab triggers and the active tab content", () => {
    render(<UserProfileShell id="u1" projectKey="p1" own tabs={tabs} />);
    expect(screen.getAllByText("Overview").length).toBeGreaterThan(0);
    expect(screen.getByText("overview-content")).toBeInTheDocument();
  });

  it("falls back to the Profile heading when the user has no name", () => {
    h.me = { data: { data: { firstName: "", lastName: "", email: "" } } };
    render(<UserProfileShell id="u1" projectKey="p1" own tabs={tabs} />);
    expect(screen.getByText("Profile")).toBeInTheDocument();
  });
});
