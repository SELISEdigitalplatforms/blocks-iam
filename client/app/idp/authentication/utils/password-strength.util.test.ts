import { describe, expect, it } from "vitest";
import {
  STRENGTH_COLORS,
  STRENGTH_LABELS,
  STRENGTH_SEGMENTS,
  STRENGTH_TEXT_COLORS,
  STRENGTH_THRESHOLDS,
  getFilledSegments,
  scorePasswordStrength,
  getStrengthColor,
  getStrengthLabel,
  getStrengthTextColor,
} from "./password-strength.util";

describe("getStrengthColor", () => {
  it("maps each strength range to its display color", () => {
    expect(getStrengthColor(0)).toBe(STRENGTH_COLORS.WEAK);
    expect(getStrengthColor(STRENGTH_THRESHOLDS.WEAK)).toBe(STRENGTH_COLORS.WEAK);
    expect(getStrengthColor(26)).toBe(STRENGTH_COLORS.MEDIUM_WEAK);
    expect(getStrengthColor(51)).toBe(STRENGTH_COLORS.MEDIUM_STRONG);
    expect(getStrengthColor(100)).toBe(STRENGTH_COLORS.STRONG);
  });
});

describe("getStrengthLabel", () => {
  it("names each band", () => {
    expect(getStrengthLabel(0)).toBe(STRENGTH_LABELS.WEAK);
    expect(getStrengthLabel(50)).toBe(STRENGTH_LABELS.MEDIUM_WEAK);
    expect(getStrengthLabel(75)).toBe(STRENGTH_LABELS.MEDIUM_STRONG);
    expect(getStrengthLabel(100)).toBe(STRENGTH_LABELS.STRONG);
  });

  it("pairs the label colour with the bar colour band", () => {
    expect(getStrengthTextColor(0)).toBe(STRENGTH_TEXT_COLORS.WEAK);
    expect(getStrengthTextColor(100)).toBe(STRENGTH_TEXT_COLORS.STRONG);
  });
});

describe("getFilledSegments", () => {
  it("lights no segment at zero and every segment at full strength", () => {
    expect(getFilledSegments(0)).toBe(0);
    expect(getFilledSegments(100)).toBe(STRENGTH_SEGMENTS);
  });

  it("always lights at least one segment for a non-zero score", () => {
    expect(getFilledSegments(1)).toBe(1);
    expect(getFilledSegments(25)).toBe(1);
    expect(getFilledSegments(50)).toBe(2);
    expect(getFilledSegments(75)).toBe(3);
  });
});

describe("scorePasswordStrength", () => {
  it("scores nothing for an empty password", () => {
    expect(scorePasswordStrength("")).toBe(0);
  });

  it("gives anything typed at least one segment", () => {
    expect(scorePasswordStrength("a")).toBeGreaterThan(0);
  });

  it("rises with length", () => {
    expect(scorePasswordStrength("Aa1!Aa1!")).toBeLessThan(scorePasswordStrength("Aa1!Aa1!Aa1!"));
    expect(scorePasswordStrength("Aa1!Aa1!Aa1!")).toBeLessThan(scorePasswordStrength("Aa1!Aa1!Aa1!Aa1!"));
  });

  it("rises with character variety at the same length", () => {
    expect(scorePasswordStrength("abcdefghijkl")).toBeLessThan(scorePasswordStrength("abcdEFghijkl"));
    expect(scorePasswordStrength("abcdEFghijkl")).toBeLessThan(scorePasswordStrength("abcdEF12ijkl"));
    expect(scorePasswordStrength("abcdEF12ijkl")).toBeLessThan(scorePasswordStrength("abcdEF12ij!l"));
  });

  it("penalises a run of the same character, so length alone cannot read as strong", () => {
    expect(scorePasswordStrength("aaaaaaaaaaaaaaaa")).toBeLessThan(scorePasswordStrength("abcdefghijklmnop"));
  });

  it("never exceeds the meter's range", () => {
    expect(scorePasswordStrength("Xq7#mLp2$wZk9!aB3&nQ8*uV")).toBeLessThanOrEqual(100);
    expect(scorePasswordStrength("a")).toBeGreaterThanOrEqual(0);
  });

  it("reaches the top band for a long, varied password", () => {
    expect(getStrengthLabel(scorePasswordStrength("Xq7#mLp2$wZk9!aB"))).toBe("Strong");
  });

  it("stays in the weak bands for a short, plain one", () => {
    expect(getStrengthLabel(scorePasswordStrength("abcdef"))).toBe("Weak");
  });

  it("keeps a single-class password weak however long it runs", () => {
    // Length must not buy its way past the variety ceiling.
    expect(getStrengthLabel(scorePasswordStrength("sunflowerpetal"))).toBe("Weak");
    expect(getStrengthLabel(scorePasswordStrength("sunflowereeeee"))).toBe("Weak");
    expect(getStrengthLabel(scorePasswordStrength("abcdefghijklmnopqrstuvwxyz"))).toBe("Weak");
  });

  it("caps each variety band, so only all four classes can read as strong", () => {
    expect(getStrengthLabel(scorePasswordStrength("sunflowerpetalxx"))).toBe("Weak");      // 1 class
    expect(getStrengthLabel(scorePasswordStrength("Sunflowerpetalxx"))).toBe("Fair");      // 2
    expect(getStrengthLabel(scorePasswordStrength("Sunflowerpetal12"))).toBe("Good");      // 3
    expect(getStrengthLabel(scorePasswordStrength("Sunflowerpetal1!"))).toBe("Strong");    // 4
  });
});
