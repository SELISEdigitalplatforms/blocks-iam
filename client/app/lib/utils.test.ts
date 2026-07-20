import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  cn,
  formatDate,
  formatFullDate,
  parseDateString,
  compareDates,
  BREADCRUMB_CUSTOM_TITLES,
  clearBreadCrumbTitleEntry,
  debounce,
  normalizeSearchQueryText,
  debounceSearchQuery,
  parseMongoDBString,
  checkValidDate,
  deepEqual,
  clearQueryString,
  getUniqueID,
  formatSize,
} from "./utils";

describe("cn", () => {
  it("merges class names and dedupes conflicting tailwind classes", () => {
    expect(cn("p-2", "p-4")).toBe("p-4");
    expect(cn("text-sm", false, undefined, "font-bold")).toBe("text-sm font-bold");
  });
});

describe("formatDate", () => {
  const date = new Date(2026, 0, 5, 9, 7); // 05/01/2026, 09:07

  it("pads day/month/hour/minute and includes time by default", () => {
    expect(formatDate(date)).toBe("05/01/2026, 09:07");
  });

  it("omits the time when withoutTime is true", () => {
    expect(formatDate(date, true)).toBe("05/01/2026");
  });
});

describe("formatFullDate", () => {
  const date = new Date(2026, 2, 9, 14, 3); // Mar 09, 2026 at 14:03

  it("uses the month name and includes time by default", () => {
    expect(formatFullDate(date)).toBe("Mar 09, 2026 at 14:03");
  });

  it("omits the time when withoutTime is true", () => {
    expect(formatFullDate(date, true)).toBe("Mar 09, 2026");
  });
});

describe("parseDateString / compareDates", () => {
  it("parses an ISO string into a Date", () => {
    expect(parseDateString("2026-01-01T00:00:00Z").getTime()).toBe(
      new Date("2026-01-01T00:00:00Z").getTime(),
    );
  });

  it("returns a negative number when A precedes B", () => {
    expect(compareDates("2026-01-01", "2026-02-01")).toBeLessThan(0);
  });

  it("returns a positive number when A follows B", () => {
    expect(compareDates("2026-03-01", "2026-01-01")).toBeGreaterThan(0);
  });

  it("returns 0 for equal dates", () => {
    expect(compareDates("2026-01-01", "2026-01-01")).toBe(0);
  });
});

describe("clearBreadCrumbTitleEntry", () => {
  it("nulls out the stored breadcrumb title", () => {
    BREADCRUMB_CUSTOM_TITLES["/foo"] = "Foo";
    clearBreadCrumbTitleEntry("/foo");
    expect(BREADCRUMB_CUSTOM_TITLES["/foo"]).toBeNull();
  });
});

describe("debounce", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("invokes the fn only once after the delay with the latest args", () => {
    const fn = vi.fn();
    const d = debounce(fn, 200);
    d("a");
    d("b");
    expect(fn).not.toHaveBeenCalled();
    vi.advanceTimersByTime(200);
    expect(fn).toHaveBeenCalledTimes(1);
    expect(fn).toHaveBeenCalledWith("b");
  });

  it("cancel() prevents a pending invocation", () => {
    const fn = vi.fn();
    const d = debounce(fn, 200);
    d("x");
    d.cancel();
    vi.advanceTimersByTime(500);
    expect(fn).not.toHaveBeenCalled();
  });

  it("flush() invokes immediately with the latest args", () => {
    const fn = vi.fn();
    const d = debounce(fn, 200);
    d("y");
    d.flush();
    expect(fn).toHaveBeenCalledWith("y");
    // No further call after the timer would have fired.
    vi.advanceTimersByTime(200);
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it("flush() is a no-op when nothing is pending", () => {
    const fn = vi.fn();
    const d = debounce(fn, 200);
    d.flush();
    expect(fn).not.toHaveBeenCalled();
  });
});

describe("normalizeSearchQueryText", () => {
  it("trims and returns the text when it meets the min length", () => {
    expect(normalizeSearchQueryText("  hello  ")).toBe("hello");
  });

  it("returns an empty string below the min length", () => {
    expect(normalizeSearchQueryText("hi")).toBe("");
  });

  it("honors a custom min length", () => {
    expect(normalizeSearchQueryText("hi", 2)).toBe("hi");
  });
});

describe("debounceSearchQuery", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("passes the normalized query to onChange after the delay", () => {
    const onChange = vi.fn();
    const search = debounceSearchQuery(onChange, 100, 3);
    search("  abcd  ");
    vi.advanceTimersByTime(100);
    expect(onChange).toHaveBeenCalledWith("abcd");
  });

  it("passes an empty string when the query is too short", () => {
    const onChange = vi.fn();
    const search = debounceSearchQuery(onChange, 100, 3);
    search("ab");
    vi.advanceTimersByTime(100);
    expect(onChange).toHaveBeenCalledWith("");
  });
});

