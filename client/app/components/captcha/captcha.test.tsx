import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("./reCaptcha", () => ({
  ReCaptcha: () => <div data-testid="recaptcha" />,
}));
vi.mock("./hCaptcha", () => ({
  HCaptcha: () => <div data-testid="hcaptcha" />,
}));

import { Captcha } from "./captcha";

describe("Captcha", () => {
  it("renders the reCaptcha implementation for the checkbox type", () => {
    render(<Captcha type="reCaptcha-v2-checkbox" />);
    expect(screen.getByTestId("recaptcha")).toBeInTheDocument();
  });

  it("renders the hCaptcha implementation for the hCaptcha type", () => {
    render(<Captcha type="hCaptcha" />);
    expect(screen.getByTestId("hcaptcha")).toBeInTheDocument();
  });

  it("throws when no captcha type is provided", () => {
    // @ts-expect-error intentionally omitting the required type
    expect(() => render(<Captcha />)).toThrow("Captcha type is not passed");
  });

  it("throws for an unsupported captcha type", () => {
    // @ts-expect-error intentionally using an unsupported type
    expect(() => render(<Captcha type="unknown" />)).toThrow("Captcha type is not supported");
  });
});
