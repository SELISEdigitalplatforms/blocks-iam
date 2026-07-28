import { describe, expect, it } from "vitest";
import { addServiceSchema, addServiceDefaultValues } from "./utils";

const valid = {
  serviceName: "billing",
  tags: ["a", "b"],
  serviceType: "backend" as const,
  description: "handles billing",
};

describe("addServiceDefaultValues", () => {
  it("defaults to a frontend service with no tags", () => {
    expect(addServiceDefaultValues.serviceType).toBe("frontend");
    expect(addServiceDefaultValues.tags).toEqual([]);
  });
});

describe("addServiceSchema", () => {
  it("accepts a valid service", () => {
    expect(addServiceSchema.safeParse(valid).success).toBe(true);
  });

  it("requires a service name", () => {
    expect(addServiceSchema.safeParse({ ...valid, serviceName: "" }).success).toBe(false);
  });

  it("rejects a service name longer than 100 characters", () => {
    expect(
      addServiceSchema.safeParse({ ...valid, serviceName: "x".repeat(101) }).success,
    ).toBe(false);
  });

  it("rejects duplicate tags", () => {
    const result = addServiceSchema.safeParse({ ...valid, tags: ["a", "a"] });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.map((i) => i.message)).toContain(
        "Duplicate tags are not allowed",
      );
    }
  });

  it("defaults the service type to frontend when omitted", () => {
    const result = addServiceSchema.safeParse({ serviceName: "svc", tags: [] });
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.serviceType).toBe("frontend");
    }
  });
});
