import { describe, expect, it } from "vitest";
import {
  DEFAULT_POLICY_MESSAGE,
  PASSWORD_MAX_INPUT_LENGTH,
  compilePasswordPolicy,
} from "./password-policy.util";

describe("compilePasswordPolicy", () => {
  it.each([null, undefined])("returns null for a nullish policy", (policy) => {
    expect(compilePasswordPolicy(policy)).toBeNull();
  });

  it.each(["", "   "])("returns null for a blank pattern", (regex) => {
    expect(compilePasswordPolicy({ regex, message: "Ignored", ignoreCase: true })).toBeNull();
  });

  it("catches patterns JavaScript cannot compile", () => {
    expect(
      compilePasswordPolicy({ regex: "^(?<-a>x)$", message: "Ignored", ignoreCase: true }),
    ).toBeNull();
  });

  it("preserves the pattern and ignore-case behavior", () => {
    const policy = compilePasswordPolicy({ regex: "^abc$", message: "Use abc", ignoreCase: true });
    expect(policy?.test("ABC")).toBe(true);
    expect(policy?.message).toBe("Use abc");
  });

  it("compiles case-sensitively when requested and supplies the fallback message", () => {
    const policy = compilePasswordPolicy({ regex: "^abc$", message: null, ignoreCase: false });
    expect(policy?.test("ABC")).toBe(false);
    expect(policy?.message).toBe(DEFAULT_POLICY_MESSAGE);
  });

  it("sets the input cap to 256 characters", () => {
    expect(PASSWORD_MAX_INPUT_LENGTH).toBe(256);
  });
});
