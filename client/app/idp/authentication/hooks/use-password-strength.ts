import { useMemo } from "react";
import type { CompiledPasswordPolicy } from "../utils/password-policy.util";
import {
  STRENGTH_THRESHOLDS,
  getStrengthColor,
  getStrengthLabel,
  getStrengthTextColor,
} from "../utils/password-strength.util";
import type { PasswordChecks, PasswordRequirement } from "../utils/password-strength.util";

export type { PasswordChecks, PasswordRequirement } from "../utils/password-strength.util";

export const usePasswordStrength = (password: string, policy: CompiledPasswordPolicy | null) =>
  useMemo(() => {
    const criteria = policy?.criteria ?? [];
    const checks: PasswordChecks = {};
    for (const criterion of criteria) {
      checks[criterion.key] = criterion.test(password);
    }

    const requirements: PasswordRequirement[] = criteria.map(({ key, label }) => ({ key, label }));
    const met = criteria.filter((criterion) => checks[criterion.key]).length;
    const score = criteria.length > 0 ? (met / criteria.length) * 100 : 0;
    // "Strong" is reserved for a password the policy actually accepts, so a near-miss on a
    // many-rule policy cannot round its way into the top band.
    const strength =
      password === "" || criteria.length === 0
        ? 0
        : met === criteria.length
          ? 100
          : Math.min(Math.round(score), STRENGTH_THRESHOLDS.STRONG);

    // The compiled pattern stays the only authority on whether the password is acceptable;
    // the criteria above exist to explain and score it, never to gate submission.
    const policySatisfied = policy ? policy.test(password) : password.length > 0;

    return {
      strength,
      checks,
      requirements,
      allRequirementsMet: policySatisfied,
      policySatisfied,
      /** Shown only if every listed rule passes but the whole pattern still rejects. */
      unexplainedFailure: policy != null && !policySatisfied && met === criteria.length,
      policyMessage: policy?.message ?? null,
      hasPolicy: criteria.length > 0,
      getStrengthColor: () => getStrengthColor(strength),
      getStrengthTextColor: () => getStrengthTextColor(strength),
      getStrengthLabel: () => getStrengthLabel(strength),
    };
  }, [password, policy]);
