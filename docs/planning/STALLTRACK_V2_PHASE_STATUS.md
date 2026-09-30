# StallTrack V2 Phase Status

**Last updated:** 2026-09-28
**Canonical branch:** `interface-v2/clean-adoption`
**Purpose:** One concise status record for the approved StallTrack V2 itemized-collections/cutover phases and the presentation-safe UI completion track.

> This file records implementation status and release gates. It does not override the business rules in `docs/business/`, the decisions/ADRs in `docs/decisions/`, or the canonical V2 baseline in `docs/v2/ITEMIZED_COLLECTIONS_CANONICAL_IMPLEMENTATION_BASELINE.md`.

## 1. Status vocabulary

- **Implemented** — code is present on the canonical branch and passed the stated local validation.
- **Implemented / not activated** — the capability exists in code, but the relevant production source remains under Legacy settlement authority.
- **Blocked** — the next phase may not proceed until the stated gate is satisfied.
- **Presentation-safe** — UI/semantic work may proceed without changing settlement authority or activating real financial writers.
- **Cutover/release-safe** — requires the stronger database, writer, device, reconciliation, and reporting gates described below.

A phase being implemented does **not** mean a real EEMO source has been converted to Canonical authority.

## 2. Canonical implementation history

| Phase | Canonical commit(s) | Status | Primary result |
| --- | --- | --- | --- |
| Architecture baseline | `18d9f4465897e07d709c3096db55140fe047ca70` | Approved | Single-MASTER itemized-collections baseline and sequential implementation plan |
| Phase 1 — Dormant foundation | `ccb8bf62922bd04cffcad4141871ce6f8bfb597f` | Implemented / dormant | Payor, itemized Collection/Line/Allocation foundation, settlement-cutover evidence, versioned Web drafts, durable posting-operation identity, accountable documents, corrections |
| Phase 2 — Bounded ECF / OR path | `997263c5faa75ded7cb6c97f3ffce2ca9c669512` | Implemented / not activated | Electricity source adapter, OR-aware canonical posting, revisioned draft, allocation, document consumption, durable operation outcome, ECF activity |
| Phase 2 closeout — Philippine time | `1d8532ab13b990a71d1af0ffecfa5288da5c19d5` | Implemented | ECF active period/refresh and displayed timestamps aligned with Philippine time |
| Phase 3 — Shared monthly-rent Composer | `d31c81bc998f0aeee89dfb84e2ee42261135d1a0` | Implemented / not activated | Shared rent + ECF Current Collection, explicit period allocation, compatible multi-line OR, Payor-first discovery |
| Phase 3 closeout | `c22f17fe1df3f6a2ee4954ab6e30a8e13c4c2b2a` | Implemented | Single-allocation correction, draft-business-date rent revalidation, scoped activity, business-date refresh/review invalidation |
| Phase 4 — WCF / CT / Mobile | `73f38ab2d41b5ec3607251e0d517df3707f33449` | Implemented / not activated | Water-only WCF workflow, CT custody/assignment, Web + focused Mobile canonical writer, durable issued-ticket evidence, reconciliation-required handling, old-client/online guards |
| Phase 5A — Cutover control plane | `fdd3010208264c63c09d0762012abe0a6553d7d4` | Implemented as tooling/test boundary | Exact tenant/source readiness, Pending Cutover gate, opening-position freeze, controlled activation service, Mobile/online/document/report readiness evidence |
| Phase 5A closeout | `5a5d2a7934669a9c48349afc1c7ac1de87b8e86c` | Implemented | Activation now requires the current readiness fingerprint to match the fingerprint frozen at review |
| Business-rule checkpoint | `586bca2c` | Implemented documentation checkpoint | Latest EEMO Head rulings, Market grouping, Vegetable OR/CT resolver, ECF/WCF direction, Utility/NPM boundary, Transfer Large Cattle direction |
| Phase 5B — Canary review / source activation | `67d5d235379db30b47d9ca079c86f04690f4aec7` | **READY FOR EXPLICIT CANARY REVIEW** | Required PostgreSQL/Testcontainers suite ran successfully; source-specific writer, device, document, online-payment, reporting, and reconciliation gates still apply. No source is activated. |

