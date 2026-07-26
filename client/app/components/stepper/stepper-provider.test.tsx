import { render, screen, fireEvent, renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import StepperProvider, { useStepper } from "./stepper-provider";
import type { Steps } from "./stepper-models";

const steps: Steps = [
  { id: 1, title: "One" },
  { id: 2, title: "Two" },
  { id: 3, title: "Three" },
];

// A small consumer that surfaces the stepper state and controls.
function Consumer() {
  const { currentStep, completedSteps, totalSteps, nextStep, previousStep, goToStep, getSteps } =
    useStepper();
  return (
    <div>
      <span data-testid="current">{currentStep}</span>
      <span data-testid="completed">{completedSteps.join(",")}</span>
      <span data-testid="total">{totalSteps}</span>
      <span data-testid="titles">{getSteps().map((s) => s.title).join("|")}</span>
      <button type="button" onClick={nextStep}>next</button>
      <button type="button" onClick={previousStep}>prev</button>
      <button type="button" onClick={() => goToStep(3)}>goto3</button>
      <button type="button" onClick={() => goToStep(2)}>goto2</button>
    </div>
  );
}

const renderWith = (props: Partial<React.ComponentProps<typeof StepperProvider>> = {}) =>
  render(
    <StepperProvider steps={steps} {...props}>
      <Consumer />
    </StepperProvider>,
  );

describe("StepperProvider", () => {
  it("throws when useStepper is used outside a provider", () => {
    expect(() => renderHook(() => useStepper())).toThrow(
      /must be used within a StepperProvider/,
    );
  });

  it("starts at step 1 with no completed steps by default", () => {
    renderWith();
    expect(screen.getByTestId("current")).toHaveTextContent("1");
    expect(screen.getByTestId("completed")).toHaveTextContent("");
    expect(screen.getByTestId("total")).toHaveTextContent("3");
    expect(screen.getByTestId("titles")).toHaveTextContent("One|Two|Three");
  });

  it("honours an initialStep by precompleting the earlier steps", () => {
    renderWith({ initialStep: 2 });
    expect(screen.getByTestId("current")).toHaveTextContent("2");
    expect(screen.getByTestId("completed")).toHaveTextContent("1");
  });

  it("advances with nextStep and marks the visited step complete", () => {
    renderWith();
    fireEvent.click(screen.getByText("next"));
    expect(screen.getByTestId("current")).toHaveTextContent("2");
    expect(screen.getByTestId("completed")).toHaveTextContent("1");
  });

  it("does not advance past the last step", () => {
    renderWith({ initialStep: 3 });
    fireEvent.click(screen.getByText("next"));
    expect(screen.getByTestId("current")).toHaveTextContent("3");
  });

  it("goes back with previousStep and un-completes the prior step", () => {
    renderWith({ initialStep: 3 });
    fireEvent.click(screen.getByText("prev"));
    expect(screen.getByTestId("current")).toHaveTextContent("2");
    expect(screen.getByTestId("completed")).toHaveTextContent("1");
  });

  it("does not go before the first step", () => {
    renderWith();
    fireEvent.click(screen.getByText("prev"));
    expect(screen.getByTestId("current")).toHaveTextContent("1");
  });

  it("blocks goToStep when the previous step is not completed", () => {
    renderWith();
    // Jumping straight to step 3 is blocked (step 2 not completed).
    fireEvent.click(screen.getByText("goto3"));
    expect(screen.getByTestId("current")).toHaveTextContent("1");
  });

  it("allows goToStep once the previous step is completed", () => {
    renderWith();
    // Complete step 1 by advancing, go back, then jump to step 2.
    fireEvent.click(screen.getByText("next"));
    fireEvent.click(screen.getByText("goto2"));
    expect(screen.getByTestId("current")).toHaveTextContent("2");
  });

  it("respects the isStepValid guard", () => {
    renderWith({ initialStep: 2, isStepValid: () => false });
    // Even with step 1 completed, an invalid guard blocks navigation to step 2.
    fireEvent.click(screen.getByText("prev"));
    fireEvent.click(screen.getByText("goto2"));
    expect(screen.getByTestId("current")).toHaveTextContent("1");
  });
});
