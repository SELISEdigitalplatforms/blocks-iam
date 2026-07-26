import { describe, expect, it } from "vitest";
import {
  createProjectNamingFormDefaultValue,
  createProjectNamingFormSchema,
} from "./utils";

const valid = {
  name: "My Project",
  isAcceptBlocksTerms: true,
  isUseBlocksExclusively: true,
};

describe("createProjectNamingFormDefaultValue", () => {
  it("starts unaccepted with an empty name", () => {
    expect(createProjectNamingFormDefaultValue.name).toBe("");
    expect(createProjectNamingFormDefaultValue.isAcceptBlocksTerms).toBe(false);
    expect(createProjectNamingFormDefaultValue.isUseBlocksExclusively).toBe(false);
  });
});

describe("createProjectNamingFormSchema", () => {
  it("accepts a valid form", () => {
    expect(createProjectNamingFormSchema.safeParse(valid).success).toBe(true);
  });

  it("requires a name of at least 3 characters", () => {
    expect(createProjectNamingFormSchema.safeParse({ ...valid, name: "ab" }).success).toBe(false);
  });

  it("rejects a name longer than 100 characters", () => {
    expect(
      createProjectNamingFormSchema.safeParse({ ...valid, name: "x".repeat(101) }).success,
    ).toBe(false);
  });

  it("requires the terms to be accepted", () => {
    expect(
      createProjectNamingFormSchema.safeParse({ ...valid, isAcceptBlocksTerms: false }).success,
    ).toBe(false);
  });

  it("requires exclusive use to be accepted", () => {
    expect(
      createProjectNamingFormSchema.safeParse({ ...valid, isUseBlocksExclusively: false }).success,
    ).toBe(false);
  });
});
