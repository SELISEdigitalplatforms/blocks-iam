import { getRuntimeEnv } from "@/lib/runtime-env";
import { getMergedAreaBaseUrl } from "@seliseblocks/genesis-os/lib";

export const BLOCKS_OS_BASE_URL = getRuntimeEnv("BLOCKS_OS_BASE_URL");
// Logic is served by the os host: BLOCKS_LOGIC_BASE_URL while set, else BLOCKS_OS_BASE_URL.
export const BLOCKS_LOGIC_BASE_URL = getMergedAreaBaseUrl("BLOCKS_LOGIC_BASE_URL");
