import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { usePasswordStrength } from "./use-password-strength";
import type { CompiledPasswordPolicy } from "../utils/password-policy.util";

const policy: CompiledPasswordPolicy = {
  test: (password) => /^(?=.*\d).{10,64}$/i.test(password),
  message: "At least 10 characters including one number.",
};

describe("usePasswordStrength", () => {
  it("uses the active policy as the only requirement", () => {
    const { result } = renderHook(() => usePasswordStrength("Sunflower7", policy));
    expect(result.current.requirements).toEqual([{ key: "policy", label: policy.message }]);
    expect(result.current.checks).toEqual({ policy: true });
    expect(result.current.allRequirementsMet).toBe(true);
    expect(result.current.strength).toBe(100);
  });

  it("fails the active policy when the password does not match", () => {
    const { result } = renderHook(() => usePasswordStrength("Sunflower", policy));
    expect(result.current.checks).toEqual({ policy: false });
    expect(result.current.allRequirementsMet).toBe(false);
    expect(result.current.strength).toBe(0);
  });

  it("has no complexity requirements without a policy and only requires non-empty input", () => {
    const empty = renderHook(() => usePasswordStrength("", null));
    expect(empty.result.current.requirements).toEqual([]);
    expect(empty.result.current.checks).toEqual({});
    expect(empty.result.current.allRequirementsMet).toBe(false);

    const oneCharacter = renderHook(() => usePasswordStrength("a", null));
    expect(oneCharacter.result.current.allRequirementsMet).toBe(true);
  });
});
