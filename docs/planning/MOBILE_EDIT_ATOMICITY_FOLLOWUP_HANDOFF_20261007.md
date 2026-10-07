# Mobile edit atomicity closure — 2026-10-07

Base: `ace5e276`; branch: `codex/mobile-edit-atomicity-followup`.
Backend-only correction. No changed request/response DTOs, routes, source rules, UI, or schema.

## Root cause and fix

Production DI registered `AppDbContext` with `AddDbContext`, then registered
`IAppDbContext, AppDbContext` independently. One request therefore obtained two
scoped contexts. The collection-session store/source dispatcher used the concrete
context; the mobile correction workflow used the independently constructed
interface context.

The store's Serializable transaction surrounded only the concrete context.
Reversal `SaveChanges` on the interface context committed independently. The
source writer posted its replacement inside the concrete transaction. Reloading
that uncommitted replacement from the interface context failed with `SingleAsync`;
the store rolled back the replacement but could not roll back the already committed
reversal. Hand-built integration fixtures supplied one context to all participants,
so they did not exercise this production composition defect.

`AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>())` now aliases
the existing context. Reversal, allocation/source changes, replacement/SRC,
replacement link and correction PostingOperation share the store's one Serializable
transaction. Any exception disposes/rolls back that transaction. Quote-edit uses the
same scoped context inside its temporary transaction; disposal rolls preview
changes back. No exception suppression or missing-replacement workaround was added.

## Actual API proof

`CollectorApiHost` uses real API/Application/Infrastructure registrations, real
JWT authentication and controllers/middleware, and throwaway migrated PostgreSQL.
TestServer replaces only the HTTP transport. No source writer, repository,
current-user service or transaction is mocked. Production startup migration and
backup jobs are not invoked; configuration contains only scratch DB/JWT settings
and an unreachable payment-provider placeholder.

Before the fix, actual HTTP edits for Terminal, walk-up Vendor Fee and registered
Weight & Measure all returned 500. A failure injected after replacement save left
a committed `CollectionCorrection` of -100, proving the financial defect. The
same regression is also run with the old DI registration deliberately restored,
then the alias is restored before final validation.

Success tests assert original amount/SRC retained, explicit reversal, linked new
Collection/new SRC, corrected recent-history amounts, and same-outcome retry.
Failure test throws after the real canonical writer's save and asserts no committed
reversal, replacement, replacement line or successful PostingOperation; the original
remains fully effective and the same intent can subsequently succeed.

## Frontend contract — unchanged

| Verb / route | Request | Response / behavior |
|---|---|---|
| GET `/api/mobile/collections/recent` | none | `MobileRecentCollections`; each row has canonical activity, capabilities and typed edit source |
| POST `/api/mobile/collections/edit/quote` | `EditMobileCollectionIntent` | `CollectionSessionQuote`; only proceed when `CanRecord` with its `QuoteFingerprint` |
| POST `/api/mobile/collections/edit` | `RecordMobileCollectionEditRequest` | `MobileCollectionCorrectionResult`; replacement includes its new Collection ID/SRC |
| POST `/api/mobile/collections/remove` | `RemoveMobileCollectionRequest` | existing audited reversal contract |

Keep the same `ClientOperationId`, replacement session/item IDs and reviewed
fingerprint on unknown-response retry. Same intent replays the original successful
outcome; changed intent conflicts. HTTP 500 is still an unexpected error, but cannot
leave this edit half committed. Do not implement a client-side remove-then-record
workaround. Current-day/ownership/unremitted/canonical/source-boundary checks remain.
Rate-owned replacements still re-quote through the source writer.

## NPM discovery and source-native audit

HTTP `POST /api/mobile/collection-session/source-eligible` accepts
`CollectionSourceIdentity(SourceIdentityKind.Occupancy, Contract.Id)`.
For a current unpaid canonical NPM occupancy, `Operations` independently contains
`NpmDaily` and `NpmWholePayment` when eligible. Daily choice has stable stall/occupancy
identity, `FixedAmount`, server-owned `ServerAmount`, and the response `BusinessDate`.
Use that amount/date in `SessionNpmDailyIntent` and the normal session quote/record
contracts. Do not compute a daily charge from monthly rent or hard-code 30 pesos.
After daily record, Daily is absent while Whole remains when a balance remains.

The new HTTP NPM fixture has no Payor rows and no Contract.PayorId. It discovers,
posts and edits Daily successfully, preserving its server charge and daily settlement
projection. An injected replacement-save failure preserves the original paid daily
row, its canonical Collection link and the Whole remaining balance; retry succeeds.
No NPM domain change was needed. A source showing only Whole can be
correct when today's Daily is already settled or otherwise ineligible.

Bounded audit: NPM Daily/Whole use occupancy/stall authority; Vendor Fee uses explicit
registration or permitted walk-up PayerSnapshot; Weight & Measure requires an active
registration; Terminal uses section/optional vehicle facts and optional snapshot.
None of those target paths requires BusinessPayor or performs name matching.
Compatibility Payor tables and legacy discovery endpoints remain intact; no destructive
cleanup or migration is part of this task.

## Changed files

- `EEMOCantilanSDS.Infrastructure/DependencyInjection.cs`: shared context alias.
- `EEMOCantilanSDS.IntegrationTests/CollectorApiHost.cs`: production composition HTTP harness.
- `EEMOCantilanSDS.IntegrationTests/CollectionSessionTests.ApiAtomicity.cs`: successful edits/replay and post-save failure rollback.
- `EEMOCantilanSDS.IntegrationTests/NpmDailyCanonicalTests.Api.cs`: HTTP NPM discovery/post/edit proof without Payor.
- `EEMOCantilanSDS.IntegrationTests/PostgresFixture.cs`: internal scratch connection for host.
- `EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj`: API reference and TestHost.
- This handoff.

## Validation

- Full Unit: 2,519 passed, zero failures/skips.
- Focused collection-session/NPM PostgreSQL slice: 94 passed, zero failures/skips.
- Final HTTP/API regression slice, including the additional NPM settlement rollback
  assertions: five passed, zero failures/skips.
- Full Integration: 362 passed, zero failures, seven skips (369 total). Skips are
  existing local-snapshot tests requiring `STALLTRACK_SNAPSHOT_DB`, not container tests.
- Initial pre-fix API reproduction: four failures (three successful-edit scenarios
  returned 500; forced-failure test found committed reversal). Deliberately restoring
  the old registration reproduced the rollback assertion failure again.
- API, HttpClients and Mobile.Core Release builds passed with zero errors.
- EF pending-model check: no changes since the last migration.
- `git diff --check`: passed.
No new migration. Existing migrations are applied only to throwaway test containers.
No UI files or dirty canonical documentation files changed.
No production deployment/data mutation, push, master merge or APK publication.
