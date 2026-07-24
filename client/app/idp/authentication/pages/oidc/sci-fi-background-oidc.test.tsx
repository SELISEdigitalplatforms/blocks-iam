import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { SciFiBackgroundOidc } from "./sci-fi-background-oidc";

// jsdom does not implement canvas getContext (returns null), so the 2-D and
// WebGL effects early-return. The render still exercises the component body,
// the effect setup up to the guard, and the corner-bracket JSX branch.
describe("SciFiBackgroundOidc", () => {
  it("renders the two background canvases", () => {
    const { container } = render(<SciFiBackgroundOidc />);
    expect(container.querySelectorAll("canvas")).toHaveLength(2);
  });

  it("renders the corner brackets by default", () => {
    const { container } = render(<SciFiBackgroundOidc />);
    expect(container.querySelector(".corner-tl")).not.toBeNull();
    expect(container.querySelector(".corner-br")).not.toBeNull();
  });

  it("omits the corner brackets when showCorners is false", () => {
    const { container } = render(<SciFiBackgroundOidc showCorners={false} />);
    expect(container.querySelector(".corner-tl")).toBeNull();
  });
});
