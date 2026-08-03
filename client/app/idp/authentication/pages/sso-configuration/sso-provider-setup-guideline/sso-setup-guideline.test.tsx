import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { SSOSetupGuideLine } from "./sso-setup-guideline";

const steps = [
  { id: "1", description: "Setup one" },
  { id: "2", description: "Setup two" },
];

describe("SSOSetupGuideLine", () => {
  it("shows the first step with previous disabled", () => {
    render(<SSOSetupGuideLine steps={steps} />);
    expect(screen.getByText("Setup one")).toBeInTheDocument();
    const [prev] = screen.getAllByRole("button");
    expect(prev).toBeDisabled();
  });

  it("navigates forward then back through the steps", () => {
    render(<SSOSetupGuideLine steps={steps} />);
    const [prev, next] = screen.getAllByRole("button");
    fireEvent.click(next);
    expect(screen.getByText("Setup two")).toBeInTheDocument();
    expect(next).toBeDisabled();
    fireEvent.click(prev);
    expect(screen.getByText("Setup one")).toBeInTheDocument();
  });
});
