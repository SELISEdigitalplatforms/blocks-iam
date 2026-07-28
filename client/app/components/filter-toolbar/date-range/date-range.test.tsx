import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DateRange } from "./date-range";

const value = { from: new Date("2020-01-01T00:00:00Z"), to: new Date("2020-01-05T00:00:00Z") };

describe("DateRange filter", () => {
  it("shows the selected range on the trigger", () => {
    render(<DateRange label="Created" value={value} onChange={vi.fn()} />);
    // The trigger shows the label and the formatted from/to dates.
    expect(screen.getByText("Created")).toBeInTheDocument();
  });

  it("applies the current range when Apply is clicked", () => {
    const onChange = vi.fn();
    render(<DateRange label="Created" value={value} onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: /Created/ }));
    fireEvent.click(screen.getByRole("button", { name: "Apply" }));
    expect(onChange).toHaveBeenCalledWith(value);
  });

  it("resets the range to null when Reset is clicked", () => {
    const onChange = vi.fn();
    render(<DateRange label="Created" value={value} onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: /Created/ }));
    fireEvent.click(screen.getByRole("button", { name: "Reset" }));
    expect(onChange).toHaveBeenCalledWith(null);
  });
});
