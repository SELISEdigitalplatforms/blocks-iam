import { describe, expect, it } from "vitest";
import { buildSignupFormSchema, signupFormDefaultValue } from "./utils";

const base = { firstName: "Jane", lastName: "Doe", email: "jane@example.com" };

describe("buildSignupFormSchema", () => {
  it("does not require an organization name when none is collected", () => {
    expect(buildSignupFormSchema(false).safeParse({ ...base, organizationName: "" }).success).toBe(true);
  });

  it("requires the organization name when creation is the only option (H16)", () => {
    const schema = buildSignupFormSchema(true);
    expect(schema.safeParse({ ...base, organizationName: " " }).success).toBe(false);
    expect(schema.safeParse({ ...base, organizationName: "Acme" }).success).toBe(true);
  });

  describe("join-or-create", () => {
    const schema = buildSignupFormSchema(true, true);

    it("H12: Join is the default and needs no name", () => {
      expect(signupFormDefaultValue.organizationChoice).toBe("join");
      expect(schema.safeParse({ ...base, organizationName: "", organizationChoice: "join" }).success).toBe(true);
    });

    it("H13: Create requires a non-blank name, reported on the name field", () => {
      const result = schema.safeParse({ ...base, organizationName: "  ", organizationChoice: "create" });
      expect(result.success).toBe(false);
      expect(result.error?.issues[0]).toEqual(
        expect.objectContaining({ path: ["organizationName"], message: "Organization name is required" }),
      );
      expect(schema.safeParse({ ...base, organizationName: "Acme", organizationChoice: "create" }).success).toBe(true);
    });

    it("rejects an unknown choice", () => {
      expect(schema.safeParse({ ...base, organizationChoice: "other" }).success).toBe(false);
    });
  });
});
