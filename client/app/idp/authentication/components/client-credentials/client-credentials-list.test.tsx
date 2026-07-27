import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  isFetching: false,
  data: [] as { itemId: string; createdDate: string }[],
}));

vi.mock("@blocks-idp/authentication/hooks/use-auth-clients", () => ({
  useGetAuthClientCredentials: () => ({
    isLoading: h.isLoading,
    isFetching: h.isFetching,
    data: h.data,
  }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("./client-credential-card", () => ({
  ClientCredentialsCard: ({ clientCredential }: { clientCredential: { itemId: string } }) => (
    <div data-testid="cc-card">{clientCredential.itemId}</div>
  ),
}));

import { ClientCredentialList } from "./client-credentials-list";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.isFetching = false;
  h.data = [];
});

describe("ClientCredentialList", () => {
  it("renders the loading skeleton while fetching", () => {
    h.isLoading = true;
    const { container } = render(<ClientCredentialList />);
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("renders the empty state when there are no credentials", () => {
    render(<ClientCredentialList />);
    expect(screen.getByText(/No client credential found/)).toBeInTheDocument();
  });

  it("renders a card per credential sorted by newest first", () => {
    h.data = [
      { itemId: "old", createdDate: "2020-01-01T00:00:00Z" },
      { itemId: "new", createdDate: "2022-01-01T00:00:00Z" },
    ];
    render(<ClientCredentialList />);
    const cards = screen.getAllByTestId("cc-card");
    expect(cards).toHaveLength(2);
    expect(cards[0]).toHaveTextContent("new");
    expect(cards[1]).toHaveTextContent("old");
  });
});
