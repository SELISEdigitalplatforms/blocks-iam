export const STRENGTH_THRESHOLDS = {
  WEAK: 25,
  MEDIUM: 50,
  STRONG: 75,
} as const;

export const STRENGTH_COLORS = {
  WEAK: "bg-red-500",
  MEDIUM_WEAK: "bg-orange-500",
  MEDIUM_STRONG: "bg-yellow-500",
  STRONG: "bg-green-600",
} as const;

export const STRENGTH_TEXT_COLORS = {
  WEAK: "text-red-500",
  MEDIUM_WEAK: "text-orange-500",
  MEDIUM_STRONG: "text-yellow-600",
  STRONG: "text-green-600",
} as const;

export const STRENGTH_LABELS = {
  WEAK: "Weak",
  MEDIUM_WEAK: "Fair",
  MEDIUM_STRONG: "Good",
  STRONG: "Strong",
} as const;

export type StrengthBand = keyof typeof STRENGTH_COLORS;

/** Number of segments the meter is drawn with; one band each. */
export const STRENGTH_SEGMENTS = 4;

export const getStrengthBand = (strength: number): StrengthBand => {
  if (strength <= STRENGTH_THRESHOLDS.WEAK) return "WEAK";
  if (strength <= STRENGTH_THRESHOLDS.MEDIUM) return "MEDIUM_WEAK";
  if (strength <= STRENGTH_THRESHOLDS.STRONG) return "MEDIUM_STRONG";
  return "STRONG";
};

export const getStrengthColor = (strength: number): string =>
  STRENGTH_COLORS[getStrengthBand(strength)];

export const getStrengthTextColor = (strength: number): string =>
  STRENGTH_TEXT_COLORS[getStrengthBand(strength)];

export const getStrengthLabel = (strength: number): string =>
  STRENGTH_LABELS[getStrengthBand(strength)];

/** How many of the meter's segments a score lights up, always at least one above zero. */
export const getFilledSegments = (strength: number): number =>
  strength <= 0 ? 0 : Math.max(1, Math.ceil((strength / 100) * STRENGTH_SEGMENTS));

// Fixed literals written directly in this file's own source, never built from a policy.
const HAS_UPPER = /[A-Z]/;
const HAS_LOWER = /[a-z]/;
const HAS_DIGIT = /[0-9]/;
const HAS_SPECIAL = /[^A-Za-z0-9]/;
const HAS_RUN_OF_THREE = /(.)\1\1/;

/** Lengths the score steps up at, and what each step is worth. */
const LENGTH_TIERS = [
  { atLeast: 16, score: 50 },
  { atLeast: 12, score: 40 },
  { atLeast: 10, score: 30 },
  { atLeast: 8, score: 20 },
  { atLeast: 6, score: 10 },
] as const;

/** Each distinct character class present is worth this much. */
const VARIETY_PER_CLASS = 12;

/**
 * The most a password can score for each number of distinct character classes it uses, indexed by
 * that count. Length cannot buy its way past this: "sunflowereeeee" is fourteen characters of one
 * class, which is a weak password however long it runs.
 */
const VARIETY_CEILING = [0, STRENGTH_THRESHOLDS.WEAK, STRENGTH_THRESHOLDS.MEDIUM, STRENGTH_THRESHOLDS.STRONG, 100] as const;

/** Charged once for an obvious weakness, so "aaaaaaaaaaaa" cannot read as strong on length alone. */
const RUN_PENALTY = 20;

/**
 * How strong this password is *as a password* -- length and character variety -- with no reference
 * to the tenant's rule.
 *
 * Character variety sets the ceiling and length fills it in: a password drawing on one class only
 * cannot leave the weak band, two classes cannot pass fair, three cannot pass good, and only all
 * four can reach strong.
 *
 * Deliberately independent of {@link buildPasswordPolicyRequirements}: the requirement rows answer
 * "may I use this password here", which is pass/fail and the server's call, while the meter answers
 * "how good is this password", which is advice. Tying the meter to the rule made it meaningless for
 * a tenant whose rule is mostly length -- one requirement met reads as 100%.
 */
export const scorePasswordStrength = (password: string): number => {
  if (password === "") return 0;

  const length = LENGTH_TIERS.find((tier) => password.length >= tier.atLeast)?.score ?? 0;

  const variety =
    (HAS_UPPER.test(password) ? 1 : 0) +
    (HAS_LOWER.test(password) ? 1 : 0) +
    (HAS_DIGIT.test(password) ? 1 : 0) +
    (HAS_SPECIAL.test(password) ? 1 : 0);

  // Capped by variety first, then charged for the run, so a long single-class password with a
  // repeat still scores below a long single-class one without.
  const penalty = HAS_RUN_OF_THREE.test(password) ? RUN_PENALTY : 0;
  const score = Math.min(length + variety * VARIETY_PER_CLASS, VARIETY_CEILING[variety]) - penalty;

  // Anything typed scores at least 1, so the meter shows a first segment rather than reading as
  // "nothing entered".
  return Math.min(100, Math.max(1, score));
};

