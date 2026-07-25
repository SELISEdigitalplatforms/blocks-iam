import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  data: undefined as { data: Record<string, unknown> } | undefined,
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUserById: () => ({ isLoading: h.isLoading, data: h.data }),
}));
vi.mock("@/components/copy-to-clipboard-button", () => ({
  CopyToClipboardButton: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
}));

import { UserBasicInformation } from "./user-basic-information";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.data = undefined;
});

describe("UserBasicInformation", () => {
  it("returns null when there is no user and it is not loading", () => {
    const { container } = render(<UserBasicInformation id="u1" projectKey="p1" />);
    expect(container).toBeEmptyDOMElement();
  });

  it("renders loading skeletons while the user loads", () => {
    h.isLoading = true;
    const { container } = render(<UserBasicInformation id="u1" projectKey="p1" />);
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("renders the user's name, email, status and meta fields", () => {
    h.data = {
      data: {
        firstName: "Ada",
        lastName: "Lovelace",
        email: "ada@example.com",
        active: true,
        isVerified: true,
        mfaEnabled: false,
        language: "en",
        logInCount: 7,
      },
    };
    render(<UserBasicInformation id="u1" projectKey="p1" />);
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("ada@example.com")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    // "Verified" appears both as a meta label and the status badge.
    expect(screen.getAllByText("Verified").length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText("Disabled")).toBeInTheDocument();
    expect(screen.getByText("7")).toBeInTheDocument();
  });

  it("shows inactive and not-verified states", () => {
    h.data = { data: { firstName: "Bob", active: false, isVerified: false, mfaEnabled: true } };
    render(<UserBasicInformation id="u1" projectKey="p1" />);
    expect(screen.getByText("Inactive")).toBeInTheDocument();
    expect(screen.getByText("Not verified")).toBeInTheDocument();
    expect(screen.getByText("Enabled")).toBeInTheDocument();
  });

  it("shows the username instead of redundant fields when hideRedundantFields is set", () => {
    h.data = { data: { userName: "ada", active: true } };
    render(<UserBasicInformation id="u1" projectKey="p1" hideRedundantFields />);
    expect(screen.getByText("@ada")).toBeInTheDocument();
    expect(screen.queryByText("Logins")).not.toBeInTheDocument();
  });
});
