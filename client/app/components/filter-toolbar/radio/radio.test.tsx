import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Radio } from "./radio";

const options = [
  { label: "Published", value: "pub" },
  { label: "Draft", value: "draft" },
];

describe("Radio filter", () => {
  it("shows the selected option as a badge on the trigger", () => {
    render(<Radio label="Status" options={options} value="pub" onChange={vi.fn()} />);
    expect(screen.getAllByText("Published").length).toBeGreaterThan(0);
  });

  it("opens the popover and selects an option", () => {
    const onChange = vi.fn();
    render(<Radio label="Status" options={options} value="" onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: /Status/ }));
    fireEvent.click(screen.getByText("Draft"));
    expect(onChange).toHaveBeenCalledWith("draft");
  });

  it("filters options by the search term", () => {
    render(<Radio label="Status" options={options} value="" onChange={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Status/ }));
    fireEvent.change(screen.getByPlaceholderText("Status"), { target: { value: "pub" } });
    expect(screen.getByText("Published")).toBeInTheDocument();
    expect(screen.queryByText("Draft")).not.toBeInTheDocument();
  });

  it("shows a no-results message when nothing matches", () => {
    render(<Radio label="Status" options={options} value="" onChange={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Status/ }));
    fireEvent.change(screen.getByPlaceholderText("Status"), { target: { value: "zzz" } });
    expect(screen.getByText("No results found.")).toBeInTheDocument();
  });

  it("clears the selection through the clear button", () => {
    const onChange = vi.fn();
    render(<Radio label="Status" options={options} value="pub" onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: /Status/ }));
    fireEvent.click(screen.getByText(/Clear/i));
    expect(onChange).toHaveBeenCalledWith(null);
  });
});
