import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";

const h = vi.hoisted(() => ({ urlParams: {} as Record<string, string> }));

vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  extractOIDCParams: () => h.urlParams,
}));

import { OIDCProvider, useOIDCContext } from "./oidc-layout";

const Consumer = () => {
  const ctx = useOIDCContext();
  return (
    <div>
      <span data-testid="tenant">{ctx.tenantId ?? "none"}</span>
      <span data-testid="theme">{ctx.themeColor}</span>
      <span data-testid="loading">{String(ctx.isLoading)}</span>
    </div>
  );
};

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  h.urlParams = {};
});

describe("oidc-layout OIDCProvider", () => {
  it("throws when useOIDCContext is used outside the provider", () => {
    const Bad = () => {
      useOIDCContext();
      return null;
    };
    expect(() => render(<Bad />)).toThrow("useOIDCContext must be used within OIDCProvider");
  });

  it("merges url params into the context and persists them", () => {
    h.urlParams = { tenantId: "tenant-1", state: "s1" };
    render(
      <MemoryRouter>
        <OIDCProvider>
          <Consumer />
        </OIDCProvider>
      </MemoryRouter>,
    );
    expect(screen.getByTestId("tenant")).toHaveTextContent("tenant-1");
    expect(screen.getByTestId("loading")).toHaveTextContent("false");
    expect(localStorage.getItem("oidc-flow-params")).not.toBeNull();
  });

  it("falls back to the default theme color when none is supplied", () => {
    render(
      <MemoryRouter>
        <OIDCProvider>
          <Consumer />
        </OIDCProvider>
      </MemoryRouter>,
    );
    expect(screen.getByTestId("theme")).toHaveTextContent("#124091");
  });

  it("reads previously stored params from localStorage", () => {
    localStorage.setItem("oidc-flow-params", JSON.stringify({ tenantId: "stored-tenant" }));
    render(
      <MemoryRouter>
        <OIDCProvider>
          <Consumer />
        </OIDCProvider>
      </MemoryRouter>,
    );
    expect(screen.getByTestId("tenant")).toHaveTextContent("stored-tenant");
  });
});
