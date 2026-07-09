# Codex Findings — Review & Fixes (2026-07-09)

Independent code review of Codex's audit findings, verified against the actual code
(not taken at face value), then fixed in priority order with regression tests, Cantilan
kept byte-for-byte, and small verified batches. Full test suite grew from 532 → 544.

## Commit trail (all on `master`, deployed except the last — see bottom)

| Commit | Scope |
|--------|-------|
| `c2487764` | #3 PayMongo webhook settles under the transaction's own LGU (multi-LGU) |
| `ed776d87` | #1 utility paid-amount lock + #4 cross-module OR uniqueness (utility↔collection) |
| `4233d305` | #2 online-payment contract-period validation + #5 collector online-OR authorization |
| `578ff51a` | broader OR-uniqueness (SLH/TPM/TRM also check utilities) + #8 webhook timestamp freshness |
| `0e6b4717` | shared `OrNumberRegistry` refactor + #9 mobile record period-occupant (deploy pending — GitHub runner hiccup; re-run `#132`) |

## Findings, verdicts, and fixes

### Fixed
- **#3 (High) PayMongo webhook tenant.** The anonymous webhook resolved to the default tenant
  (Cantilan), so the tenant query filter hid non-Cantilan transactions and never settled them.
  `OnlinePaymentRepository.GetByGatewayReferenceAsync` now bypasses the tenant filter (globally
  unique reference); a new scoped `IRequestTenantScope` pins the request to the transaction's own
  municipality so the record lookup, write-stamping, and cache invalidation run under the right LGU.
  Null override by default → Cantilan unchanged. Regression test added.
- **#1 (High) Utility paid amount.** A paid utility's amount derived from live consumption × rate,
  so editing readings after payment silently changed a receipted amount. `UtilityBill.WouldChangeSettledReadings`
  blocks editing an already-paid utility's readings (per-utility; the unpaid side stays editable).
- **#4 (Med/High) Cross-module OR uniqueness.** Utility ORs and collection ORs were checked in
  separate silos. Both checks now cross-reference each other; re-marking the same bill is preserved.
- **#2 (High, integrity) Online-payment period.** A payor could initiate payment for any year/month.
  `InitiateOnlinePayment` now requires the requested period to fall within a stall contract term.
- **#5 (Med) Collector online-OR auth.** The OR-issue endpoint allows collectors but never checked
  facility assignment. It now verifies the collector is assigned to the transaction's facility.
- **#8 (Low) Webhook replay.** Signature verification now rejects stale/future timestamps beyond a
  configurable window (`PayMongo:WebhookToleranceMinutes`, default 12h); reconciliation is the fallback.
- **Broader OR uniqueness.** SLH/TPM/TRM already cross-checked the five collection tables but not
  utilities; added that. Later unified all five repositories behind one shared `OrNumberRegistry`.
- **#9 (Low) Mobile record occupant.** The Records feed showed the stall's *current* lessee on a
  back-dated record. `GetCollectorRecordsAsync` now resolves the occupant whose contract covered the
  record's period (monthly = billing month, NPM daily = collection date).

### Verified already-safe (Codex over-flagged; no change needed)
- **#6 Mobile read-cache scoping.** `MobileSessionService` already clears the read cache on both
  login and logout, so no cross-collector/cross-LGU stale reads. Write ops are owner-tagged.
- **#7 Utility offline idempotency.** Utility payments are a single combined offline operation with
  one `ClientOperationId`, deduped at the sync level (`IsOperationProcessedAsync`); `RecordPayment`
  is absolute-state (idempotent). No separate elec/water ops exist.

### Left as decisions / follow-ups
- **#10** No unique index on `CollectorFacilityAssignment` — a business decision (enforce
  "one facility = one collector" or not); duplicate-collection risk is blunted by per-op idempotency.
