import { render, screen, fireEvent, act } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DropdownSearchInput } from "./dropdown-search-input";

const options = [
  { label: "Name", value: "name" },
  { label: "Email", value: "email" },
];

beforeEach(() => {
  vi.useFakeTimers();
});
afterEach(() => {
  vi.runOnlyPendingTimers();
  vi.useRealTimers();
});

describe("DropdownSearchInput", () => {
  it("renders an optional label", () => {
    render(
      <DropdownSearchInput
        label="Search by"
        value={{ selected: "name", value: "" }}
        options={options}
        onChange={vi.fn()}
      />,
    );
    expect(screen.getByText("Search by")).toBeInTheDocument();
  });

  it("debounces text input changes before calling onChange", () => {
    const onChange = vi.fn();
    render(
      <DropdownSearchInput
        value={{ selected: "name", value: "" }}
        options={options}
        onChange={onChange}
      />,
    );
    fireEvent.change(screen.getByPlaceholderText("Search..."), { target: { value: "abc" } });
    expect(onChange).not.toHaveBeenCalled();
    act(() => {
      vi.advanceTimersByTime(300);
    });
    expect(onChange).toHaveBeenCalledWith({ selected: "name", value: "abc" });
  });

  it("clears the text value immediately", () => {
    const onChange = vi.fn();
    render(
      <DropdownSearchInput
        value={{ selected: "name", value: "abc" }}
        options={options}
        onChange={onChange}
      />,
    );
    fireEvent.click(screen.getByRole("button"));
    expect(onChange).toHaveBeenCalledWith({ selected: "name", value: "" });
  });
});
