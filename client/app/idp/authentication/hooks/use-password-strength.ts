import { useMemo } from "react";
import type {
  IOidcPasswordPolicy,
  PasswordPolicyChecks,
  PasswordPolicyRequirement,
} from "../utils/password-policy.util";
import {
  buildPasswordPolicyRequirements,
  checkPasswordAgainstPolicy,
  resolvePasswordPolicy,
} from "../utils/password-policy.util";
import {
  STRENGTH_THRESHOLDS,
  getStrengthColor,
  getStrengthLabel,
  getStrengthTextColor,
} from "../utils/password-strength.util";

export type {
  PasswordPolicyChecks,
  PasswordPolicyRequirement,
} from "../utils/password-policy.util";

export const usePasswordStrength = (
  password: string,
  policy: IOidcPasswordPolicy | null | undefined,
) =>
  useMemo(() => {
    // No tenant policy (unconfigured, still loading, or failed to load) falls back to the FE's
    // own baseline rule rather than dropping to "no requirements at all".
    const effectivePolicy = resolvePasswordPolicy(policy);
    const requirements: PasswordPolicyRequirement[] = buildPasswordPolicyRequirements(effectivePolicy);
    const checks: PasswordPolicyChecks = checkPasswordAgainstPolicy(password, effectivePolicy);

    // requirements/checks are two views over the same policy flags (built in the same order
    // from the same source), so they always describe the same set of rows -- there is no
    // separate "whole policy" test that could disagree with "every listed row passed".
    const hasPolicy = requirements.length > 0;
    const met = requirements.filter((requirement) => checks[requirement.key]).length;
    const allRequirementsMet = hasPolicy ? met === requirements.length : password.length > 0;

    const strength =
      !hasPolicy || password === ""
        ? 0
        : met === requirements.length
          ? 100
          : Math.min(Math.round((met / requirements.length) * 100), STRENGTH_THRESHOLDS.STRONG);

    return {
      strength,
      checks,
      requirements,
      allRequirementsMet,
      hasPolicy,
      getStrengthColor: () => getStrengthColor(strength),
      getStrengthTextColor: () => getStrengthTextColor(strength),
      getStrengthLabel: () => getStrengthLabel(strength),
    };
  }, [password, policy]);
