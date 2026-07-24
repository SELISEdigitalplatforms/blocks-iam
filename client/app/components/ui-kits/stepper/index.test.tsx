import { render, screen } from "@testing-library/react";
import { Home } from "lucide-react";
import { describe, expect, it, vi } from "vitest";
import { Stepper, Step } from "./index";
import type { StepItem } from "./types";

const steps: StepItem[] = [
  { label: "One" },
  { label: "Two" },
  { label: "Three" },
];

describe("Stepper", () => {
  it("renders horizontal steps with labels and only the active step content", () => {
    render(
      <Stepper initialStep={0} steps={steps} orientation="horizontal">
        {steps.map((s) => (
          <Step key={s.label} label={s.label}>
            <div>content-{s.label}</div>
          </Step>
        ))}
      </Stepper>,
    );

    expect(screen.getByText("One")).toBeInTheDocument();
    expect(screen.getByText("Two")).toBeInTheDocument();
    expect(screen.getByText("Three")).toBeInTheDocument();
    // Horizontal content shows only the active step (index 0) children.
    expect(screen.getByText("content-One")).toBeInTheDocument();
    expect(screen.queryByText("content-Two")).not.toBeInTheDocument();
  });

  it("renders vertical steps and expands all content when expandVerticalSteps is set", () => {
    render(
      <Stepper
        initialStep={1}
        steps={steps}
        orientation="vertical"
        expandVerticalSteps
      >
        {steps.map((s) => (
          <Step key={s.label} label={s.label} description={`desc-${s.label}`}>
            <div>vcontent-{s.label}</div>
          </Step>
        ))}
      </Stepper>,
    );

    expect(screen.getByText("vcontent-One")).toBeInTheDocument();
    expect(screen.getByText("vcontent-Two")).toBeInTheDocument();
    expect(screen.getByText("desc-Three")).toBeInTheDocument();
  });

  it("renders a footer child that is not a Step", () => {
    render(
      <Stepper initialStep={0} steps={steps} orientation="horizontal">
        {steps.map((s) => (
          <Step key={s.label} label={s.label} />
        ))}
        <div data-testid="footer">footer</div>
      </Stepper>,
    );
    expect(screen.getByTestId("footer")).toBeInTheDocument();
  });

  it("marks steps clickable and invokes onClickStep", () => {
    const onClickStep = vi.fn();
    render(
      <Stepper
        initialStep={0}
        steps={steps}
        orientation="vertical"
        onClickStep={onClickStep}
        expandVerticalSteps
      >
        {steps.map((s) => (
          <Step key={s.label} label={s.label} icon={Home} />
        ))}
      </Stepper>,
    );
    screen.getByText("One").click();
    expect(onClickStep).toHaveBeenCalled();
  });

  it("throws when a child is not a valid element", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() =>
      render(
        <Stepper initialStep={0} steps={steps}>
          {"not-an-element"}
        </Stepper>,
      ),
    ).toThrow(/valid React elements/);
    spy.mockRestore();
  });
});
