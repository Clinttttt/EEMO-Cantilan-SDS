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


---

## Follow-up (Cantilan first-run setup — platform-operator distinction)

**Symptom (found in prod):** navigating to the Cantilan portal did not redirect to the first-run
Head setup page even though Cantilan had no Head. `GET /api/setup/status` returned
`{"isSetupRequired":false}`.

**Root cause (verified against the prod DB, not assumed):** the admin-user table held three
SuperAdmins — `carrascal.head` (Carrascal), `madrid.head` (Madrid), and `clint`
(`IsPlatformOperator = true`, stamped to **Cantilan**). `clint` is the platform/console operator
for the Angular onboarding console — a *different identity* from the Cantilan LGU **Head**, but it
is a SuperAdmin under the default municipality, so the earlier default-scoped setup check counted
it and reported Cantilan's Head setup as already complete.

**Fix:** `SetupRepository.IsSuperAdminExistsAsync` now also excludes `IsPlatformOperator` when
deciding whether the default (Cantilan) LGU has a Head. The console operator and the LGU Head are
distinct roles; the created Head is a non-platform-operator SuperAdmin, so setup correctly flips to
complete only once the actual Head exists. Regression test extended (`AuthTenantTests`): a
Cantilan-stamped platform operator does **not** satisfy the Head check; a non-platform-operator
Cantilan Head does. Suite 546/546.

## Accepted follow-up — payor-stall link lifecycle (Codex, independently verified)

Codex accepted the seven fixes as valid and raised one new concern. **Verified as accurate:**
`PayorStallLink` is only `PayorUserId + StallId + MunicipalityId` — no `ContractId`, no period, no
active/inactive state (`Domain/Entities/Users/PayorStallLink.cs`). Links are created
(`AddStallLinkAsync`) and checked (`LinkExistsAsync`) but **never removed anywhere** in the codebase
(no `PayorStallLinks.Remove`/`RemoveRange` in any handler). The online-payment period guard
(`stall.Contracts.Any(c => c.OverlapsPeriod(...))`) proves the *stall* has a contract for the period
but not that the logged-in payor is the current occupant. So if a stall transfers to a new lessee,
the previous payor's link persists and could still initiate payment for the current/future period.

Severity in context: a real authorization/data-exposure gap — and since **online payments are live in
production**, it is not deferrable. The actual transfer path is `RenewStallContractCommandHandler`,
which terminates the outgoing contract and creates a new one for the incoming occupant; the payor
balance/payable queries (`GetLinkedStallsAsync`) return every linked stall with no occupant check, so
before the fix the outgoing occupant kept seeing and could pay the incoming occupant's dues.

**Fixed (implemented, not deferred):** on renewal/transfer, when the occupant actually changes
(case-insensitive, trimmed name comparison), the stall's payor→stall links are revoked
(`IPayorRepository.RemoveStallLinksAsync`), so the outgoing occupant's account can no longer view or
pay the incoming occupant's obligations; the incoming occupant re-links by activating a fresh code.
A same-occupant renewal keeps the link intact (no re-activation friction for genuine renewals).
`InitiateOnlinePayment` then rejects the stale payor via its existing `LinkExists` guard. Regression
tests added (occupant-changed → revoked; same occupant incl. case/whitespace → kept; missing stall →
no-op). Suite 547/547.

Residual (documented, low risk): editing an occupant name *in place* via the vendor-registry edit
form (`Contract.UpdateOccupant`, no new term) does not revoke links — that path is a data correction,
not a change of hands. Transfers should go through renewal (which creates a new term). Binding the
link to a `ContractId` would also cover the in-place-edit path but needs a schema migration + backfill;
deferred as a hardening follow-up since the primary transfer path is now closed.

Also noted (accepted, non-blocking): the utility paid-amount lock will eventually need a formal
correction/reversal workflow so admins can fix a legitimately wrong reading after payment; and the
12-hour webhook tolerance is fine for provider retries but could be tightened later.
