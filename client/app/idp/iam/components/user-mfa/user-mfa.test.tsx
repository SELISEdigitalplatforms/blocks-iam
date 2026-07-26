import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mfaConfig: {} as Record<string, unknown>,
  userById: {} as Record<string, unknown>,
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({ useGetUserById: () => h.userById }));
vi.mock("@blocks-idp/mfa/hooks/use-mfa-config", () => ({ useGetMFAConfig: () => h.mfaConfig }));
vi.mock("./user-mfa-confirmation/user-mfa-confirmation-disable", () => ({
  UserMFAConfirmationDisable: () => <div data-testid="mfa-disable" />,
}));
vi.mock("./user-mfa-detail", () => ({ UserMFADetails: () => <div data-testid="mfa-details" /> }));
vi.mock("@/constants/endpoint.constant", () => ({ BLOCKS_OS_BASE_URL: "https://os.test" }));

import { UserMFA } from "./user-mfa";

beforeEach(() => {
  vi.clearAllMocks();
  h.userById = { isLoading: false, isFetching: false, data: { data: { mfaEnabled: true } } };
});

describe("UserMFA", () => {
  it("renders the loading skeleton while the mfa config loads", () => {
    h.mfaConfig = { isLoading: true, data: undefined };
    render(<UserMFA userId="u1" projectKey="p1" />);
    expect(screen.queryByTestId("mfa-details")).toBeNull();
  });

  it("renders the project-level MFA prompt when mfa is not enabled for the project", () => {
    h.mfaConfig = { isLoading: false, data: { enabled: false } };
    render(<UserMFA userId="u1" projectKey="p1" />);
    expect(screen.getByText("Go to MFA Settings")).toBeInTheDocument();
  });

  it("renders the user MFA config with the disable control when mfa is enabled", () => {
    h.mfaConfig = { isLoading: false, data: { enabled: true } };
    render(<UserMFA userId="u1" projectKey="p1" />);
    expect(screen.getByText("Multi-factor Authentication")).toBeInTheDocument();
    expect(screen.getByTestId("mfa-details")).toBeInTheDocument();
    expect(screen.getByTestId("mfa-disable")).toBeInTheDocument();
  });
});
