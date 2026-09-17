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

    // A single character no longer satisfies the fallback rule (8-30, every character class).
    const oneCharacter = renderHook(() => usePasswordStrength("a", null));
    expect(oneCharacter.result.current.allRequirementsMet).toBe(false);

    const strongDefault = renderHook(() => usePasswordStrength("Sunflower7!", null));
    expect(strongDefault.result.current.allRequirementsMet).toBe(true);
  });

  it("C1: shows no requirements for a policy that states none, without throwing", () => {
    // Bounds that say nothing and no server check: there is simply no row to draw. Showing the FE
    // baseline here would invent requirements the server does not enforce.
    const statesNothing: IOidcPasswordPolicy = {
      ...policy,
      minLength: 0,
      maxLength: 0,
      requireUppercase: false,
      requireLowercase: false,
      requireNumbers: false,
      requireSpecialChars: false,
    };
    expect(() => renderHook(() => usePasswordStrength("anything", statesNothing))).not.toThrow();
    const { result } = renderHook(() => usePasswordStrength("anything", statesNothing));
    expect(result.current.requirements).toEqual([]);
    expect(result.current.hasPolicy).toBe(false);
  });

  it("states only what the policy describes, for a rule the server also checks", () => {
    // No extra row and no request: the server reports the rest through its own submit error.
    const serverChecked: IOidcPasswordPolicy = { ...policy, requiresServerCheck: true };
    const { result } = renderHook(() => usePasswordStrength("Sunflower7!", serverChecked));

    expect(result.current.requirements.map((r) => r.key)).not.toContain("custom");
  });
});
