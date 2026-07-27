import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useState } from "react";
import {
  ChipsInput,
  ChipsInputField,
  ChipsInputList,
  useChipsContext,
} from "./chips-input";

const Harness = ({
  initial = [] as string[],
  ...rest
}: {
  initial?: string[];
  validatorRegex?: RegExp;
  customValidator?: (v: string) => boolean;
  validatorRegexErrorMessage?: string;
}) => {
  const [value, setValue] = useState<string[]>(initial);
  return (
    <ChipsInput value={value} onChange={setValue} {...rest}>
      <ChipsInputList />
      <ChipsInputField />
    </ChipsInput>
  );
};

describe("ChipsInput", () => {
  it("renders the initial chips", () => {
    render(<Harness initial={["alpha", "beta"]} />);
    expect(screen.getByText("alpha")).toBeInTheDocument();
    expect(screen.getByText("beta")).toBeInTheDocument();
  });

  it("adds a chip when Enter is pressed on a non-empty value", () => {
    render(<Harness />);
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "gamma" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.getByText("gamma")).toBeInTheDocument();
    expect((input as HTMLInputElement).value).toBe("");
  });

  it("removes a chip via the remove control", () => {
    render(<Harness initial={["removeme"]} />);
    fireEvent.click(screen.getByLabelText("Remove removeme"));
    expect(screen.queryByText("removeme")).toBeNull();
  });

  it("shows a validation error and blocks adding an invalid value", () => {
    render(
      <Harness
        validatorRegex={/^\d+$/}
        validatorRegexErrorMessage="Numbers only"
      />,
    );
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "abc" } });
    expect(screen.getByText("Numbers only")).toBeInTheDocument();
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.queryByText("abc")).toBeNull();
  });

  it("accepts a value that passes a custom validator", () => {
    const customValidator = vi.fn((v: string) => v.startsWith("ok"));
    render(<Harness customValidator={customValidator} />);
    const input = screen.getByPlaceholderText("Type and press enter");
    fireEvent.change(input, { target: { value: "ok-value" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(screen.getByText("ok-value")).toBeInTheDocument();
  });

  it("throws when the context hook is used outside the provider", () => {
    const Bad = () => {
      useChipsContext();
      return null;
    };
    expect(() => render(<Bad />)).toThrow(
      "ChipsInput components must be used within <ChipsInput>",
    );
  });
});
