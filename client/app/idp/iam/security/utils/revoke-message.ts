import { tStatic } from "@seliseblocks/genesis-os/i18n";
import type { IRevokeSessionResponseApi } from "../api";

// The revoke endpoint answers 200 whether or not the session was still live, so the toast reads
// the result instead of assuming the device was signed out just now.
export const revokeResultMessage = (result: unknown): string =>
  (result as Partial<IRevokeSessionResponseApi> | null | undefined)?.alreadyRevoked
    ? tStatic("iam.security", "REVOKE_MESSAGE.ALREADY_SIGNED_OUT")
    : tStatic("iam.security", "REVOKE_MESSAGE.SIGNED_OUT");
