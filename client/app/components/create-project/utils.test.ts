import { describe, expect, it, beforeEach } from "vitest";
import { useCreateProjectFormState, shortGuidGenerator } from "./utils";

describe("shortGuidGenerator", () => {
  it("returns a string of the requested length", () => {
    expect(shortGuidGenerator(8)).toHaveLength(8);
    expect(shortGuidGenerator(1)).toHaveLength(1);
  });

  it("uses only lowercase letters", () => {
    expect(shortGuidGenerator(20)).toMatch(/^[a-z]+$/);
  });

  it("returns an empty string for length 0", () => {
    expect(shortGuidGenerator(0)).toBe("");
  });
});

describe("useCreateProjectFormState", () => {
  beforeEach(() => {
    useCreateProjectFormState.getState().resetFormData();
  });

  it("exposes three form sections by default", () => {
    expect(useCreateProjectFormState.getState().formData).toHaveLength(3);
  });

  it("updates a form section by index", () => {
    const next = { name: "Renamed", isAcceptBlocksTerms: true, isUseBlocksExclusively: true };
    useCreateProjectFormState.getState().setFormData(0, next);
    expect(useCreateProjectFormState.getState().formData[0]).toEqual(next);
  });

  it("resets the form data back to defaults", () => {
    useCreateProjectFormState
      .getState()
      .setFormData(0, { name: "Temp", isAcceptBlocksTerms: true, isUseBlocksExclusively: true });
    useCreateProjectFormState.getState().resetFormData();
    expect(useCreateProjectFormState.getState().formData[0].name).toBe("");
  });
});
