import { describe, expect, it } from "vitest";
import {
  storageConfigurationFormSchema,
  storageConfigurationFormDefaultValue,
} from "./utils";

const base = {
  name: "my-storage",
  secretKey: null,
  accessKey: null,
  cloudStorageRegionEndPoint: null,
  connectionString: null,
  host: null,
  port: null,
  userName: null,
  password: null,
  remoteBasePath: null,
};

describe("storageConfigurationFormDefaultValue", () => {
  it("defaults to the Amazon strategy with empty credentials", () => {
    expect(storageConfigurationFormDefaultValue.storageStrategy).toBe("Amazon");
    expect(storageConfigurationFormDefaultValue.name).toBe("");
  });
});

describe("storageConfigurationFormSchema", () => {
  it("requires a name", () => {
    const result = storageConfigurationFormSchema.safeParse({
      ...base,
      name: "",
      storageStrategy: "Amazon",
      secretKey: "sk",
      accessKey: "ak",
      cloudStorageRegionEndPoint: "us-east-1",
    });
    expect(result.success).toBe(false);
  });

  it("accepts a complete Amazon configuration", () => {
    const result = storageConfigurationFormSchema.safeParse({
      ...base,
      storageStrategy: "Amazon",
      secretKey: "sk",
      accessKey: "ak",
      cloudStorageRegionEndPoint: "us-east-1",
    });
    expect(result.success).toBe(true);
  });

  it("flags missing Amazon credentials", () => {
    const result = storageConfigurationFormSchema.safeParse({
      ...base,
      storageStrategy: "Amazon",
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      const messages = result.error.issues.map((i) => i.message);
      expect(messages).toContain("Secret key is required");
      expect(messages).toContain("Access key is required");
      expect(messages).toContain("Region endpoint is required");
    }
  });

  it("requires a connection string for Azure", () => {
    const result = storageConfigurationFormSchema.safeParse({
      ...base,
      storageStrategy: "Azure",
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.map((i) => i.message)).toContain(
        "Connection string is required",
      );
    }
  });

  it("requires host, port, credentials and base path for SFTP", () => {
    const result = storageConfigurationFormSchema.safeParse({
      ...base,
      storageStrategy: "SftpStorage",
      port: "22",
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      const messages = result.error.issues.map((i) => i.message);
      expect(messages).toContain("Host is required");
      expect(messages).toContain("Username is required");
      expect(messages).toContain("Password is required");
      expect(messages).toContain("Remote base path is required");
    }
  });

  it("requires access key, secret key and host for S3-compatible storage", () => {
    const result = storageConfigurationFormSchema.safeParse({
      ...base,
      storageStrategy: "S3Compatible",
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      const messages = result.error.issues.map((i) => i.message);
      expect(messages).toContain("Access key is required");
      expect(messages).toContain("Secret key is required");
      expect(messages).toContain("Host URL is required");
    }
  });

  it("rejects an unknown storage strategy", () => {
    const result = storageConfigurationFormSchema.safeParse({
      ...base,
      storageStrategy: "Unknown",
    });
    expect(result.success).toBe(false);
  });
});
