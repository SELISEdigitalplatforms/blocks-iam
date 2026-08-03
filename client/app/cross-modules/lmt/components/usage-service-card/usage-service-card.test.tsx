import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import { UsageServiceCard } from "./usage-service-card";
import type { UsageMatrixSummary } from "../../models/usage.model";

const matrix = (overrides: Partial<UsageMatrixSummary>): UsageMatrixSummary =>
  ({
    TotalRequests: 100,
    totalSuccess: 90,
    successRate: 90,
    totalFailure: 10,
    failureRate: 10,
    ...overrides,
  }) as UsageMatrixSummary;

const metrics = {
  api: matrix({ TotalRequests: 100 }),
  worker: matrix({ TotalRequests: 42 }),
};

const renderCard = (props = {}) =>
  render(
    <MemoryRouter>
      <UsageServiceCard name="Auth" metrics={metrics} isLoading={false} {...props} />
    </MemoryRouter>,
  );

describe("UsageServiceCard", () => {
  it("renders a skeleton with the service name while loading", () => {
    const { container } = render(
      <MemoryRouter>
        <UsageServiceCard name="Auth" metrics={metrics} isLoading={true} />
      </MemoryRouter>,
    );
    expect(screen.getByText("Auth")).toBeInTheDocument();
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("renders API metrics by default and switches to worker metrics", () => {
    renderCard();
    expect(screen.getByText("Auth")).toBeInTheDocument();
    // API metrics show the api total requests (abbreviated).
    expect(screen.getByText("100")).toBeInTheDocument();
    fireEvent.click(screen.getByTitle("Worker metrics"));
    expect(screen.getByText("42")).toBeInTheDocument();
  });

  it("renders a logs link when a logLink is provided", () => {
    renderCard({ logLink: "/logs/auth" });
    expect(screen.getByRole("link", { name: /Logs/ })).toHaveAttribute("href", "/logs/auth");
  });
});
