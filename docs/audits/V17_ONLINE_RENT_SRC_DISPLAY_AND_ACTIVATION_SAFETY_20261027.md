# V17 Online Stall Rent SRC — read-only payor visibility and guarded activation

## Scope and evidence

- The current `OnlineRentCanonicalPoster` already posts a real canonical Collection with an SRC in the same `SaveChanges` as paid gateway settlement, using `OnlineRentCanonicalPoster.OperationIdFor(tx.Id)` for deterministic idempotency.
- Existing `EcfCollectionWorkflowTests.OnlineRent` tests cover canonical payment, duplicate webhook replay, webhook-versus-confirm concurrency, wrong-tenant source, concurrent collector settlement and amounts changed after checkout.
- When `PaymentRecord.SettlementAuthorityState` is `Legacy`, `OnlinePaymentSettlementService` intentionally records its legacy payment status without posting canonical Collection or SRC. Previously paid/OR-bearing legacy money must not be relabeled or silently backfilled. This includes the illustrated NCC ₱1.00 row.
- ST-20's `RentActivationWorkflow` plan/attested activate sequence is already implemented for prospective **eligible, unpaid** facility-months. It requires approved rate/collector device and offline queue evidence and a reviewed plan fingerprint; activating an already-paid month is refused.

## Implemented safe changes

- Append an optional `PaymentHistoryDto.SrcReferences` collection. No schema, money authority or existing API field is changed.
- For non-NPM monthly rent records, the read-only ledger fetches persisted `Collections.ReferenceCode` through `CollectionAllocations` (PaymentRecord SourceKind) and tenant-equal `CollectionLines` and `Collections` joins, with each EF municipality filter active. Only rows whose authority is Canonical participate. Query handles multiple SRCs on a partially settled month and de-duplicates repeated references.
- The Payor portal's existing receipt-style payment details conditionally show every exact persisted SRC, separately from office physical OR. A legacy month with no SRC displays only its existing OR/pending message; no derived string or fake reference.
- Regression tests were added to the established PostgreSQL online payment workflow asserting the real Collection SRC is returned to Payor History and legacy-paid rows return none. The existing Payor History test suite now covers multiple SRCs and the separation from physical OR.

## Consequential activation withheld

The screenshot payment was already recorded; no production update or retroactive SRC issuance is authorized. Even for future payments, automatically switching authority at PayMongo checkout would bypass ST-20 queue/collector evidence and introduce a race with pending Mobile offline collections.

Before enabling canonical SRC for further months in a municipality, operators must:

1. Verify the protected database backup/restore gate, webhook reconciliation status, device version, and drained legacy offline queues.
2. Use `POST /api/rent-activations/plan` on the specific tenant/facility/current month and inspect every blocker; never infer ready from the screenshot or assume other LGUs share the same rates.
3. Attest the evidence and confirm the plan fingerprint, then use the existing `POST /api/rent-activations` operation only for eligible fresh rows; payor identities, balances and all prior OR-bearing legacy rows remain untouched.
4. In an isolated test environment validate PayMongo callback/return racing, double callbacks, cross-channel collections, an unavailable FCM sender, partial collections, no automatic OR issuance, and receipts/figures in Collection Activity, Remittance and Payor History.
5. After separately authorized, controlled production activation, monitor reconciliation exceptions and posted Collections. A plan alone does not change authority or issue SRCs.

## Verification performed

- PostgreSQL 18 integration tests (canonical paid online SRC -> Payor History; Legacy paid -> no fabricated SRC): **2/2 passed**.
- Existing gateway webhook/confirmation race, retry, amount mismatch, and tenant-conflict regression set: **4/4 passed on final combined rerun**. The first combined run was **3 passed, 1 intermittent failure** in `Concurrent_webhook_and_confirm_post_one_Collection`; the failing case passed in a separate isolated rerun (1/1) and the final combined rerun passed (4/4). No test assertion or financial production logic was weakened or edited; the intermittent concurrency result should remain visible to the release reviewer.
- Platform Payor Vitest (includes canonical SRC and physical OR separation): **77/77 passed**. Payor production build: **passed**.
- No migrations, new DB objects, fake SRCs or changes to PayMongo settlement / authorization. The Payor frontend work is stacked on its still-open receipt-style History PR, so the frontend release requires that prerequisite to be reviewed and merged first.

No ST-20 activation, database migration, production deployment, or payment mutation is part of this UI/read-model patch.
