import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { usePasswordStrength } from "./use-password-strength";
import { FALLBACK_REQUIREMENT_LABEL, compilePasswordPolicy } from "../utils/password-policy.util";
import { STRENGTH_COLORS } from "../utils/password-strength.util";

const compile = (regex: string, message: string | null = null) =>
  compilePasswordPolicy({ regex, message, ignoreCase: false })!;

const policy = compile(
  "^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d).{10,64}$",
  "At least 10 characters including one number.",
);

describe("usePasswordStrength", () => {
  it("lists one requirement per rule the policy pattern carries", () => {
    const { result } = renderHook(() => usePasswordStrength("Sunflower7", policy));
    expect(result.current.requirements.map((requirement) => requirement.label)).toEqual([
      "One lowercase letter",
      "One uppercase letter",
      "One number",
      "Between 10 and 64 characters",
    ]);
    expect(result.current.allRequirementsMet).toBe(true);
    expect(result.current.strength).toBe(100);
    expect(result.current.getStrengthLabel()).toBe("Strong");
    expect(result.current.getStrengthColor()).toBe(STRENGTH_COLORS.STRONG);
  });

  it("scores a partially compliant password between the bands", () => {
    const { result } = renderHook(() => usePasswordStrength("sunflower", policy));
    expect(result.current.checks).toEqual({
      "rule-0": true,
      "rule-1": false,
      "rule-2": false,
      length: false,
    });
    expect(result.current.strength).toBe(25);
    expect(result.current.getStrengthLabel()).toBe("Weak");
    expect(result.current.allRequirementsMet).toBe(false);
  });

  it("holds back the top band until the whole pattern passes", () => {
    const { result } = renderHook(() => usePasswordStrength("Sunflowers", policy));
    expect(result.current.strength).toBe(75);
    expect(result.current.getStrengthLabel()).toBe("Good");
    expect(result.current.getStrengthColor()).toBe(STRENGTH_COLORS.MEDIUM_STRONG);
  });

  it("scores an empty password at zero even when a rule would accept it", () => {
    const { result } = renderHook(() => usePasswordStrength("", compile("^(?=.*\\d)?.{0,64}$")));
    expect(result.current.strength).toBe(0);
  });

  it("falls back to a positive generic label when the pattern cannot be split", () => {
    const opaque = compile("^\\w+@\\w+$", "Use an email address");
    const { result } = renderHook(() => usePasswordStrength("grace@navy", opaque));
    // Not "Use an email address" — that's the failure message, and this row's icon toggles
    // with the password, so it must read correctly next to a ✓ as well as a ✗.
    expect(result.current.requirements).toEqual([
      { key: "policy", label: FALLBACK_REQUIREMENT_LABEL },
    ]);
    expect(result.current.strength).toBe(100);
    expect(result.current.unexplainedFailure).toBe(false);
  });

  it("has no complexity requirements without a policy and only requires non-empty input", () => {
    const empty = renderHook(() => usePasswordStrength("", null));
    expect(empty.result.current.requirements).toEqual([]);
    expect(empty.result.current.checks).toEqual({});
    expect(empty.result.current.allRequirementsMet).toBe(false);
    expect(empty.result.current.hasPolicy).toBe(false);

    const oneCharacter = renderHook(() => usePasswordStrength("a", null));
    expect(oneCharacter.result.current.allRequirementsMet).toBe(true);
  });

  it("surfaces the policy message when every listed rule passes but the pattern rejects", () => {
    const mismatched = {
      test: () => false,
      message: "Blocked by the organisation",
      criteria: [{ key: "rule-0", label: "One number", test: () => true }],
    };
    const { result } = renderHook(() => usePasswordStrength("Sunflower7", mismatched));
    expect(result.current.unexplainedFailure).toBe(true);
    expect(result.current.policyMessage).toBe("Blocked by the organisation");
  });
});
