import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { MultiSelect } from "./multi-select";

const options = [
  { label: "Alpha", value: "a" },
  { label: "Beta", value: "b" },
  { label: "Gamma", value: "c" },
];

describe("MultiSelect filter", () => {
  it("selects an unselected option", () => {
    const onChange = vi.fn();
    render(<MultiSelect label="Tags" options={options} value={[]} onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: /Tags/ }));
    fireEvent.click(screen.getByText("Alpha"));
    expect(onChange).toHaveBeenCalledWith(["a"]);
  });

  it("deselects an already-selected option", () => {
    const onChange = vi.fn();
    render(<MultiSelect label="Tags" options={options} value={["a"]} onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: /Tags/ }));
    // "Alpha" appears both as a trigger badge and as a list option; the option
    // (rendered last) is the interactive one.
    const alphaNodes = screen.getAllByText("Alpha");
    fireEvent.click(alphaNodes[alphaNodes.length - 1]);
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("clears all selected options", () => {
    const onChange = vi.fn();
    render(<MultiSelect label="Tags" options={options} value={["a", "b"]} onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: /Tags/ }));
    fireEvent.click(screen.getByText("Clear"));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("summarises the count when more than two options are selected", () => {
    render(<MultiSelect label="Tags" options={options} value={["a", "b", "c"]} onChange={vi.fn()} />);
    expect(screen.getByText("3 selected")).toBeInTheDocument();
  });

  it("shows individual badges when two or fewer options are selected", () => {
    render(<MultiSelect label="Tags" options={options} value={["a", "b"]} onChange={vi.fn()} />);
    expect(screen.getAllByText("Alpha").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Beta").length).toBeGreaterThan(0);
  });
});
