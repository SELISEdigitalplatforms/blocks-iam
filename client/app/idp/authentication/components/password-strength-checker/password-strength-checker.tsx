import React, { useEffect } from "react";
import { Check, X } from "lucide-react";
import { usePasswordStrength } from "@blocks-idp/authentication/hooks/use-password-strength";
import type { IOidcPasswordPolicy } from "@blocks-idp/authentication/utils/password-policy.util";
import {
  STRENGTH_SEGMENTS,
  getFilledSegments,
} from "@blocks-idp/authentication/utils/password-strength.util";

interface PasswordStrengthCheckerProps {
  password: string;
  confirmPassword: string;
  onRequirementsMet: (met: boolean) => void;
  excludePassword?: string;
  excludePasswordLabel?: string;
  policy: IOidcPasswordPolicy | null | undefined;
}

const RequirementRow: React.FC<{ met: boolean; children: React.ReactNode }> = ({
  met,
  children,
}) => (
  <li className="flex items-start gap-2">
    {met ? (
      <Check className="h-4 w-4 shrink-0 text-success" aria-hidden="true" />
    ) : (
      <X className="h-4 w-4 shrink-0 text-red-500" aria-hidden="true" />
    )}
    <span className="text-xs leading-4">{children}</span>
  </li>
);

export const PasswordStrengthChecker: React.FC<PasswordStrengthCheckerProps> = ({
  password,
  confirmPassword,
  onRequirementsMet,
  excludePassword,
  excludePasswordLabel,
  policy,
}) => {
  const {
    checks,
    requirements,
    allRequirementsMet,
    strength,
    hasPolicy,
    getStrengthColor,
    getStrengthTextColor,
    getStrengthLabel,
  } = usePasswordStrength(password, policy);
  const passwordsMatch = password === confirmPassword && password !== "";
  const isDifferentFromExcluded =
    !excludePassword || password === "" || password !== excludePassword;

  useEffect(() => {
    const allMet =
      allRequirementsMet && passwordsMatch && isDifferentFromExcluded;
    onRequirementsMet(allMet);
  }, [
    password,
    confirmPassword,
    allRequirementsMet,
    passwordsMatch,
    excludePassword,
    isDifferentFromExcluded,
    onRequirementsMet,
  ]);

  const filledSegments = getFilledSegments(strength);
  const strengthLabel = getStrengthLabel();

  return (
    <div className="border-border-default mx-auto w-full rounded-lg border px-6 py-4 shadow-sm">
      <h2 className="mb-2 text-sm font-semibold text-high-emphasis">Password Requirements</h2>

      {hasPolicy && (
        <div className="mb-3 flex items-center gap-3">
          <div
            className="flex h-1.5 flex-1 gap-1"
            role="progressbar"
            aria-valuenow={strength}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuetext={`${strengthLabel} password`}
            aria-label="Password strength"
          >
            {Array.from({ length: STRENGTH_SEGMENTS }, (_, index) => (
              <div
                key={index}
                className={`h-full flex-1 rounded-full transition-colors duration-300 ${
                  index < filledSegments ? getStrengthColor() : "bg-neutral-200"
                }`}
              />
            ))}
          </div>
          <span className={`text-xs font-medium ${getStrengthTextColor()}`}>{strengthLabel}</span>
        </div>
      )}

      <p className="mb-2 text-xs text-medium-emphasis">
        Your password must meet these requirements:
      </p>

      <ul className="space-y-1.5 text-medium-emphasis">
        {requirements.map((requirement) => (
          <RequirementRow key={requirement.key} met={Boolean(checks[requirement.key])}>
            {requirement.label}
          </RequirementRow>
        ))}

        {excludePassword && password !== "" && (
          <RequirementRow met={isDifferentFromExcluded}>
            {excludePasswordLabel ?? "New password shouldn't match current password"}
          </RequirementRow>
        )}

        <RequirementRow met={passwordsMatch}>Passwords match</RequirementRow>
      </ul>

      {/* Admin-authored guidance, display only -- never independently checked, so it never
          gets a pass/fail icon of its own. */}
    </div>
  );
};
