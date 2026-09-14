import { describe, expect, it } from "vitest";
import { buildActivationFormSchema } from "./utils";

const values = {
  firstname: "Grace",
  lastname: "Hopper",
  password: "Sunflower",
  confirmPassword: "Sunflower",
};

describe("buildActivationFormSchema", () => {
  it("applies the active tenant policy to the submitted password", () => {
    const policy = { test: (password: string) => password.includes("7"), message: "Include 7" };
    const result = buildActivationFormSchema(policy).safeParse(values);
    expect(result.success).toBe(false);
    if (!result.success) expect(result.error.issues[0]?.message).toBe("Include 7");
  });

  it("keeps the no-whitespace rule and 256-character cap", () => {
    const schema = buildActivationFormSchema(null);
    expect(schema.safeParse({ ...values, password: "has space" }).success).toBe(false);
    expect(schema.safeParse({ ...values, password: "a".repeat(257) }).success).toBe(false);
  });
});
