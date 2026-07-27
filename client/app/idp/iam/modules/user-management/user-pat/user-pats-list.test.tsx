import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@/hooks/use-is-mobile", () => ({ default: () => false }));
vi.mock("@/components/copy-to-clipboard-button/copy-to-clipboard-button", () => ({
  CopyToClipboardButton: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

import { UserPATList } from "./user-pats-list";

const future = new Date(Date.now() + 86400000).toISOString();
const past = new Date(Date.now() - 86400000).toISOString();

const pat = (over: Record<string, unknown> = {}) =>
  ({ note: "ci token", code: "pat-abc", expiryDate: future, ...over }) as unknown as Parameters<
    typeof UserPATList
  >[0]["data"][number];

beforeEach(() => vi.clearAllMocks());

describe("UserPATList", () => {
  it("renders a row per token with an active badge for valid tokens", () => {
    render(<UserPATList isLoading={false} data={[pat()]} />);
    expect(screen.getByText("ci token")).toBeInTheDocument();
    expect(screen.getByText("active")).toBeInTheDocument();
  });

  it("renders an expired badge for expired tokens", () => {
    render(<UserPATList isLoading={false} data={[pat({ expiryDate: past })]} />);
    expect(screen.getByText("expired")).toBeInTheDocument();
  });

  it("shows the no-results row when there are no tokens", () => {
    render(<UserPATList isLoading={false} data={[]} />);
    expect(screen.getByText("No results.")).toBeInTheDocument();
  });

  it("renders the loading skeleton while loading", () => {
    const { container } = render(<UserPATList isLoading data={[]} />);
    expect(container.querySelector(".grid")).not.toBeNull();
    expect(screen.queryByText("No results.")).toBeNull();
  });
});
