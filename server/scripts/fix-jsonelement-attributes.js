// Clears the "_t": "JsonElement" placeholders left in Attributes bags by the permissive
// ObjectSerializer (see Iam.DomainService/Shared/Serialization/AttributeBagSerializer.cs).
//
//   mongosh "mongodb://<host>/<db>" --file scripts/fix-jsonelement-attributes.js
//
// The values those placeholders stood for were never written - only the discriminator was - so
// there is nothing to recover and the entries are removed outright. Reading them no longer throws
// once AttributeBagSerializer is deployed, which makes this script optional cleanup rather than a
// prerequisite; run it to stop the empty keys from showing up in API responses.
//
// Idempotent: re-running finds nothing to change. Run once per tenant database.

const COLLECTIONS = ["Organizations", "Users"];

function strip(value) {
  if (Array.isArray(value)) {
    return value.map(strip);
  }

  if (value === null || typeof value !== "object" || value instanceof Date) {
    return value;
  }

  // A bare discriminator with no siblings carries no data - drop the whole entry.
  const keys = Object.keys(value);
  if (keys.length === 1 && keys[0] === "_t") {
    return undefined;
  }

  const cleaned = {};
  for (const key of keys) {
    if (key === "_t") continue;
    const child = strip(value[key]);
    if (child !== undefined) cleaned[key] = child;
  }
  return cleaned;
}

for (const name of COLLECTIONS) {
  const coll = db.getCollection(name);
  let scanned = 0;
  let updated = 0;

  coll.find({ Attributes: { $ne: null } }, { Attributes: 1 }).forEach(function (doc) {
    scanned++;

    const before = JSON.stringify(doc.Attributes);
    const after = strip(doc.Attributes) || {};

    if (JSON.stringify(after) === before) return;

    coll.updateOne({ _id: doc._id }, { $set: { Attributes: after } });
    updated++;
  });

  print(name + ": scanned " + scanned + ", repaired " + updated);
}
