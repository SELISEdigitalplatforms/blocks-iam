import { describe, expect, it } from "vitest";
import { PermissionSeverityLevel } from "@blocks-idp/iam/models/permission";
import { permissionFormDefaultValue, permissionFormSchema } from "./utils";

const valid = {
  name: "Read users",
  type: 2,
  resource: "user-read",
  resourceGroup: "users",
  tags: ["a"],
  description: "",
  dependentPermissions: [],
  permissionSeverity: PermissionSeverityLevel.High,
};

describe("permissionFormDefaultValue", () => {
  it("starts with an empty name and no tags", () => {
    expect(permissionFormDefaultValue.name).toBe("");
    expect(permissionFormDefaultValue.tags).toEqual([]);
  });
});

describe("permissionFormSchema", () => {
  it("accepts a valid permission", () => {
    expect(permissionFormSchema.safeParse(valid).success).toBe(true);
  });

  it("requires a name", () => {
    expect(permissionFormSchema.safeParse({ ...valid, name: "" }).success).toBe(false);
  });

  it("rejects a resource containing spaces", () => {
    const result = permissionFormSchema.safeParse({ ...valid, resource: "user read" });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.map((i) => i.message)).toContain(
        "Resource can't contain spaces",
      );
    }
  });

  it("requires a severity", () => {
    const result = permissionFormSchema.safeParse({
      ...valid,
      permissionSeverity: "" as unknown as PermissionSeverityLevel,
    });
    expect(result.success).toBe(false);
  });

  it("enforces the service::controller::name format when type is 1", () => {
    const bad = permissionFormSchema.safeParse({ ...valid, type: 1, resource: "user-read" });
    expect(bad.success).toBe(false);
    if (!bad.success) {
      expect(bad.error.issues.map((i) => i.message)).toContain(
        "Resource format should be service :: controller :: name",
      );
    }
    const good = permissionFormSchema.safeParse({
      ...valid,
      type: 1,
      resource: "iam::user::read",
    });
    expect(good.success).toBe(true);
  });

  it("requires a type of at least 1", () => {
    expect(permissionFormSchema.safeParse({ ...valid, type: 0 }).success).toBe(false);
  });
});