describe("parseMongoDBString", () => {
  it("unwraps ISODate/ObjectId, $date objects, and NumberLong", () => {
    const input =
      '{ "_id": ObjectId("abc123"), "at": ISODate("2026-01-01"), "d": { "$date": "2026-02-02" }, "n": NumberLong(42) }';
    const out = parseMongoDBString(input);
    expect(out).toContain('"abc123"');
    expect(out).toContain('"2026-01-01"');
    expect(out).toContain('"2026-02-02"');
    expect(out).toContain("42");
    expect(out).not.toContain("ObjectId");
    expect(out).not.toContain("NumberLong");
  });
});

describe("checkValidDate", () => {
  it("returns true for a valid modern date", () => {
    expect(checkValidDate("2026-01-01")).toBe(true);
  });

  it("returns false for an invalid date string", () => {
    expect(checkValidDate("not-a-date")).toBe(false);
  });

  it("returns false for dates before 1900-01-01", () => {
    expect(checkValidDate("1800-01-01")).toBe(false);
  });
});

describe("deepEqual", () => {
  it("returns true for structurally equal nested objects", () => {
    expect(deepEqual({ a: 1, b: { c: [1, 2] } }, { a: 1, b: { c: [1, 2] } })).toBe(true);
  });

  it("returns false when values differ", () => {
    expect(deepEqual({ a: 1 }, { a: 2 })).toBe(false);
  });

  it("returns false when key counts differ", () => {
    expect(deepEqual({ a: 1 }, { a: 1, b: 2 })).toBe(false);
  });

  it("returns false when a key is missing on the other object", () => {
    expect(deepEqual({ a: 1 }, { b: 1 })).toBe(false);
  });

  it("returns false comparing an object with null", () => {
    expect(deepEqual({ a: 1 }, null)).toBe(false);
  });

  it("returns true for identical primitives", () => {
    expect(deepEqual(5, 5)).toBe(true);
  });
});

describe("clearQueryString", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/page?a=1&b=2&c=3");
  });

  it("strips all query params by default", () => {
    clearQueryString();
    expect(window.location.search).toBe("");
  });

  it("keeps only the params listed in except", () => {
    clearQueryString({ except: ["b"] });
    expect(window.location.search).toBe("?b=2");
  });
});

describe("getUniqueID", () => {
  it("produces a prefixed id with 6 trailing uppercase letters", () => {
    const id = getUniqueID();
    expect(id).toMatch(/^BLK-\d+-[A-Z]{6}$/);
  });

  it("produces distinct ids across calls", () => {
    expect(getUniqueID()).not.toBe(getUniqueID());
  });
});

describe("formatSize", () => {
  it("formats bytes into the largest sensible unit", () => {
    expect(formatSize(1024)).toBe("1 KB");
    expect(formatSize(1024 ** 2)).toBe("1 MB");
    expect(formatSize(0)).toBe("0 B");
  });

  it("respects the input unit", () => {
    expect(formatSize(1, "MB")).toBe("1 MB");
    expect(formatSize(1024, "KB")).toBe("1 MB");
  });

  it("respects the decimals argument", () => {
    expect(formatSize(1536, "B", 1)).toBe("1.5 KB");
  });
});