## 3. Phase 1 — Dormant shared financial foundation

### Goal

Introduce the common target structures without replacing any existing specialized source as production settlement authority.

### Delivered

- stable tenant-owned business `Payor` identity independent of portal/login identity;
- canonical `Collection`, `CollectionLine`, explicit `CollectionAllocation`;
- accountable-document foundation;
- durable posting-operation/idempotency identity;
- versioned Web draft foundation;
- settlement-cutover evidence;
- correction/reversal foundations;
- additive persistence/migrations only.

### Boundary

No source was activated. Existing specialized sources remained authoritative.

### Validation recorded at completion

- Release solution build: 0 errors;
- 11 focused foundation tests passed;
- full unit/component suites had only the already-known unrelated architecture/UI failures reported at that checkpoint;
- database integration tests compiled but were skipped because Docker was unavailable.

## 4. Phase 2 — Bounded ECF / Official Receipt vertical path

### Goal

Prove one small end-to-end canonical source path before attempting broader collection migration.

### Delivered

- Electricity-only source adapter over `UtilityBill` Electricity facts;
- server-persisted/revisioned collection draft;
- explicit Electricity allocation;
- OR availability and consumption;
- durable posting-operation idempotency;
- canonical compatibility projection;
- initial source-backed Collection Activity;
- source-part isolation so Electricity cutover does not block Water legacy replay.

### Closeout

`1d8532ab` aligned ECF active period, refresh, and recorded-time presentation with Philippine time.

### Boundary

ECF remained `Legacy`; no live settlement-authority transition occurred.

## 5. Phase 3 — Shared monthly rent + ECF Current Collection

### Goal

Move from one-source proof to the shared Composer model required by the office's itemized same-OR workflow.

### Delivered

- shared `CollectionComposerWorkflow`;
- monthly-rent source adapter using rent-only assessment facts;
- explicit period allocations;
- one draft/review/posting protocol for compatible rent + ECF lines;
- Payor-first discovery through explicit tenant-scoped Payor relationships;
- atomic save of Collection, lines, allocations, document consumption, source projections, operation outcome, and draft state;
- optimistic settlement versioning.

### Closeout

`c22f17fe` corrected single-allocation updates, rent revalidation against the draft business date, ECF-scoped versus shared Activity, and explicit business-date refresh/review invalidation.

### Boundary

Rent and ECF remained `Legacy`; writer readiness and reconciliation were still required before cutover.

## 6. Phase 4 — WCF Cash Ticket + focused Mobile readiness

### Goal

Add the first CT-oriented canonical path while preserving physical document custody and offline evidence.

### Delivered

- Water-only WCF source workflow;
- WCF classification/instrument resolution;
- partial settlement with stale/excess allocation rejection;
- shared canonical Web/Mobile posting coordinator;
- one WCF line + one Water allocation + one CT per WCF Collection;
- Head/Admin CT book receipt, assignment, and custody inspection;
- collector use restricted to currently assigned units;
- versioned Mobile received-money intent;
- durable local issued-ticket + operation recording before capture is reported safe;
- non-discardable/reconciliation-required handling after physical issue;
- guards for old cumulative Mobile payloads and online callbacks;
- current-custody hardening so historical assignment alone is insufficient.

### Boundary

WCF, ECF, and rent remained `Legacy`. No APK release and no production cutover occurred.

## 7. Phase 5A — Controlled settlement cutover tooling

### Goal

Provide a source-scoped control plane that can prove readiness before any source becomes Canonical.

### State machine

```text
Legacy
  -> Pending Cutover
  -> reconcile / quiesce writers
  -> freeze opening position
  -> Canonical
```

