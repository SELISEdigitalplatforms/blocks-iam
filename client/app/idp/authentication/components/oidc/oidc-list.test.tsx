import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  result: { isLoading: false, isFetching: false, data: undefined as unknown },
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "t1" } })),
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth-oidc", () => ({
  useGetAuthOidcCredentials: () => h.result,
}));
vi.mock("./oidc-card", () => ({
  OIDCCard: ({ oidc }: { oidc: { clientDisplayName: string } }) => (
    <div data-testid="oidc-card">{oidc.clientDisplayName}</div>
  ),
}));

import { OidcList } from "./oidc-list";

beforeEach(() => {
  vi.clearAllMocks();
  h.result = { isLoading: false, isFetching: false, data: undefined };
});

describe("OidcList", () => {
  it("shows the loading skeleton while fetching", () => {
    h.result = { isLoading: true, isFetching: false, data: undefined };
    const { container } = render(<OidcList />);
    expect(container.querySelectorAll("[class*='rounded']").length).toBeGreaterThan(0);
    expect(screen.queryByTestId("oidc-card")).toBeNull();
  });

  it("shows the empty state when there are no credentials", () => {
    h.result = { isLoading: false, isFetching: false, data: { oIDCClientCredentials: [] } };
    render(<OidcList />);
    expect(screen.getByText(/No OIDC configuration found/)).toBeInTheDocument();
  });

  it("renders a card per credential, newest first", () => {
    h.result = {
      isLoading: false,
      isFetching: false,
      data: {
        oIDCClientCredentials: [
          { itemId: "a", clientDisplayName: "Older", createdDate: "2025-01-01T00:00:00Z" },
          { itemId: "b", clientDisplayName: "Newer", createdDate: "2025-06-01T00:00:00Z" },
        ],
      },
    };
    render(<OidcList />);
    const cards = screen.getAllByTestId("oidc-card");
    expect(cards).toHaveLength(2);
    expect(cards[0]).toHaveTextContent("Newer");
    expect(cards[1]).toHaveTextContent("Older");
  });

  it("normalises a single (non-array) credential object", () => {
    h.result = {
      isLoading: false,
      isFetching: false,
      data: {
        oIDCClientCredentials: {
          itemId: "solo",
          clientDisplayName: "Solo Client",
          createdDate: "2025-03-01T00:00:00Z",
        },
      },
    };
    render(<OidcList />);
    expect(screen.getByText("Solo Client")).toBeInTheDocument();
  });
});
