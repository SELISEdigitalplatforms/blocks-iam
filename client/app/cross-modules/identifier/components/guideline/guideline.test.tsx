import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { GuideLine } from "./guideline";

const steps = [
  { id: "1", description: "Step one" },
  { id: "2", description: "Step two" },
  { id: "3", description: "Step three" },
];

describe("GuideLine", () => {
  it("shows the first step with the previous button disabled", () => {
    render(<GuideLine steps={steps} />);
    expect(screen.getByText("Step one")).toBeInTheDocument();
    const [prev, next] = screen.getAllByRole("button");
    expect(prev).toBeDisabled();
    expect(next).not.toBeDisabled();
  });

  it("navigates forward and backward through the steps", () => {
    render(<GuideLine steps={steps} />);
    const [prev, next] = screen.getAllByRole("button");
    fireEvent.click(next);
    expect(screen.getByText("Step two")).toBeInTheDocument();
    fireEvent.click(next);
    expect(screen.getByText("Step three")).toBeInTheDocument();
    // Next is disabled on the last step.
    expect(screen.getAllByRole("button")[1]).toBeDisabled();
    fireEvent.click(prev);
    expect(screen.getByText("Step two")).toBeInTheDocument();
  });

  it("does not go past the last step", () => {
    render(<GuideLine steps={steps} />);
    const next = screen.getAllByRole("button")[1];
    fireEvent.click(next);
    fireEvent.click(next);
    fireEvent.click(next);
    expect(screen.getByText("Step three")).toBeInTheDocument();
  });
});
