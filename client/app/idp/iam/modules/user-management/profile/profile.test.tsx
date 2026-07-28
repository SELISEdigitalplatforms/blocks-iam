import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isPending: false,
  isLoading: false,
  data: undefined as { data: { itemId: string } } | undefined,
  shellProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetMe: () => ({ isPending: h.isPending, isLoading: h.isLoading, data: h.data }),
}));
vi.mock("@blocks-idp/iam/components/user-profile-shell", () => ({
  UserProfileShell: (props: Record<string, unknown>) => {
    h.shellProps = props;
    return <div data-testid="profile-shell" />;
  },
}));
vi.mock("@blocks-idp/iam/components/profile-details", () => ({
  ProfileDetails: () => <div />,
}));
vi.mock("../user-histories", () => ({ UserHistories: () => <div /> }));
vi.mock("../user-devices", () => ({ UserDevices: () => <div /> }));

import { Profile } from "./profile";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.isLoading = false;
  h.data = undefined;
});

describe("Profile", () => {
  it("renders the skeleton while the profile is loading", () => {
    h.isLoading = true;
    const { container } = render(<Profile />);
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
    expect(screen.queryByTestId("profile-shell")).not.toBeInTheDocument();
  });

  it("renders the profile shell with security, sessions and history tabs once loaded", () => {
    h.data = { data: { itemId: "u1" } };
    render(<Profile />);
    expect(screen.getByTestId("profile-shell")).toBeInTheDocument();
    const tabs = h.shellProps?.tabs as { value: string }[];
    expect(tabs.map((t) => t.value)).toEqual(["security", "devices", "history"]);
    expect(h.shellProps?.id).toBe("u1");
  });
});
