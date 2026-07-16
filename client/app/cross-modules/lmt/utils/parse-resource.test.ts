import { describe, expect, it } from "vitest";
import { parseResourceType, formatResourceName } from "./parse-resource";

describe("parseResourceType", () => {
  it("returns an empty string for empty input", () => {
    expect(parseResourceType("")).toBe("");
  });

  it("extracts and uppercases the middle segment of a scoped resource", () => {
    expect(parseResourceType("blocks-identifier-api::people::invite")).toBe("PEOPLE");
  });

  it("uppercases the whole string when there is no scope separator", () => {
    expect(parseResourceType("people")).toBe("PEOPLE");
  });
});

describe("formatResourceName", () => {
  it("title-cases the parsed resource type", () => {
    expect(formatResourceName("blocks-identifier-api::people::invite")).toBe("People");
  });

  it("returns 'Unknown' for empty input", () => {
    expect(formatResourceName("")).toBe("Unknown");
  });

  it("formats a bare resource name", () => {
    expect(formatResourceName("roles")).toBe("Roles");
  });
});
