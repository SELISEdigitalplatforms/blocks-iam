import { getRuntimeEnv, getSelfBaseUrl } from "@/lib/runtime-env";
import { HttpClient } from "@seliseblocks/genesis-os/lib";
import {
  createHttpFailureReporter,
  getRollbar,
} from "@seliseblocks/genesis-os/observability";
import { SERVICE_NAME } from "@/constants/service.constant";

// Only failures that never reached the server -- API unreachable, DNS, CORS, TLS. Anything with
// an HTTP status is left alone: a 4xx is a business outcome the UI already surfaces, and a 5xx is
// reported server-side with a real stack trace. Shared by both clients, since getRollbar is
// memoised and a second instance would split Rollbar's breadcrumb buffer.
const reportHttpFailure = createHttpFailureReporter(
  getRollbar({ service: SERVICE_NAME }),
);

export const serviceInstances = {
  logicService: new HttpClient({
    baseURL: getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") || "",
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
    onError: reportHttpFailure,
  }),
  idpService: new HttpClient({
    // IAM's own API is same-origin with this SPA. Resolved per request rather
    // than at module load, so nothing depends on script ordering.
    baseURL: () => getSelfBaseUrl(),
    blocksKey: getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
    onError: reportHttpFailure,
  }),
};

export { HttpClient };