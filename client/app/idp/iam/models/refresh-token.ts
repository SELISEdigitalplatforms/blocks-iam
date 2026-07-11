export interface IRefreshTokenRotation {
  fingerprint?: string;
  issuedUtc: string;
  absoluteExpiry: string;
  isRevoked: boolean;
  revokedAt?: string | null;
  revokeReason?: string | null;
  ipAddress?: string | null;
  userAgent?: string | null;
  isCurrent: boolean;
}
