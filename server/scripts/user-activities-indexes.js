// Run this script once per environment to create the UserActivities indexes.
// Idempotent: createIndex is a no-op when an index of the same name already exists.
//
//   mongosh "mongodb://<host>/<db>" --file scripts/user-activities-indexes.js
//
// Notes:
// - The unique partial index on MessageId makes worker consumers idempotent against
//   broker re-deliveries. Background workers that synthesize events without a
//   MessageId (none today, but possible) are excluded via the partialFilterExpression.
// - The five indexes below align with the UserActivityRepository filter set
//   (UserId, SessionId, Category, Event) and the canonical history sort
//   (CreatedDate desc).
//
// Drop legacy collections AFTER verifying the new build is healthy in production
// (see task 8 in the plan):
//   db.UserTimelines.drop()
//   db.ResourceTimelines.drop()
//   db.UserAuthenticationTimelines.drop()
//   db.IdentityEvents.drop()
//   db.IdpAuditLogs.drop()

const coll = db.getCollection("UserActivities");

coll.createIndex(
  { TenantId: 1, UserId: 1, CreatedDate: -1 },
  { name: "ix_user_activity_user_history" }
);

coll.createIndex(
  { TenantId: 1, UserId: 1, SessionId: 1, CreatedDate: -1 },
  { name: "ix_user_activity_user_session" }
);

coll.createIndex(
  { TenantId: 1, Category: 1, CreatedDate: -1 },
  { name: "ix_user_activity_category" }
);

coll.createIndex(
  { MessageId: 1 },
  {
    name: "ux_user_activity_message_id",
    unique: true,
    partialFilterExpression: { MessageId: { $type: "string" } }
  }
);

coll.createIndex(
  { TenantId: 1, Category: 1, Event: 1, CreatedDate: -1 },
  { name: "ix_user_activity_category_event" }
);

print("UserActivities indexes ensured:");
printjson(coll.getIndexes());