import { describe, expect, it } from "vitest";
import { addPermissionFormDefaultValue, addPermissionFormSchema } from "./utils";

const valid = {
  name: "Read users",
  type: "2",
  resource: "user-read",
  resourceGroup: "users",
  tags: ["a"],
  description: "",
  dependentPermissions: [],
};

describe("addPermissionFormDefaultValue", () => {
  it("starts empty", () => {
    expect(addPermissionFormDefaultValue.name).toBe("");
    expect(addPermissionFormDefaultValue.type).toBe("");
  });
});

describe("addPermissionFormSchema", () => {
  it("accepts a valid permission", () => {
    expect(addPermissionFormSchema.safeParse(valid).success).toBe(true);
  });

  it("requires a type", () => {
    expect(addPermissionFormSchema.safeParse({ ...valid, type: "" }).success).toBe(false);
  });

  it("rejects a resource with spaces", () => {
    expect(addPermissionFormSchema.safeParse({ ...valid, resource: "a b" }).success).toBe(false);
  });

  it("enforces the resource format when type is 1", () => {
    expect(
      addPermissionFormSchema.safeParse({ ...valid, type: "1", resource: "plain" }).success,
    ).toBe(false);
    expect(
      addPermissionFormSchema.safeParse({ ...valid, type: "1", resource: "iam::user::read" })
        .success,
    ).toBe(true);
  });

  it("rejects a description longer than 150 characters", () => {
    const result = addPermissionFormSchema.safeParse({ ...valid, description: "x".repeat(151) });
    expect(result.success).toBe(false);
  });
});
