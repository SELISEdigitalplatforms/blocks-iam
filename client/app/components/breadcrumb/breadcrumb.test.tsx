import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  segments: [] as { href: string; key: string; label: string }[],
}));

vi.mock("@/hooks/use-path-segments", () => ({ default: () => h.segments }));
vi.mock("@/constants/breadcrumb-custom-title", () => ({
  BREADCRUMB_CUSTOM_TITLES: { "/app/users": "People" },
  BREADCRUMB_LINK_OVERRIDES: {},
}));

import PageBreadcrumb from "./breadcrumb";

const renderCrumb = (props = {}) =>
  render(
    <MemoryRouter>
      <PageBreadcrumb {...props} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.segments = [
    { href: "/app", key: "/app", label: "App" },
    { href: "/app/users", key: "/app/users", label: "Users" },
    { href: "/app/users/detail", key: "/app/users/detail", label: "Detail" },
  ];
});

describe("PageBreadcrumb", () => {
  it("renders links for parent segments and a page for the last", () => {
    const { container } = renderCrumb();
    const links = Array.from(container.querySelectorAll("a"));
    expect(links).toHaveLength(2);
    expect(links.map((a) => a.getAttribute("href"))).toEqual(["/app", "/app/users"]);
    // The custom title replaces the raw label on the /app/users crumb.
    expect(links[1]).toHaveTextContent("People");
    // Last segment renders as the current page, not a link.
    expect(screen.getByText("Detail")).toBeInTheDocument();
  });

  it("renders a loading skeleton for the last item when requested", () => {
    const { container } = renderCrumb({ isLoadingLastItem: true });
    expect(container.querySelector("[class*='animate-pulse']")).not.toBeNull();
    expect(screen.queryByText("Detail")).not.toBeInTheDocument();
  });

  it("slices the breadcrumbs when a start index is provided", () => {
    const { container } = renderCrumb({ breadcrumbIndex: 2 });
    const links = Array.from(container.querySelectorAll("a"));
    // Starting at index 1 leaves the Users link and the Detail page.
    expect(links.map((a) => a.getAttribute("href"))).toEqual(["/app/users"]);
    expect(screen.getByText("Detail")).toBeInTheDocument();
  });
});
