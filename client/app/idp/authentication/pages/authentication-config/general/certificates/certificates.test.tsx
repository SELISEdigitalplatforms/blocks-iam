import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  cert: { isLoading: false, data: undefined as unknown },
  jwt: { data: undefined as unknown, isLoading: false },
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "t1" } })),
}));
vi.mock("@blocks-idp/authentication/hooks/use-identifier", () => ({
  useGetSavedPublicCertificates: () => h.cert,
}));
vi.mock("@blocks-idp/authentication/hooks/use-jwt-claim", () => ({
  useGetJwtClaim: () => h.jwt,
}));
vi.mock("./empty-configuration", () => ({
  EmptyConfiguration: () => <div data-testid="empty-config" />,
}));
vi.mock("./add-edit-provider-modal", () => ({
  AddEditProviderModal: () => <div data-testid="add-edit-modal" />,
}));
vi.mock("./map-jwt-claim-modal", () => ({
  default: ({ open }: { open: boolean }) => (open ? <div data-testid="jwt-modal" /> : null),
}));

import { Certificates } from "./certificates";

beforeEach(() => {
  vi.clearAllMocks();
  h.cert = { isLoading: false, data: undefined };
  h.jwt = { data: undefined, isLoading: false };
});

describe("Certificates", () => {
  it("shows the loading skeleton", () => {
    h.cert = { isLoading: true, data: undefined };
    const { container } = render(<Certificates />);
    expect(container.querySelectorAll("[class*='rounded']").length).toBeGreaterThan(0);
  });

  it("shows the empty configuration when nothing is configured", () => {
    h.cert = { isLoading: false, data: { isConfigured: false } };
    render(<Certificates />);
    expect(screen.getByTestId("empty-config")).toBeInTheDocument();
  });

  it("renders the configured provider details", () => {
    h.cert = {
      isLoading: false,
      data: {
        isConfigured: true,
        providerName: "Keycloak",
        jwksUrl: "https://idp.example.com/jwks",
        issuer: "issuer-1",
        audiences: ["aud-a", "aud-b"],
      },
    };
    h.jwt = { data: { itemId: "claim-1" }, isLoading: false };
    render(<Certificates />);
    expect(screen.getByText("External IdP")).toBeInTheDocument();
    expect(screen.getByText("Keycloak")).toBeInTheDocument();
    expect(screen.getByText("https://idp.example.com/jwks")).toBeInTheDocument();
    expect(screen.getByText("issuer-1")).toBeInTheDocument();
    expect(screen.getByText("aud-a, aud-b")).toBeInTheDocument();
  });

  it("warns when the JWT claims are not mapped and opens the mapping modal", () => {
    h.cert = {
      isLoading: false,
      data: { isConfigured: true, providerName: "Others", jwksUrl: "https://x", audiences: [] },
    };
    h.jwt = { data: undefined, isLoading: false };
    render(<Certificates />);
    expect(screen.getByText(/didn't map the jwt claims/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /map jwt claim/i }));
    expect(screen.getByTestId("jwt-modal")).toBeInTheDocument();
  });
});
