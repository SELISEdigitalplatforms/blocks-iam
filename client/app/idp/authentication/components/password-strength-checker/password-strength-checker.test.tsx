import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  getStrengthColor,
  getStrengthLabel,
  getStrengthTextColor,
} from "@blocks-idp/authentication/utils/password-strength.util";

const h = vi.hoisted(() => ({
  checks: {} as Record<string, boolean>,
  requirements: [] as { key: string; label: string }[],
}));
const policy = {
  minLength: 8,
  maxLength: 64,
  requireUppercase: false,
  requireLowercase: false,
  requireNumbers: true,
  requireSpecialChars: false,
};

vi.mock("@blocks-idp/authentication/hooks/use-password-strength", async () => {
  const strengthUtil = await import(
    "@blocks-idp/authentication/utils/password-strength.util"
  );
  return {
    usePasswordStrength: () => {
      const total = h.requirements.length;
      const met = h.requirements.filter((requirement) => h.checks[requirement.key]).length;
      const strength = total === 0 ? 0 : Math.round((met / total) * 100);
      return {
        checks: h.checks,
        requirements: h.requirements,
        allRequirementsMet: Object.values(h.checks).every(Boolean),
        strength,
        hasPolicy: total > 0,
        getStrengthColor: () => strengthUtil.getStrengthColor(strength),
        getStrengthTextColor: () => strengthUtil.getStrengthTextColor(strength),
        getStrengthLabel: () => strengthUtil.getStrengthLabel(strength),
      };
    },
  };
});

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
      <PasswordStrengthChecker password="abc" confirmPassword="abc" policy={policy} onRequirementsMet={vi.fn()} />,
    );
    expect(screen.getByText("At least 8 characters")).toBeInTheDocument();
    expect(screen.getByText("Contains a number")).toBeInTheDocument();
    expect(screen.getByText("Passwords match")).toBeInTheDocument();
  });

  it("reports requirements met when all checks pass and passwords match", () => {
    const onMet = vi.fn();
    render(
      <PasswordStrengthChecker password="Secret1" confirmPassword="Secret1" policy={policy} onRequirementsMet={onMet} />,
    );
    expect(onMet).toHaveBeenLastCalledWith(true);
  });

  it("reports not met when passwords do not match", () => {
    const onMet = vi.fn();
    render(
      <PasswordStrengthChecker password="Secret1" confirmPassword="other" policy={policy} onRequirementsMet={onMet} />,
    );
    expect(onMet).toHaveBeenLastCalledWith(false);
  });

  it("shows the exclude-password requirement and fails when it matches", () => {
    const onMet = vi.fn();
    render(
      <PasswordStrengthChecker
        password="Secret1"
        confirmPassword="Secret1"
        policy={policy}
        onRequirementsMet={onMet}
        excludePassword="Secret1"
        excludePasswordLabel="Must differ from current"
      />,
    );
    expect(screen.getByText("Must differ from current")).toBeInTheDocument();
    expect(onMet).toHaveBeenLastCalledWith(false);
  });

  it("fills every segment and calls the password strong when all rules pass", () => {
    const { container } = render(
      <PasswordStrengthChecker password="Secret1" confirmPassword="Secret1" policy={policy} onRequirementsMet={vi.fn()} />,
    );
    expect(container.querySelectorAll(`.${getStrengthColor(100)}`)).toHaveLength(4);
    expect(screen.getByText(getStrengthLabel(100))).toHaveClass(getStrengthTextColor(100));
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "100");
  });

  it("renders a weak, partly filled bar when few checks pass", () => {
    h.checks = { length: false, number: false };
    const { container } = render(
      <PasswordStrengthChecker password="a" confirmPassword="a" policy={policy} onRequirementsMet={vi.fn()} />,
    );
    expect(container.querySelector(`.${getStrengthColor(0)}`)).toBeNull();
    expect(container.querySelectorAll(".bg-neutral-200")).toHaveLength(4);
    expect(screen.getByText(getStrengthLabel(0))).toBeInTheDocument();
  });

  it("hides the strength meter and shows only match and exclusion rows without a policy (H5)", () => {
    h.requirements = [];
    h.checks = {};
    render(
      <PasswordStrengthChecker
        password="new"
        confirmPassword="new"
        excludePassword="old"
        policy={null}
        onRequirementsMet={vi.fn()}
      />,
    );
    expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
    expect(screen.getByText("Passwords match")).toBeInTheDocument();
    expect(screen.getByText("New password shouldn't match current password")).toBeInTheDocument();
    expect(screen.queryByText("At least 8 characters")).not.toBeInTheDocument();
  });

});
