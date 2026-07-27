import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { SearchInput } from "./search-input";

describe("SearchInput", () => {
  it("renders an input with the current value and reports changes", () => {
    const onSearch = vi.fn();
    render(
      <SearchInput value="hello" onSearch={onSearch} isVisible setIsVisible={vi.fn()} />,
    );
    const input = screen.getByPlaceholderText("Search...") as HTMLInputElement;
    expect(input.value).toBe("hello");
    fireEvent.change(input, { target: { value: "world" } });
    expect(onSearch).toHaveBeenCalledWith("world");
  });

  it("clears the value and refocuses when not toggleable", () => {
    const onSearch = vi.fn();
    render(<SearchInput value="text" onSearch={onSearch} isVisible setIsVisible={vi.fn()} />);
    fireEvent.click(screen.getByRole("button"));
    expect(onSearch).toHaveBeenCalledWith("");
  });

  it("shows a toggle button when toggleable and hidden, and reveals the input on click", () => {
    const setIsVisible = vi.fn();
    render(
      <SearchInput
        value=""
        onSearch={vi.fn()}
        toggleable
        isVisible={false}
        setIsVisible={setIsVisible}
      />,
    );
    // Collapsed: only the search toggle button, no input.
    expect(screen.queryByPlaceholderText("Search...")).toBeNull();
    fireEvent.click(screen.getByRole("button"));
    expect(setIsVisible).toHaveBeenCalledWith(true);
  });

  it("hides the input after clearing when toggleable", () => {
    const setIsVisible = vi.fn();
    const onSearch = vi.fn();
    render(
      <SearchInput
        value="text"
        onSearch={onSearch}
        toggleable
        isVisible
        setIsVisible={setIsVisible}
      />,
    );
    // The clear (X) button is the only button while the input has a value.
    fireEvent.click(screen.getByRole("button"));
    expect(onSearch).toHaveBeenCalledWith("");
    expect(setIsVisible).toHaveBeenCalledWith(false);
  });

  it("uses a custom placeholder", () => {
    render(
      <SearchInput
        value=""
        onSearch={vi.fn()}
        placeholder="Find users"
        isVisible
        setIsVisible={vi.fn()}
      />,
    );
    expect(screen.getByPlaceholderText("Find users")).toBeInTheDocument();
  });
});
