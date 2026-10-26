# ST-21: optional Space Rental receipt reference (2026-10-26)

## User requirement and authority separation
On Mobile Space Rental New Collection, the typed number is OPTIONAL descriptive reference information. It is not a request to issue, register, assign, or consume an accountable OR. It must not make a collectible charge fail solely because no OR stock was registered.

The existing formal accountable-document OR path remains unchanged for callers that explicitly request it. A free-text reference must never be silently elevated to a verified issued receipt.

## Local branch
fix/st21-optional-receipt-reference-20261026 from EEMO 3ffc06f3, isolated worktree C:\dev\stalltrack\eemo-st21-optional-reference.

## Behavior
- Mobile sends CollectionSessionIntent.ReceiptReference, not OfficialReceiptNumber, for optional Space Rental inputs.
- Typed reference groups months of one liable account into one canonical Collection/SRC with separate period allocations. Blank reference keeps existing independent monthly SRC behavior.
- An entered reference is trimmed (up to 100 chars), saved on immutable Collection.ReceiptReference, and shown in Head activity/register/detail as "Entered reference (unverified): ..." when no registered document exists.
- The request never touches AccountableDocument custody/state, RAAF/OR stock, remittance, source rates or old collections.
- Registered OfficialReceiptNumber API requests retain existing custody checks and consumption behavior; the two modes cannot be combined.
- Server still checks collector tenant/assignment, active policy, monthly balance, amount limits, per-month allocations, quote freshness, session idempotency and same-account grouping.

## Migration
20261026093730_CollectionReceiptReference20261026 adds ONLY nullable varchar(100) Collections.ReceiptReference and updates the EF model. This migration is NOT applied to any live or production database. Coordinate its order with other pending additive migrations before deploying.

## Evidence
- EF migration generated successfully (API build succeeds).
- New PostgreSQL integration test passes for unregistered number 365363, amount/period allocations, no document consumption, persisted number, Head activity display and idempotent replay.
- Mobile Windows build passes; warnings from existing package dependencies and code remain.
- Existing registered-OR and blank-reference regression results need to be recorded after the running test suite finishes.

## Release safety
Do not integrate into an actively changing release candidate or deploy API/Head/Mobile until the pending agents finish and migration order is reviewed. The new client intent requires updated API first. Signed Android APK and real device smoke remain separate gates. No push, production migration, source cutover or deployment authorized.
## Final test results
- New optional-reference Testcontainers PostgreSQL integration: 1/1 passed (fresh database).
- Existing ST-21 PostgreSQL registered-OR/no-OR regression: 6/6 passed.
- Mobile Windows Debug build: passed, 0 errors (24 warnings).
- EF migration generated and API compiled successfully. No production/scratch DB migrated by this change.
