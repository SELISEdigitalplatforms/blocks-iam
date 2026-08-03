import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  meData: undefined as { data: Record<string, unknown> } | undefined,
  userByIdData: undefined as { data: Record<string, unknown> } | undefined,
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetMe: () => ({ data: h.meData }),
  useGetUserById: () => ({ data: h.userByIdData }),
}));
vi.mock("@blocks-idp/iam/components/profile-image-uploader", () => ({
  ProfileImageUploader: () => <div data-testid="image-uploader" />,
}));
vi.mock("@/components/copy-to-clipboard-button", () => ({
  CopyToClipboardButton: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
}));

import { UserProfileSidebar } from "./user-profile-sidebar";

beforeEach(() => {
  vi.clearAllMocks();
  h.meData = undefined;
  h.userByIdData = undefined;
});

describe("UserProfileSidebar", () => {
  it("renders the current user's details when own", () => {
    h.meData = {
      data: {
        firstName: "Ada",
        lastName: "Lovelace",
        email: "ada@example.com",
        active: true,
        logInCount: 4,
        lastLoggedInTime: "2022-05-01T10:00:00Z",
      },
    };
    render(<UserProfileSidebar id="u1" projectKey="p1" own />);
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("ada@example.com")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("4")).toBeInTheDocument();
  });

  it("renders another user's details when not own", () => {
    h.userByIdData = { data: { firstName: "Bob", active: false } };
    render(<UserProfileSidebar id="u2" projectKey="p1" />);
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("shows Never when there is no valid last login", () => {
    h.meData = { data: { firstName: "Ada", active: true } };
    render(<UserProfileSidebar id="u1" projectKey="p1" own />);
    expect(screen.getByText("Never")).toBeInTheDocument();
  });
});