There is no approved direct `Legacy -> Canonical` shortcut.

### Delivered

- exact tenant/source/source-part readiness evaluation;
- non-mutating readiness HTTP endpoint;
- operator evidence for writer quiescence, device queues, field-device inventory, online drain, document reconciliation, and reporting-path verification;
- server-detected blockers overriding operator attestation;
- opening assessment / legacy settled / outstanding freeze as evidence only;
- no Collection/revenue created from opening settlement;
- source-part-specific Water/Electricity handling;
- serializable transaction + optimistic source version protection;
- late-legacy submissions preserved as reconciliation evidence;
- report-readiness disclosure for legacy readers still not migrated.

Detailed runbook: [PHASE5A_SETTLEMENT_CUTOVER_CONTROL_PLANE.md](PHASE5A_SETTLEMENT_CUTOVER_CONTROL_PLANE.md).

## 8. Phase 5A closeout — frozen readiness binding

The closeout commit `5a5d2a7934669a9c48349afc1c7ac1de87b8e86c` closes a material review gap.

At activation, StallTrack now requires all of the following to remain valid:

- exact source is still `PendingCutover`;
- the cutover record exists and matches the expected source boundary version;
- frozen reconciliation evidence is readable and complete;
- readiness re-evaluation is still `Ready`;
- **current readiness fingerprint equals the fingerprint frozen at review**;
- opening assessment is unchanged;
- opening legacy-settled evidence is unchanged;
- opening outstanding is unchanged.

The readiness fingerprint includes readiness-relevant evidence such as effective policy and active accountable-document identities/custodians. Source version remains a separate guard because the freeze action itself advances the boundary token.

The regression test intentionally changes CT custody after freeze while keeping the Water source version and opening money unchanged. Expected result:

- activation fails;
- Water remains `PendingCutover`;
- Electricity remains independently `Legacy`;
- no `Collection`, `CollectionLine`, or `CollectionAllocation` is created.

This is the correct fail-closed behavior.

## 9. Phase 5B gate

**Status: READY FOR EXPLICIT CANARY REVIEW. No real source activation is approved by this status.**

The prior database-concurrency blocker is resolved. These Release-mode runs executed against PostgreSQL through Testcontainers:

- readiness-after-freeze regression: 1 passed, 0 failed, 0 skipped;
- `WcfCollectionWorkflowTests`: 17 passed, 0 failed, 0 skipped;
- `EcfCollectionWorkflowTests`: 16 passed, 0 failed, 0 skipped;
- full integration project: 101 passed, 0 failed, 7 skipped (108 total);
- Release solution build: succeeded, 0 errors, 179 warnings;
- `git diff --check`: passed.

The seven skips are the existing opt-in historical snapshot tests requiring `STALLTRACK_SNAPSHOT_DB` (five `OccupancyHistorySnapshotTests` and two `RegisterAndFollowUpAgreementTests`). No Docker-unavailable skips occurred. The tested integration commit is `67d5d235379db30b47d9ca079c86f04690f4aec7`.

Before any activation, the separately selected source must still satisfy every applicable gate:

1. identify the exact tenant and source identity (for UtilityBill, exact bill ID and Electricity or Water part);
2. obtain a fresh readiness result and complete source-scoped reconciliation, then quiesce or canonically route every writer for that source;
3. verify every affected collector/device's actual compatible application and payload capability, and drain old queued operations;
4. drain/reconcile in-flight online/provider transactions, including requests not yet durably represented by a transaction row;
5. reconcile accountable-document custody and all issued, consumed, voided, or reconciliation-required units without returning a physical document to inventory;
6. verify Collection Activity, classification totals, and the applicable report path do not count compatibility projections as receipts;
7. preserve unresolved Office policy, including IA-043 cross-period RCD treatment, without inventing a reporting rule;
8. obtain Clint's separate, explicit approval for the named canary source and activation action.

