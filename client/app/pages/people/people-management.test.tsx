import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PeopleManagement } from "./people-management";

describe("PeopleManagement", () => {
  it("renders the People heading", () => {
    render(<PeopleManagement />);
    expect(screen.getByText("People")).toBeInTheDocument();
  });

  it("renders the empty state when there are no people", () => {
    render(<PeopleManagement />);
    expect(screen.getByText("No people found in this project.")).toBeInTheDocument();
  });
});
