# Core Brain V5 — final execution handoff

Branch `codex/vegetable-fruit-space-workspace`. Local commits only; nothing pushed, merged, deployed or migrated.
Starting HEAD `3c5f0b52`. Eight protected files (Login, AuditTrail, four planning docs) untouched and unstaged.

## Commits
| Commit | Scope |
|---|---|
| `a2d6bd6e` | NPM partial-installment allocation, canonical settlement, migration `NpmPartialInstallments20261012` |
| `ce64c362` | Collection Manager part-payment preview and recording |
| `98b4cb5c` | Mobile Collection Session: NPM "Part payment" item |
| `620f8fd6` | Stage B (read-only): office-confirmed NPM link as navigation from a Fish/Meat vendor |
| `c0c085ab` | Read-only office back-entry reconciliation lookup |

## NPM partial installments (implemented, switched OFF by default)
- Model: each payment is its own Collection/SRC with one allocation per installment reached (existing `CollectionAllocation`, non-unique per source).
  `DailyCollection.CanonicalSettledAmount` (additive, default 0) is a projection of the net allocations; `IsPaid` only when it reaches the base fee.
  Voiding a Collection reverses exactly its own allocation amounts; other payments stay.
- Rules: oldest-first; only days payable today (never future, closed, excused, outside term); month ceiling holds (31-day month ≤ ₱900);
  more than owed is refused (no advance, no credit); the month-end adjustment is not an installment and is still collected whole through the existing whole payment.
- Safety: reviewed quote + fingerprint, idempotent replay, conflict on changed intent, `RECONCILIATION_REQUIRED` when the month changed,
  serializable transaction (two collectors paying the last ₱10: exactly one applies, the other gets a clean conflict).
  Whole-day paths (SettleNpmDays/Month, RecordDaily, bulk import, fish day) skip or refuse a part-paid day; `MarkPaid/MarkUnpaid/MarkAbsent` throw on one.
  Mobile collectors cannot correct a part payment (the office voids).
- Switch: `StallTrack:NpmPartPayments:Enabled` (default false). Only after canonical NPM activation (legacy rows carry one payment event).
- Reports patched to include part money: Official Monthly Income (already Collection-driven, verified), Collection Manager month DTO, Facility Overview revenue + breakdown.
  **Not yet audited for part days (why the switch is off):** CollectorRepository Mobile/Reporting, CollectorReportQueries, DashboardRepository, Facility Trends/Sections/Rates/Compliance/Fish,
  TransactionFeed, ClosedAccounts, PaymentRepository ledger/missing-receipts. They read `IsPaid` and would not show the part received. Audit them before enabling.
- Tests: 16 + 3 PostgreSQL part-payment tests, 3 Web component tests, 1 Mobile component test.

## Mobile
New Collection offers "Part payment" for a selected NPM occupancy (DirectAmount, server maximum), reviewed and recorded by the existing Review → Record flow; review needs a connection (as for every reviewed session). v1 fingerprints unchanged.
Charge-row visual refinements remain a separate follow-up. Android device acceptance pending. Windows runtime must be rebuilt (`dotnet build … -f net10.0-windows10.0.19041.0 -c Debug`) before checking: a stale Oct-8 Debug exe caused the "old UI" report.

## Office back-entry
Implemented: read-only lookup (`POST api/office-sources/back-entry/lookup`, Head/Admin) by ClientOperationId, SRC, or collector+date(+amount). "No server record" is not proof nothing was collected.
Not implemented (blocked on authority): posting. Needs recorded-by vs collected-by, original ClientOperationId, reason, evidence reference, device-side duplicate exclusion, role matrix, a Needs-Review state, and an approved instrument/receipt policy. No universal back-entry endpoint was created.

## Stage B
Implemented: navigation only (vendor → its confirmed, uncorrected, still-answering NPM stall as a separate session).
Mixed-source checkout stays disabled. Gate before enabling: per-item exact source identity, verified relationship scope, per-item collector assignment and eligibility, distinct canonical allocations, OR/CT treatment per item, atomic posting, idempotency, corrections, no duplicate Collection/SRC.

## Database
Migrations unapplied by me. `NpmOperatingCalendar20261011` and `NpmPartialInstallments20261012` are pending in source. **The API auto-migrates on startup (`DatabaseStartup.MigrateAndSeedAsync`)**, so the next API start against any database applies pending migrations.
Both are additive (new tables/column with default). Whether the local `EEMOCantilanSDS.DB` already has the calendar migration could not be verified (no connection string in the tool context; `pg_dump`/`psql` not installed; Docker present). Before starting the API against it: confirm it is development-only and take a backup.
No weekend effective date was seeded; the schedule is not activated.

## Pending decisions
Part-payment activation (after report audit); back-entry authority; mixed-source checkout approval; NPM advances (still refused); weekend schedule effective date; `OccupancyHistorySnapshotTests` / `RegisterAndFollowUpAgreementTests` need an isolated snapshot DB.

## Not production-ready
Part payments and back-entry are not accepted for production: report audit, device acceptance and owner review remain.
