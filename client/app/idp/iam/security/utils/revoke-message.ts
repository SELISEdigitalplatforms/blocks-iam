import type { IRevokeSessionResponseApi } from "../api";

// The revoke endpoint answers 200 whether or not the session was still live, so the toast reads
// the result instead of assuming the device was signed out just now.
export const revokeResultMessage = (result: unknown): string =>
  (result as Partial<IRevokeSessionResponseApi> | null | undefined)?.alreadyRevoked
    ? "This device was already signed out"
    : "Device signed out successfully";
