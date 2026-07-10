// One-shot backfill: derive IssuedUtc on legacy IdpRefreshTokens rows.
//
// Background:
// RefreshTokenModel did not previously persist an explicit IssuedUtc timestamp.
// New writes (post-deploy) populate it exactly. Existing rows can be backfilled
// heuristically as IssuedUtc = AbsoluteExpiry - 60 days (the default
// AbsoluteRefreshTokenValidForNumberMinutes lifetime in IdentityConfiguration).
//
// This script is idempotent: it only touches rows that do not yet have
// IssuedUtc. Re-running it is a no-op.
//
// Run once per environment after the model change ships:
//   mongosh "$MONGODB_URI" --quiet backfill-refresh-token-issued-utc.js

const ASSUMED_LIFETIME_MINUTES = 60 * 24 * 60; // 60 days
const ASSUMED_LIFETIME_MS = ASSUMED_LIFETIME_MINUTES * 60 * 1000;

let updated = 0;
let skipped = 0;
let missingExpiry = 0;

db.IdpRefreshTokens.find({ IssuedUtc: { $exists: false } }).forEach((doc) => {
  if (!doc.AbsoluteExpiry) {
    missingExpiry += 1;
    return;
  }
  const derived = new Date(doc.AbsoluteExpiry.getTime() - ASSUMED_LIFETIME_MS);
  db.IdpRefreshTokens.updateOne(
    { _id: doc._id },
    { $set: { IssuedUtc: derived } },
  );
  updated += 1;
});

const alreadySet = db.IdpRefreshTokens.countDocuments({ IssuedUtc: { $exists: true } });
const total = db.IdpRefreshTokens.countDocuments({});

print(`backfill-refresh-token-issued-utc: updated=${updated} skipped=${skipped} missingExpiry=${missingExpiry} alreadySet=${alreadySet} total=${total}`);
