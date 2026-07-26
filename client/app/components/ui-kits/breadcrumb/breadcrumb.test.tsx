import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
  Breadcrumb,
  BreadcrumbList,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbPage,
  BreadcrumbSeparator,
  BreadcrumbEllipsis,
} from "./breadcrumb";

describe("Breadcrumb", () => {
  it("renders a full breadcrumb trail with links, a page and separators", () => {
    render(
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink href="/home">Home</BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbEllipsis />
          </BreadcrumbItem>
          <BreadcrumbSeparator>/</BreadcrumbSeparator>
          <BreadcrumbItem>
            <BreadcrumbPage>Current</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>,
    );

    expect(screen.getByRole("navigation", { name: "breadcrumb" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Home" })).toHaveAttribute("href", "/home");
    expect(screen.getByText("Current")).toHaveAttribute("aria-current", "page");
    expect(screen.getByText("More")).toBeInTheDocument();
    // Custom separator content wins over the default chevron.
    expect(screen.getByText("/")).toBeInTheDocument();
  });

  it("renders the link as a child element when asChild is set", () => {
    render(
      <BreadcrumbLink asChild>
        <button type="button">as-child</button>
      </BreadcrumbLink>,
    );
    expect(screen.getByRole("button", { name: "as-child" })).toBeInTheDocument();
  });
});
