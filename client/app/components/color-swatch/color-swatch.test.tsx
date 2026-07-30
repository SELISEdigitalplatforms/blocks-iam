import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ColorSwatch } from "./color-swatch";

const colorInput = () => screen.getByLabelText("Pick a color") as HTMLInputElement;
const textInput = () => screen.getByPlaceholderText("#FFFFFF") as HTMLInputElement;

describe("ColorSwatch", () => {
  it("defaults to white and shows the value uppercased", () => {
    render(<ColorSwatch />);

    expect(textInput().value).toBe("#FFFFFF");
  });

  it("exposes the colour input by label so it is reachable by keyboard", () => {
    // The transparent input covers the swatch and takes the click itself, which is
    // why the swatch needs no click handler of its own.
    render(<ColorSwatch value="#123456" />);

    expect(colorInput()).toHaveAttribute("type", "color");
  });

  it("reports a colour chosen from the picker in upper case", () => {
    const onChange = vi.fn();
    render(<ColorSwatch value="#000000" onChange={onChange} />);

    fireEvent.change(colorInput(), { target: { value: "#abcdef" } });

    expect(onChange).toHaveBeenCalledWith("#ABCDEF");
  });

  it("uppercases typed input and strips characters that are not hex", () => {
    const onChange = vi.fn();
    render(<ColorSwatch value="#000000" onChange={onChange} />);

    fireEvent.change(textInput(), { target: { value: "#abz12g" } });

    expect(onChange).toHaveBeenCalledWith("#AB12");
  });

  it("collapses repeated hashes to a single leading one", () => {
    const onChange = vi.fn();
    render(<ColorSwatch value="#000000" onChange={onChange} />);

    fireEvent.change(textInput(), { target: { value: "##AB#12" } });

    expect(onChange).toHaveBeenCalledWith("#AB12");
  });

  it("marks the field when it has an error", () => {
    const { container } = render(<ColorSwatch hasError />);

    expect(container.querySelector(".border-destructive")).not.toBeNull();
  });
});
