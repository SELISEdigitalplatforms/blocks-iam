import { getRuntimeEnv } from "@/lib/runtime-env";
enum Version {
  V1 = "v1",
}

// Check if using localhost to determine API path structure
const isLocalhost = getRuntimeEnv("BLOCKS_API_BASE_URL")?.includes("localhost");

export const API_BASES = {
  COMMUNICATION: isLocalhost ? "/api" : `/communication/${Version.V1}`,
  CLOUD_CONFIGURATION: isLocalhost ? "/api" : `/cloudconfiguration/${Version.V1}`,
  UDS: isLocalhost ? "/api" : `/uds/${Version.V1}`,
  UILM: isLocalhost ? "/api" : `/uilm/${Version.V1}`,
  UTILITIES: isLocalhost ? "/api" : `/utilities/${Version.V1}`,
  CLOUD_BUILD: isLocalhost ? "/api" : `/cloudbuild/${Version.V1}`,
  IDP: isLocalhost ? "/api" : `/idp/${Version.V1}`,
  IDENTIFIER: isLocalhost ? "/api" : `/identifier/${Version.V1}`,
  LMT: isLocalhost ? "/api" : `/lmt/${Version.V1}`,
  MFA: isLocalhost ? "/api" : `/mfa/${Version.V1}`,
  ALERT: isLocalhost ? "/api" : `/alert/${Version.V1}`,
  AI: isLocalhost ? "/api" : `/blocksai-api/${Version.V1}`,
  STUDIO: isLocalhost ? "/api" : `/studio/${Version.V1}`,
} as const;
