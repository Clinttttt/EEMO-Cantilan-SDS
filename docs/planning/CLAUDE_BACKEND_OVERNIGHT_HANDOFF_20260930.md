# Claude Backend Overnight Handoff — 2026-09-30

Branch `backend/claude-gap-completion-v3`, worktree `claude-backend-v3`, started at `2d42a39a`. Gap ledger:
[CLAUDE_BACKEND_GAP_AUDIT_20260930.md](CLAUDE_BACKEND_GAP_AUDIT_20260930.md). Per-slice detail (scope, invariants,
tests, remaining gates) is in the checkpoints appended to
[BACKEND_OPERATIONAL_COMPLETION_HANDOFF_20260929.md](BACKEND_OPERATIONAL_COMPLETION_HANDOFF_20260929.md).

## Commits

| # | Hash | Scope | Tests |
|---|---|---|---|
| 1 | `c8e6dda3` | `CLAUDE.md` + `.claude/skills/` (backend engineering, financial safety, backend review) | docs |
| 2 | `3a504ff4` | Gap audit ledger | docs |
| 3 | `fe4eb060` | fix(wcf): new Web-origin WCF posting durably rejected; historical `WebWcf` outcomes replay | PG WCF/settlement/assignment 23/23 |
| 4 | `ffcd44e7` | test: baseline EF allow-list gap for `CollectorOperationAssignmentWorkflow.cs` | boundary test |
| 5 | `400f4d53` | feat(reports): canonical Monthly Income reader (AsOf/LatestCorrected, canonical-only) | unit 9 + PG 1 |
| 6 | `ce63ff8c` | feat(collectors): operation-only collector create/update, atomic | unit 13 new (155 collector) + PG 2 |
| 7 | `ca5fd3fc` | first (partial) handoff | docs |
| 8 | `0d004783` | feat(mobile): collector operation capability query (assigned vs collectible) | unit 12 + PG WCF class 23/23 |
| 9 | `63b59949` | feat(npm): Weight & Measure shadow reconciliation (frozen Meat only; Fish unresolved) | unit 4 + PG 1 |
| 10 | this commit | final handoff and ledger updates | docs |

Regression proof (the test fails with the defect reintroduced, then passes once the fix is restored) was run for
commits 3, 5, 6, 8 and 9. No migrations; no model changes.

## Validation

- Full unit suite (`EEMOCantilanSDS.UnitTest`, Release): 2276 total, 2274 passed, **2 failed**. Both failures are
  `SectionNamesComeFromTheOfficeTests` failing on pre-existing ECF/WCF Razor pages. This branch changed no UI file, so
  this is a Claude UI follow-up (CB-27).
- PostgreSQL/Testcontainers: focused classes only (WCF workflow 23/23, canonical income 1/1, operation-only
  collectors 1/1 plus assignment persistence 1/1, weighing shadow 1/1). The full integration suite was not run.
- Component tests: not run (no UI changes).
- API Release build 0 errors; Client Release build 0 errors (after CB-13). `git diff --check` clean for each commit.
- `dotnet ef migrations has-pending-model-changes`: not needed (no entity or configuration changes).

## Source readiness (after this run)

| Source | State |
|---|---|
| WCF | Mobile-only canonical posting; Web posting retired; source still Legacy (activation needs Q43 cutover). Mobile capability reports `PendingCutover` today. |
| Market Fees | Assignment only; no source model or writer on the accepted baseline; capability reports `Unsupported`. |
| Vegetable/Fruit | Contextual OR/CT policy only; no source or writer; `Unsupported`. |
| Landing/Berthing | Assignment only; no source model on baseline; `Unsupported`. |
| Transfer Large Cattle | Assignment only; no configurable-service model and no classification code; `Unsupported`. |
| Fish/Meat Vendor Fee | BLOCKED BUSINESS RULE: no distinct source or rate; see U-1. |
| Weight & Measure | Shadow comparison available; Meat frozen, Fish unresolved; real adapter blocked on custody/cutover. |
| Tabo | OR policy from 2026-09-27; TPM shadow only. |
| Accountable Forms | CT custody foundation complete; return, void, remittance and RCD blocked on policy. |

## Unresolved questions

- **U-1:** Is the Fish/Meat Vendor Fee the same money as the NPM Fish/Meat section daily stall fee (₱30/day,
  ₱900/month) — i.e. a reporting classification of it — or an additional charge? What is its rate source?
- **CB-26:** Should the WCF operation alone authorize posting against NPM-bound Water sources (IA-048), or keep the
  NPM facility requirement?
- **CB-03:** Update IA-029 (Web+Mobile WCF target) to match the Mobile-only direction.
- **CB-05 / CB-07:** official cross-period RCD treatment; void/replacement approval policy for a correction writer.
- **CB-02:** when to quiesce the legacy Web cumulative Water writer (part of the scoped Water cutover).
- Future Fish weighing: should the rate and amount be frozen per collection like Meat? That is a source change needing approval.

## Frontend / Mobile follow-ups

- Remove the Web "Collect with CT" action (it now returns a conflict).
- Collectors page: send `OperationCodes` on create/update and allow an empty facility list when operations are chosen.
- Fix the two failing `SectionNamesComeFromTheOfficeTests` pages (CB-27).
- Collector Mobile: consume `GET api/Mobile/operations/capabilities` (and a typed client); needs a signed APK.
- Optional: a Reports page for `GET api/canonical-reports/monthly-income` once Core Brain approves.

## Documentation corrections

- The earlier readiness matrices describe Landing/Berthing activity, Market Fees collection points and governed
  configurable services. Those exist only on the unaccepted `ui-completion` branch.
- The Phase 5A note that onboarding reaches `Municipality.Create` is stale (CB-25 closed, no defect).

## Production effects

No push. No merge. No deployment. No production migration. No production-data rewrite. No backfill. No source
activation/cutover. No APK release.
