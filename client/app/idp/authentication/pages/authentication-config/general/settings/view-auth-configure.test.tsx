import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  isFetching: false,
  data: undefined as Record<string, unknown> | undefined,
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth-config", () => ({
  useGetAuthConfig: () => ({ data: h.data, isLoading: h.isLoading, isFetching: h.isFetching }),
}));
vi.mock("./url-with-actions", () => ({
  UrlWithActions: ({ url }: { url: string }) => <div data-testid="url">{url}</div>,
}));

import { ViewAuthConfigure } from "./view-auth-configure";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.isFetching = false;
  h.data = undefined;
});

describe("ViewAuthConfigure", () => {
  it("renders a loading skeleton while fetching", () => {
    h.isLoading = true;
    const { container } = render(<ViewAuthConfigure />);
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("renders the auth configuration values", () => {
    h.data = {
      accessTokenValidForNumberMinutes: 15,
      refreshTokenValidForNumberMinutes: 60,
      rememberMeRefreshTokenValidForNumberMinutes: 1440,
      getNumberOfWrongAttemptsToLockTheAccount: 5,
      accountLockDurationInMinutes: 30,
      publicCertificatePath: "https://cert.test",
    };
    render(<ViewAuthConfigure />);
    expect(screen.getByText("Access Token Validity")).toBeInTheDocument();
    expect(screen.getByText("15 minutes")).toBeInTheDocument();
    expect(screen.getByText("60 minutes")).toBeInTheDocument();
    expect(screen.getByText("5")).toBeInTheDocument();
    expect(screen.getByTestId("url")).toHaveTextContent("https://cert.test");
  });
});
