export interface IOidcPasswordPolicy {
  regex: string;
  message: string | null;
  ignoreCase: boolean;
}

/** One independently testable rule drawn out of the tenant pattern. */
export interface PasswordPolicyCriterion {
  key: string;
  label: string;
  test: (password: string) => boolean;
}

export interface CompiledPasswordPolicy {
  test: (password: string) => boolean;
  message: string;
  /**
   * The rules the strength meter scores against. Patterns shaped as
   * `^(?=…)(?=…).{n,m}$` split into one rule per assertion plus a length rule; anything
   * else falls back to a single rule carrying the whole pattern and the tenant message.
   * Never used to gate submission — {@link test} stays the only authority on that.
   */
  criteria: PasswordPolicyCriterion[];
}

export const PASSWORD_MAX_INPUT_LENGTH = 256;

export const DEFAULT_POLICY_MESSAGE =
  "Password does not meet this project's requirements.";

/**
 * Label for the fallback checklist row shown when a tenant pattern can't be split into
 * named rules. Deliberately positive: unlike {@link DEFAULT_POLICY_MESSAGE} (a failure
 * message, always paired with a fixed ✗), this row's icon toggles with the password, so a
 * negatively-phrased label would read backwards next to a ✓.
 */
export const FALLBACK_REQUIREMENT_LABEL = "Meets this project's password requirements";

/** Index just past the group opening at `start`, or null when it never closes. */
const findGroupEnd = (pattern: string, start: number): number | null => {
  let depth = 0;
  let inClass = false;
  for (let i = start; i < pattern.length; i++) {
    const character = pattern[i];
    if (character === "\\") {
      i++;
      continue;
    }
    if (inClass) {
      if (character === "]") inClass = false;
      continue;
    }
    if (character === "[") inClass = true;
    else if (character === "(") depth++;
    else if (character === ")" && --depth === 0) return i + 1;
  }
  return null;
};

const CLASS_LABELS: ReadonlyArray<readonly [RegExp, string]> = [
  [/^(?:\\d|\[0-9\])$/, "number"],
  [/^\[a-z\]$/, "lowercase letter"],
  [/^\[A-Z\]$/, "uppercase letter"],
  [/^\[(?:a-zA-Z|A-Za-z)\]$/, "letter"],
  [/^\\W$/, "special character"],
  [/^\\s$/, "space"],
];

/** A plain-English noun for a single-character pattern, or null when it is not one we name. */
const describeToken = (token: string): string | null => {
  for (const [pattern, label] of CLASS_LABELS) {
    if (pattern.test(token)) return label;
  }
  if (!/^\[[^\]]+\]$/.test(token)) return null;

  const members = token.slice(1, -1);
  const negated = members.startsWith("^");
  // Shorthands carry their own meaning, so drop them before asking what is left over.
  const literals = (negated ? members.slice(1) : members).replace(/\\[dwsDWS]/g, "");
  if (negated) {
    return /a-z/i.test(members) && /0-9|\\d|\\w/.test(members) ? "special character" : null;
  }
  return /[a-z0-9]/i.test(literals) ? null : "special character";
};

const pluralize = (noun: string, count: number): string => (count === 1 ? noun : `${noun}s`);

const capitalize = (text: string): string => text.charAt(0).toUpperCase() + text.slice(1);

const LEADING_SKIP = /^(?:\.|\[\\s\\S\]|\[\\S\\s\]|\\D|\\W)[*+]\??/;

/** Turns the body of a `(?=…)` into a label, or null when its shape is unfamiliar. */
const describeLookahead = (body: string): string | null => {
  const grouped = body.match(/^\((.+)\)\{(\d+),?\d*\}$/);
  const [source, count] = grouped
    ? [grouped[1], Number(grouped[2])]
    : [body, 1];

  const scanned = source.replace(LEADING_SKIP, "");
  const repeated = scanned.match(/^(.+?)\{(\d+),?\d*\}$/);
  const token = repeated ? repeated[1] : scanned;
  const total = repeated ? Number(repeated[2]) * count : count;

  const noun = describeToken(token);
  if (!noun || total < 1) return null;
  return total === 1 ? capitalize(`one ${noun}`) : `At least ${total} ${pluralize(noun, total)}`;
};

/** Reads the `.{n,m}` tail. Null means "nothing worth showing"; undefined means "give up". */
const describeLength = (tail: string): string | null | undefined => {
  const match = tail.match(/^(?:\.|\[\\s\\S\]|\[\\S\\s\])(\*|\+|\{(\d+)(,(\d*))?\})?$/);
  if (!match) return undefined;

  const [, quantifier, minimum, hasRange, maximum] = match;
  if (quantifier === "*") return null;
  const min = quantifier === "+" ? 1 : quantifier ? Number(minimum) : 1;
  const max = quantifier && quantifier !== "+" && !hasRange ? min : Number(maximum || NaN);

  if (min < 1) return null;
  if (Number.isFinite(max)) {
    return min === max
      ? `Exactly ${min} ${pluralize("character", min)}`
      : `Between ${min} and ${max} characters`;
  }
  return `At least ${min} ${pluralize("character", min)}`;
};

const toCriterion = (
  key: string,
  label: string,
  source: string,
  flags: string,
): PasswordPolicyCriterion | null => {
  try {
    const regex = new RegExp(source, flags);
    return { key, label, test: (password: string) => regex.test(password) };
  } catch {
    return null;
  }
};

/**
 * Splits an anchored lookahead pattern into one rule per assertion. Returns null whenever the
 * pattern strays from that shape, so the meter never invents rules the tenant did not write.
 */
const derivePolicyCriteria = (
  pattern: string,
  flags: string,
): PasswordPolicyCriterion[] | null => {
  if (!pattern.startsWith("^") || !pattern.endsWith("$")) return null;
  // A trailing "$" preceded by an odd run of backslashes is a literal, not an anchor.
  if (/(?:^|[^\\])(?:\\\\)*\\$/.test(pattern.slice(0, -1))) return null;

  const body = pattern.slice(1, -1);
  const criteria: PasswordPolicyCriterion[] = [];
  let cursor = 0;

  while (body.startsWith("(?=", cursor)) {
    const end = findGroupEnd(body, cursor);
    if (end === null) return null;
    const group = body.slice(cursor, end);
    const label = describeLookahead(group.slice(3, -1));
    if (!label) return null;
    const criterion = toCriterion(`rule-${criteria.length}`, label, `^${group}`, flags);
    if (!criterion) return null;
    criteria.push(criterion);
    cursor = end;
  }

  const tail = body.slice(cursor);
  const lengthLabel = describeLength(tail);
  if (lengthLabel === undefined) return null;
  if (lengthLabel) {
    const criterion = toCriterion("length", lengthLabel, `^${tail}$`, flags);
    if (!criterion) return null;
    criteria.push(criterion);
  }

  return criteria.length > 0 ? criteria : null;
};

export const compilePasswordPolicy = (
  policy: IOidcPasswordPolicy | null | undefined,
): CompiledPasswordPolicy | null => {
  if (!policy || !policy.regex.trim()) return null;

  const flags = policy.ignoreCase ? "i" : "";
  let regex: RegExp;
  try {
    regex = new RegExp(policy.regex, flags);
  } catch {
    return null;
  }

  const test = (password: string) => regex.test(password);
  const message = policy.message ?? DEFAULT_POLICY_MESSAGE;

  return {
    test,
    message,
    criteria: derivePolicyCriteria(policy.regex, flags) ?? [
      { key: "policy", label: FALLBACK_REQUIREMENT_LABEL, test },
    ],
  };
};
