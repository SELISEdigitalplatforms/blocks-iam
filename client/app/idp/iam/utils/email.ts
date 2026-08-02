/**
 * Accepts the same addresses the inline `/^[^\s@]+@[^\s@]+\.[^\s@]+$/` checks
 * accepted: a non-empty local part, exactly one "@", and a domain carrying a dot
 * with at least one character on either side.
 *
 * Written without that regex because it backtracks super-linearly. The dot is
 * not excluded from `[^\s@]`, so the two domain groups can each match it and the
 * engine has to retry every split of the domain before rejecting an address that
 * has no dot at all. This is a formatting gate for enabling a lookup, not an
 * RFC 5322 parser, and it is deliberately no stricter than what it replaced.
 */
export const isValidEmailFormat = (value: string): boolean => {
  const at = value.indexOf("@");

  // A local part of at least one character, and exactly one separator.
  if (at < 1 || at !== value.lastIndexOf("@")) {
    return false;
  }

  // A single character class with no quantifier, so this scan stays linear.
  if (/\s/.test(value)) {
    return false;
  }

  const domain = value.slice(at + 1);
  const dot = domain.indexOf(".", 1);

  return dot !== -1 && dot <= domain.length - 2;
};
