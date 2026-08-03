import { describe, expect, it } from "vitest";
import { addRoleFormDefaultValue, addRoleFormSchema } from "./utils";

const valid = { name: "Admin", slug: "admin", description: "An admin role" };

describe("addRoleFormDefaultValue", () => {
  it("starts empty", () => {
    expect(addRoleFormDefaultValue).toEqual({ name: "", slug: "", description: "" });
  });
});

describe("addRoleFormSchema", () => {
  it("accepts a valid role", () => {
    expect(addRoleFormSchema.safeParse(valid).success).toBe(true);
  });

  it("allows an omitted description", () => {
    expect(addRoleFormSchema.safeParse({ name: "Admin", slug: "admin" }).success).toBe(true);
  });

  it("requires a name", () => {
    expect(addRoleFormSchema.safeParse({ ...valid, name: "" }).success).toBe(false);
  });

  it("rejects a slug containing spaces", () => {
    const result = addRoleFormSchema.safeParse({ ...valid, slug: "an admin" });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.map((i) => i.message)).toContain("Slug can not contain spaces");
    }
  });

  it("rejects a name longer than 50 characters", () => {
    expect(addRoleFormSchema.safeParse({ ...valid, name: "x".repeat(51) }).success).toBe(false);
  });
});
