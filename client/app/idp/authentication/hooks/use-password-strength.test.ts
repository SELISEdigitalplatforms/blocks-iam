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

  it("C1: shows no requirements for an undescribable rule, without throwing", () => {
    // The server published a policy that describes nothing it can state. Showing the FE baseline
    // here would invent requirements the server does not enforce, so nothing is shown and the
    // server's own error reports the failure on submit.
    const undescribable: IOidcPasswordPolicy = {
      ...policy, minLength: 0, maxLength: 0, hasUndescribedRules: true,
    };
    expect(() => renderHook(() => usePasswordStrength("anything", undescribable))).not.toThrow();
    const { result } = renderHook(() => usePasswordStrength("anything", undescribable));
    expect(result.current.requirements).toEqual([]);
    expect(result.current.hasPolicy).toBe(false);
  });
});
