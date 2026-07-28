import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("@tanstack/react-query-devtools", () => ({
  ReactQueryDevtools: () => <div data-testid="devtools" />,
}));

import QueryProvider, { getQueryClient } from "./query-provider";

describe("QueryProvider", () => {
  it("renders its children inside the query client provider", () => {
    render(
      <QueryProvider>
        <div>child-content</div>
      </QueryProvider>,
    );
    expect(screen.getByText("child-content")).toBeInTheDocument();
    expect(screen.getByTestId("devtools")).toBeInTheDocument();
  });

  it("returns the same singleton query client on repeated calls", () => {
    const first = getQueryClient();
    const second = getQueryClient();
    expect(first).toBe(second);
  });
});