No activation command was run against real data. Readiness status is not activation authority.

The PostgreSQL fixture review also confirmed that `Municipality.Code` is mapped with a 30-character maximum while `Municipality.Create` does not fail fast on longer values. The production onboarding path calls this factory, and its command validator currently checks only that the code is non-empty. A separate small fail-fast domain/onboarding validation change is warranted; this integration did not change domain behavior or widen the schema.

## 10. Validation history and current interpretation

Earlier phase checkpoints recorded their own build/test results. The current Phase 5B integration now has successful PostgreSQL runtime evidence; the seven snapshot tests remain opt-in because they require a separate historical snapshot database.

The important current interpretation is:

- the required PostgreSQL concurrency and transaction suite passed in this canonical checkout;
- the seven `STALLTRACK_SNAPSHOT_DB` skips are unrelated to the cutover database gate and are not reported as passes;
- implementation readiness and production cutover readiness are separate states.

## 11. Production/release status

As of this checkpoint:

- no real rent source has been activated;
- no real ECF source has been activated;
- no real WCF source has been activated;
- no production settlement authority has moved to Canonical;
- no Phase 5B activation has occurred;
- no production deployment was performed by these phases;
- no collector APK was released by these phases;
- N/O/P/Q remain paused and untouched as implementation worktrees.

## 12. Parallel presentation/UI completion track

Phase 5B does **not** block truthful V2 interface work.

The UI completion track may proceed while financial activation remains locked.

### U0 — Business/documentation checkpoint

**Status: COMPLETE**
Commit: `586bca2c`

Locks the latest EEMO rulings and V2 presentation basis before UI implementation.

### U1 — Shell/navigation truthfulness

**Status: NEXT**

Scope:

- preserve the accepted Blazor visual system;
- correct route ownership and misleading navigation;
- correct Overview recent-activity destination;
- keep Current Collection contextual/resumable;
- hide inaccessible admin-only destinations instead of advertising dead navigation;
- no financial-authority change.

### U2 — Operations V2 directory

**Status: NEXT after/with U1 as one bounded presentation slice**

Target work-oriented groups:

- Configured Facilities;
- Market Services;
- Space Operations;
- Utility Operations;
- Other Source Operations.

Preserve the existing StallTrack navy/gold/neutral visual language. Correct business semantics and capability/readiness states; do not create a new design system.

### U3+ — Later controlled UI slices

After U1/U2 visual approval:

- truthfulness/static-demo gating;
- ECF/WCF latest Head-rule alignment;
- missing source-operation workspaces;
- Collection Activity / Payor integration;
- focused Mobile semantic alignment;
- report/print and responsive QA.

These UI slices must not silently activate a Legacy financial source.

## 13. Relationship between the two tracks

```text
ITEMIZED / CUTOVER TRACK
Phase 1 -> Phase 2 -> Phase 3 -> Phase 4 -> Phase 5A -> [Phase 5B READY FOR EXPLICIT CANARY REVIEW]

UI COMPLETION TRACK
U0 docs -> U1 shell/nav -> U2 Operations -> U3+ bounded UI slices -> visual QA

Phase 5B is NOT a prerequisite for U1/U2/U3 presentation-safe work.
Phase 5B IS a prerequisite for claiming real canonical settlement activation.
```

## 14. Next action

Proceed with the accepted read-only Sol High UI audit as the implementation baseline, corrected by the newer `586bca2c` business checkpoint.

Preserved audit handoff: [`SOL_HIGH_UI_AUDIT_BASELINE.md`](SOL_HIGH_UI_AUDIT_BASELINE.md).

The next implementation slice is **U1 + U2 only**. Build/test/visual-review that bounded slice before broad page-by-page UI changes.

[executed on device: DESKTOP-93OK679 (11330eea-ecbf-4333-8834-60d02bbefb3e)]
