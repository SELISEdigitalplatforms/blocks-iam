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

export type PasswordChecks = { policy: boolean } | Record<string, never>;

export interface PasswordRequirement {
  key: "policy";
  label: string;
}

export const getStrengthColor = (strength: number): string => {
  if (strength <= STRENGTH_THRESHOLDS.WEAK) return STRENGTH_COLORS.WEAK;
  if (strength <= STRENGTH_THRESHOLDS.MEDIUM) return STRENGTH_COLORS.MEDIUM_WEAK;
  if (strength <= STRENGTH_THRESHOLDS.STRONG) return STRENGTH_COLORS.MEDIUM_STRONG;
  return STRENGTH_COLORS.STRONG;
};
