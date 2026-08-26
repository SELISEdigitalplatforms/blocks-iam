import { getRuntimeEnv, getSelfBaseUrl } from "@/lib/runtime-env";
import { HttpClient } from "@seliseblocks/genesis-os/lib";

export const serviceInstances = {
  logicService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
  }),
  idpService: new HttpClient({
    // IAM's own API is same-origin with this SPA. Resolved per request rather
    // than at module load, so nothing depends on script ordering.
    baseURL: () => getSelfBaseUrl(),
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
  }),
};

export { HttpClient };