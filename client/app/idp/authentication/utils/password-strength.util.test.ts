import { describe, expect, it } from "vitest";
import { STRENGTH_COLORS, STRENGTH_THRESHOLDS, getStrengthColor } from "./password-strength.util";

describe("getStrengthColor", () => {
  it("maps each strength range to its display color", () => {
    expect(getStrengthColor(0)).toBe(STRENGTH_COLORS.WEAK);
    expect(getStrengthColor(STRENGTH_THRESHOLDS.WEAK)).toBe(STRENGTH_COLORS.WEAK);
    expect(getStrengthColor(26)).toBe(STRENGTH_COLORS.MEDIUM_WEAK);
    expect(getStrengthColor(51)).toBe(STRENGTH_COLORS.MEDIUM_STRONG);
    expect(getStrengthColor(100)).toBe(STRENGTH_COLORS.STRONG);
  });
});
