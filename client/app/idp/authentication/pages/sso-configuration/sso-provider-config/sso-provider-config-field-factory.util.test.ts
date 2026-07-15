import { describe, expect, it } from "vitest";
import {
  createProviderField,
  createNameField,
  createClientIdField,
  createClientSecretField,
  createRedirectUrlField,
  createAudienceField,
  createCommonOAuthFields,
} from "./sso-provider-config-field-factory.util";

describe("createProviderField", () => {
  it("keeps the password type for password fields", () => {
    const field = createProviderField("9", "Secret", "secret", "password");
    expect(field).toMatchObject({ id: "9", label: "Secret", name: "secret", type: "password" });
  });

  it("normalizes any non-password type to input", () => {
    const field = createProviderField("9", "Foo", "foo", "input");
    expect(field.type).toBe("input");
  });

  it("merges overrides into the base field", () => {
    const field = createProviderField("9", "Foo", "foo", "input", {
      isDisabled: true,
      description: "hint",
    });
    expect(field).toMatchObject({ isDisabled: true, description: "hint" });
  });
});

describe("named field factories", () => {
  it("createNameField is disabled with a default description and can be overridden", () => {
    const field = createNameField();
    expect(field).toMatchObject({ id: "1", name: "provider", type: "input", isDisabled: true });
    expect(field.description).toContain("identifier");

    const overridden = createNameField({ isDisabled: false });
    expect(overridden.isDisabled).toBe(false);
  });

  it("createClientIdField builds the clientId input", () => {
    expect(createClientIdField()).toMatchObject({ id: "2", name: "clientId", type: "input" });
  });

  it("createClientSecretField is a password field with a masking description", () => {
    const field = createClientSecretField();
    expect(field).toMatchObject({ id: "3", name: "clientSecret", type: "password" });
    expect(field.description).toContain("client secret");
  });

  it("createRedirectUrlField and createAudienceField build inputs", () => {
    expect(createRedirectUrlField()).toMatchObject({ id: "4", name: "redirectUrl", type: "input" });
    expect(createAudienceField()).toMatchObject({ id: "5", name: "audience", type: "input" });
  });
});

describe("createCommonOAuthFields", () => {
  it("returns the five standard OAuth fields in order", () => {
    const fields = createCommonOAuthFields();
    expect(fields.map((f) => f.name)).toEqual([
      "provider",
      "clientId",
      "clientSecret",
      "redirectUrl",
      "audience",
    ]);
  });

  it("threads per-field overrides through", () => {
    const fields = createCommonOAuthFields({
      clientId: { isDisabled: true },
      audience: { description: "aud" },
    });
    expect(fields[1].isDisabled).toBe(true);
    expect(fields[4].description).toBe("aud");
  });
});
