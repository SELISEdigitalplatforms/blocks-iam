import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { usePasswordStrength } from "./use-password-strength";
import type { IOidcPasswordPolicy } from "../utils/password-policy.util";
import { DEFAULT_PASSWORD_POLICY, buildPasswordPolicyRequirements } from "../utils/password-policy.util";
import { STRENGTH_COLORS } from "../utils/password-strength.util";

const policy: IOidcPasswordPolicy = {
  minLength: 10,
  maxLength: 64,
  requireUppercase: true,
  requireLowercase: true,
  requireNumbers: true,
  requireSpecialChars: false,
  message: "At least 10 characters including one number.",
};

describe("usePasswordStrength", () => {
  it("lists one requirement per active policy field", () => {
    const { result } = renderHook(() => usePasswordStrength("Sunflower7", policy));
    expect(result.current.requirements.map((requirement) => requirement.label)).toEqual([
      "Between 10 and 64 characters",
      "At least one uppercase letter (A-Z)",
      "At least one lowercase letter (a-z)",
      "At least one number (0-9)",
    ]);
    expect(result.current.allRequirementsMet).toBe(true);
    expect(result.current.strength).toBe(100);
    expect(result.current.getStrengthLabel()).toBe("Strong");
    expect(result.current.getStrengthColor()).toBe(STRENGTH_COLORS.STRONG);
  });

  it("scores a partially compliant password between the bands", () => {
    const { result } = renderHook(() => usePasswordStrength("sunflower", policy));
    expect(result.current.checks).toEqual({
      length: false,
      uppercase: false,
      lowercase: true,
      number: false,
    });
    expect(result.current.strength).toBe(25);
    expect(result.current.getStrengthLabel()).toBe("Weak");
    expect(result.current.allRequirementsMet).toBe(false);
  });

  it("holds back the top band until every requirement passes", () => {
    const { result } = renderHook(() => usePasswordStrength("Sunflowers", policy));
    expect(result.current.strength).toBe(75);
    expect(result.current.getStrengthLabel()).toBe("Good");
    expect(result.current.getStrengthColor()).toBe(STRENGTH_COLORS.MEDIUM_STRONG);
  });

  it("scores an empty password at zero", () => {
    const { result } = renderHook(() => usePasswordStrength("", policy));
    expect(result.current.strength).toBe(0);
    expect(result.current.allRequirementsMet).toBe(false);
  });

  it("surfaces the admin message regardless of pass/fail state", () => {
    const { result } = renderHook(() => usePasswordStrength("nope", policy));
    expect(result.current.policyMessage).toBe("At least 10 characters including one number.");
  });

  it("falls back to the FE default policy without a tenant policy, rather than no requirements", () => {
    const empty = renderHook(() => usePasswordStrength("", null));
    expect(empty.result.current.requirements.map((r) => r.key)).toEqual([
      "length",
      "uppercase",
      "lowercase",
      "number",
      "special",
    ]);
    expect(empty.result.current.hasPolicy).toBe(true);
    expect(empty.result.current.allRequirementsMet).toBe(false);
    expect(empty.result.current.policyMessage).toBeNull();

    // A single character no longer satisfies the fallback rule (8-30, every character class).
    const oneCharacter = renderHook(() => usePasswordStrength("a", null));
    expect(oneCharacter.result.current.allRequirementsMet).toBe(false);

    const strongDefault = renderHook(() => usePasswordStrength("Sunflower7!", null));
    expect(strongDefault.result.current.allRequirementsMet).toBe(true);
  });

  it("C1: falls back to the FE default policy when the tenant policy has invalid bounds, without throwing", () => {
    const brokenPolicy: IOidcPasswordPolicy = { ...policy, minLength: 0, maxLength: -1 };
    expect(() => renderHook(() => usePasswordStrength("anything", brokenPolicy))).not.toThrow();
    const { result } = renderHook(() => usePasswordStrength("anything", brokenPolicy));
    expect(result.current.requirements).toEqual(
      buildPasswordPolicyRequirements(DEFAULT_PASSWORD_POLICY),
    );
    expect(result.current.allRequirementsMet).toBe(false); // "anything" fails the default rule
  });
});
