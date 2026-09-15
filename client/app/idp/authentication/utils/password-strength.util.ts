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

export type PasswordChecks = Record<string, boolean>;

export interface PasswordRequirement {
  key: string;
  label: string;
}

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
