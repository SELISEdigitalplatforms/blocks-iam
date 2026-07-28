import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TraceGuideLine } from "./trace-guideline";

const steps = [
  { id: "1", description: "Trace one" },
  { id: "2", description: "Trace two" },
];

describe("TraceGuideLine", () => {
  it("shows the first step with previous disabled", () => {
    render(<TraceGuideLine steps={steps} />);
    expect(screen.getByText("Trace one")).toBeInTheDocument();
    const [prev] = screen.getAllByRole("button");
    expect(prev).toBeDisabled();
  });

  it("navigates to the next and back to the previous step", () => {
    render(<TraceGuideLine steps={steps} />);
    const [prev, next] = screen.getAllByRole("button");
    fireEvent.click(next);
    expect(screen.getByText("Trace two")).toBeInTheDocument();
    expect(next).toBeDisabled();
    fireEvent.click(prev);
    expect(screen.getByText("Trace one")).toBeInTheDocument();
  });
});
