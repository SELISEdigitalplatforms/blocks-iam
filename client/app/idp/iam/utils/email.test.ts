import { describe, expect, it } from "vitest";

import { isValidEmailFormat } from "./email";

// The regex this replaced was /^[^\s@]+@[^\s@]+\.[^\s@]+$/. These cases pin the
// replacement to the same accept/reject set, including the odd ones the old
// pattern allowed, so the swap cannot quietly tighten or loosen the gate.
const previouslyAccepted = [
  "a@b.c",
  "user@example.com",
  "user.name@example.co.uk",
  "user+tag@example.com",
  // The old pattern let the domain begin with a dot as long as a later dot had
  // characters on both sides, because [^\s@]+ matches a dot itself.
  "user@..b",
];

const previouslyRejected = [
  "",
  "user",
  "@example.com",
  "user@",
  "user@example",
  "user@.com",
  "user@example.",
  "user name@example.com",
  "user@exam ple.com",
  "user@@example.com",
  "a@b@c.com",
  "\tuser@example.com",
];

describe("isValidEmailFormat", () => {
  it.each(previouslyAccepted)("accepts %j", (value) => {
    expect(isValidEmailFormat(value)).toBe(true);
  });

  it.each(previouslyRejected)("rejects %j", (value) => {
    expect(isValidEmailFormat(value)).toBe(false);
  });

  it("rejects a long dotless domain without super-linear backtracking", () => {
    // This is the input shape that made the old regex quadratic: the engine had
    // to try every split of the domain before concluding there was no dot. A
    // linear check returns effectively instantly, so a generous bound still
    // fails loudly if the implementation regresses to a backtracking one.
    const hostile = `user@${"a".repeat(50_000)}`;

    const started = performance.now();
    const result = isValidEmailFormat(hostile);

    expect(result).toBe(false);
    expect(performance.now() - started).toBeLessThan(1_000);
  });
});
