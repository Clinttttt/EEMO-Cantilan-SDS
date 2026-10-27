# V18 — Monthly SRC and capstone-paper alignment (2026-10-27)

## Reported production evidence and paper claim
The user-provided Head Portal screenshot shows a paid October 27, 2026 NCC stall-rent entry with a recorded physical receipt number, but no canonical Collection SRC. The Collection Activity drawer claimed that the payment was "recorded before SRCs", even though its date is current. The current `STALLTRACK_PAPERS_FINALS.docx` states that **each successfully posted collection** receives a StallTrack Reference Code and defines an SRC as a unique reference **when a digital Collection is successfully posted**. Neither an OR/CT serial nor an older PaymentRecord is itself evidence of a canonical Collection.

## Verified explanation
The server has two payment authorities:
- `Canonical`: an actual posted `Collection` has a persisted `ReferenceCode`, survives replay, and can be independently reviewed. Mobile and Head rent writers and online rent settlement post canonical Collections **only for eligible Canonical source rows**.
- `Legacy`: monthly `PaymentRecord` status and physical receipt are the recorded financial evidence; no canonical Collection/SRC is created. `CollectionActivity` deliberately includes this separate history so finance reports do not lose money. The date alone is not evidence that the record predates the SRC feature.

The already-paid illustrated legacy entry must NOT be backfilled or converted to a new paid Collection just to make it match the paper. This could duplicate revenue, change accountable receipt interpretation, and compromise reconciliation.

## Safe prospective remedy already implemented (ST-20)
The guarded `POST /api/rent-activations/plan` (no writes) and `POST /api/rent-activations` (separately authorized, attested) operate on one tenant/facility/month. The plan names new/Unpaid zero-cash eligible rows and excludes paid/partial/OR-bearing evidence; the activation requires the exact fingerprint, current policy, assigned-collector device versions, drained offline queues, clear reconciliation and online payment state, and trusted backup/restore readiness. It does not create money or an SRC itself. The first actual canonical collection creates the genuine SRC; late legacy payloads are quarantined for reconciliation rather than posted twice. NPM and sources outside this monthly rent policy have separate writer/activation rules. This is **not automatic tenant-wide activation**.

## Focused corrective change
Update the Collection Activity legacy detail to state "Not issued (legacy payment)" and that the original receipt evidence is preserved, without falsely claiming the current-dated transaction occurred before SRCs. Its mixed feed count is relabeled **Records**, not Collections; the subtitle makes clear that genuine posted collections and legacy payments coexist. The numeric count and collected total stay unchanged. This changes presentation only: no financial writer, balances, OR number, SRC, reporting authority, schema or tenant access.

## Verification on isolated branch
- New component assertion intentionally failed against old UI; final `CollectionActivityPageTests|CollectionReviewDrawerTests`: **24 passed, 0 failed, 0 skipped**.
- PostgreSQL 18 `RentActivation` filtered suite: **11 passed, 1 pre-existing explicitly opt-in skipped, 0 failed**. The one-click safe issuance demo independently reran two Head/Mobile examples (**2 passed, 0 failed**), saved under `C:\\dev\\stalltrack\\release-evidence\\STALLTRACK_SRC_ISSUANCE_SAFE_DEMO_LAST_RUN.log`. It verifies Mobile SRC issuance after activation, two distinct Head SRCs, tenant guard, no activation for paid legacy rows, and collector/offline evidence refusal.
- Production screenshot data was not inspected or mutated through a production database connection. No cash, receipts, activation, database migration or deployment was performed by the investigation.
- Authenticating as the municipality Head and reviewing the specific current facility/month plan + operator-backed queue/backups is required before any production activation.

## Honest demonstration
Show a genuine existing canonical Collection's SRC in Collection Activity. If demonstrating the prospective activation machinery, run the disposable PostgreSQL workflow, not a new real-money production transaction. A physical OR is independent accountability evidence. Do not assert that all historical legacy PaymentRecords were issued retroactive SRCs.
