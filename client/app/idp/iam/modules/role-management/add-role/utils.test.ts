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
      expect(result.error.issues.map((i) => i.message)).toContain("A slug cannot contain spaces.");
    }
  });

  // The caps mirror the server (name 150, slug 200) so a value the API accepts is not blocked
  // client-side and the server's own messages stay reachable.
  it("accepts a name the server would accept", () => {
    expect(addRoleFormSchema.safeParse({ ...valid, name: "x".repeat(150) }).success).toBe(true);
  });

  it("rejects a name longer than the server allows", () => {
    expect(addRoleFormSchema.safeParse({ ...valid, name: "x".repeat(151) }).success).toBe(false);
  });

  it("rejects a slug longer than the server allows", () => {
    expect(addRoleFormSchema.safeParse({ ...valid, slug: "x".repeat(201) }).success).toBe(false);
  });
});
