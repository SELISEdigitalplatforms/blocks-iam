import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  checks: {} as Record<string, boolean>,
  requirements: [] as { key: string; label: string }[],
}));

vi.mock("@blocks-idp/authentication/hooks/use-password-strength", () => ({
  usePasswordStrength: () => ({ checks: h.checks, requirements: h.requirements }),
}));

import { PasswordStrengthChecker } from "./password-strength-checker";

beforeEach(() => {
  h.requirements = [
    { key: "length", label: "At least 8 characters" },
    { key: "number", label: "Contains a number" },
  ];
  h.checks = { length: true, number: true };
});

describe("PasswordStrengthChecker", () => {
  it("renders every requirement label", () => {
    render(
      <PasswordStrengthChecker password="abc" confirmPassword="abc" onRequirementsMet={vi.fn()} />,
    );
    expect(screen.getByText("At least 8 characters")).toBeInTheDocument();
    expect(screen.getByText("Contains a number")).toBeInTheDocument();
    expect(screen.getByText("Passwords match")).toBeInTheDocument();
  });

  it("reports requirements met when all checks pass and passwords match", () => {
    const onMet = vi.fn();
    render(
      <PasswordStrengthChecker password="Secret1" confirmPassword="Secret1" onRequirementsMet={onMet} />,
    );
    expect(onMet).toHaveBeenLastCalledWith(true);
  });

  it("reports not met when passwords do not match", () => {
    const onMet = vi.fn();
    render(
      <PasswordStrengthChecker password="Secret1" confirmPassword="other" onRequirementsMet={onMet} />,
    );
    expect(onMet).toHaveBeenLastCalledWith(false);
  });

  it("shows the exclude-password requirement and fails when it matches", () => {
    const onMet = vi.fn();
    render(
      <PasswordStrengthChecker
        password="Secret1"
        confirmPassword="Secret1"
        onRequirementsMet={onMet}
        excludePassword="Secret1"
        excludePasswordLabel="Must differ from current"
      />,
    );
    expect(screen.getByText("Must differ from current")).toBeInTheDocument();
    expect(onMet).toHaveBeenLastCalledWith(false);
  });

  it("renders a low-strength bar when few checks pass", () => {
    h.checks = { length: false, number: false };
    const { container } = render(
      <PasswordStrengthChecker password="" confirmPassword="" onRequirementsMet={vi.fn()} />,
    );
    expect(container.querySelector(".bg-red-500")).not.toBeNull();
  });
});
