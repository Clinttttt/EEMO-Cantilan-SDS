# V13 multi-issue implementation evidence — 2026-10-23

Local implementation evidence, **not a production release or municipal policy approval**. Continue the nine-issue audit at `C:\dev\stalltrack\release-evidence\V13_MULTI_ISSUE_READONLY_AUDIT_CODEX_HANDOFF_20261023.md`.

## Isolation and review units

- Worktree: `C:\dev\stalltrack\codex-v13-multi-fixes-20261023`; branch `fix/v13-integrity-and-ui-20261023`.
- Verified starting `origin/master`: `fcf01c44dae7bdc3acd04bb4e8b6b9c9ed803ae9`.
- The dirty `fix/fish-meat-integrity-20261022` checkout was not edited, reset, copied wholesale or integrated. Existing Fish/Meat migrations and request activation already on master were reused.
- Review independently: **A** existing-style UI/branding; **B** weighing presentation/integrity tests; **C** payment capture reconciliation, audit identity and remittance evidence. No new migration, dependency or municipality activation is introduced.
- No push, merge, service restart, APK publication, production deployment or production database write.

## Nine issue outcomes

| Issue | Verified finding and implemented boundary | Still requires evidence/authorization |
|---|---|---|
| 1. LGU seal | Public Carrascal branding returns a versioned seal URL; GET returned 200, `image/jpeg`, 54,362 bytes. The screenshot's browser failure was not reproduced. Confirmed separate defect: unresolved/failed branding claimed Cantilan's seal. `BrandingState` now returns its existing neutral placeholder until resolved; resolved default Cantilan retains its seal. Sidebar handles image failure and retries a newly versioned URL. | Authenticated onboarding/upload, cache/network and rendered Carrascal reproduction. A successful public GET does not prove the sidebar problem fixed. |
| 2. Fish/Meat requests | Current master already has normal explicit tenant activation plus migration/physical-schema readiness, separately from mandatory-registration cash policy. Existing API/request lifecycle and negative-scope tests are exercised; no flag is changed. | Approved exact tenant allow-list, deployed migration history/physical schema, operator authorization and authenticated live acceptance. See procedure below. |
| 3. Online SRC | `CollectionSourceAuthorityMap` explicitly marks `OnlinePaymentTransaction` legacy-only. Historical missing SRC is not a missing canonical receipt to manufacture. Confirmed callback gap: monthly cutover guards returned Conflict without saving captured provider evidence. They now persist the existing `ReconciliationRequired` state, provider ID/method/time/payload and preserve source balances; retry posts no cash. | A future canonical online writer needs a separately approved source/cutover decision, transaction/replay design, reconciliation/refund ownership and report authority tests. This change does **not** activate that writer or backfill SRCs. |
| 4. Weight & Measure | Hero formerly showed historical NPM figures despite the independent registered-vendor report. Hero now uses that server report; original frozen historical figures remain explicitly identified. Both pages retained the previous month's source after an exception and accepted out-of-order responses under the new period label. Clear old data, capture the requested period and accept only the latest load. Unavailable report data is distinguished from a genuine empty period. | Reconciliation against the reported real tenant/SRC needs authorized records and the same business date/scope. No query defect or missing production money was established. |
| 5. Monitoring tabs | Shared tabs and Follow-up override requested scroll rails. Remove those rails; wrap existing links at narrow widths and preserve focus outlines/active underline. | Actual desktop/narrow rendering and keyboard visual acceptance. |
| 6. Trend chart | Label shared bar geometry without sufficient top space. Reserve headroom and align the axis/grid; constrain labels to their column while keeping full amounts in title/accessible labels/tooltips/table. Existing stack data, zero traces and 3D faces remain. | Rendered monthly/annual, high/zero values and narrow-screen review. |
| 7. Audit actors | Directory grouped usernames across all municipalities and picked the first staff record, ignoring stored actor ID. Resolve recorded staff ID first, support the dashed and compact GUID formats used by existing writers, use only unambiguous local legacy usernames, exclude known foreign IDs and preserve unknown/deleted actors' recorded names. Ambiguous filter labels retain the recorded username. | No trustworthy initiating human was identified for an unspecified background System event. Existing interceptor records authenticated identity when available; true automated events remain System. No guessed retroactive reassignment. |
| 8. Mobile identifier | Monthly preview inherited a fixed 42px square. Scoped rule gives identifiers natural width, no wrapping and constrained overflow with the full identifier in a title. No shared payment logic/style is changed. | Physical 320–390px device/GUI acceptance and a separately authorized signed APK release. |
| 9. Remittance | Reuse existing V3 modal/focus helper, Escape/Cancel and selection state; preserve multi-collector atomic posting. Expected/submitted money, actor/time/reference/reason and coverage history already persist. Require a reason for a **new** shortfall after the existing replay lookup. Expose earlier voided remittances sharing actual coverage, including void reason/actor/time and shared count. | Rendered focus/scroll checks. Shared coverage proves overlap, not an inferred replacement intent. Existing submitted amounts remain immutable; old matching retries are evaluated before the new reason guard. |

## Source and financial safeguards

- Presentation: `MonitoringTabs`, `FollowUpQueue`, `RevenueTrendPanel`, Mobile `MonthlyCollection`, Client `Sidebar`/`BrandingState`, `WeightAndMeasure`/`WeightAndMeasureReport`, `Remittances`/`RemittanceDetail` and their focused tests.
- Financial changes: `OnlinePaymentSettlementService`, `OnlinePaymentTransaction` documentation, `RemittanceWorkflow`, additive optional `RemittanceDetailDto.PriorVoidedCoverage`, and `AuditRepository` identity resolution. No source-authority map, rate, allocation, receipt serial, historical record or reporting formula is rewritten.
- Remittance reasons use existing immutable Remarks evidence. Prior coverage uses retained tenant-scoped coverage rows and voided remittances, not a fabricated parent ID. No migration is needed.
- Online cutover remains fail-closed. A captured payment's durable reconciliation state is not a successful debt settlement. No Collection, allocation, OR or SRC is generated by this refusal path.
- PostgreSQL tests use disposable Testcontainers `postgres:18-alpine` with random mapped ports/database `stalltrack_tests`. The retained database on 5432 and the working-copy database on 55491 are not used.

## Fish/Meat activation procedure — pending operator approval

1. Verify the intended environment and exact authorized municipality ID. Read deployed EF history and ensure the existing migration `20261020133853_AddFishMeatRegistrationRequestsWorkingCopy20261020` and physical request columns/permissions are present. Preserve its migration identity. Any missing production migration needs separate reviewed release/control-repository authorization.
2. With approval, use normal `StallTrack:FishMeatRequests:Enabled=true` and an explicit `StallTrack:FishMeatRequests:MunicipalityIds` allow-list. Do not simultaneously enable the working-copy activation helper. Empty/duplicate/invalid IDs are rejected by existing startup validation.
3. Leave `RegistrationRequiredFrom` unchanged. Request availability grants no authority to change direct vendor-fee policy or create cash.
4. Through authenticated endpoints, verify assigned Collector submission/retry/cancellation and Head return/reject/register/link completion. Confirm an unapproved municipality remains unavailable. Missing schema must return `VendorRequestSchemaNotEnabled`, not a database exception.

## Validation record

Final validation completed on 2026-10-24. Counts below are separate runs, not a sum of unique tests.

| Check | Result | Log under `%TEMP%` |
|---|---|---|
| Focused unit: audit, online/PayMongo, weighing, activation/schema, source authority | 231 passed, 0 failed/skipped | `v13-unit-final.log` |
| Actor follow-up, including the compact-ID regression | 25 passed, 0 failed/skipped | `v13-actors-final.log` |
| Head bUnit, including failed and out-of-order period loads | 92 passed, 0 failed/skipped | `v13-components-complete.log` |
| All Mobile component tests | 167 passed, 0 failed/skipped | `v13-mobile-components.log` |
| Disposable PostgreSQL 18: collection sessions, requests, migration guards, remittance, online capture, actors | 150 passed, 0 failed/skipped | `v13-postgres-final.log` |
| PostgreSQL actor follow-up: dashed/compact IDs, shared usernames, search, tenant exclusion | 1 passed, 0 failed/skipped | `v13-postgres-actor-formats.log` |
| PostgreSQL remittance follow-up: explicitly supplied synthetic shortfall reasons, no inferred fixture reasons | 21 passed, 0 failed/skipped | `v13-postgres-remittance-final.log` |
| API Release | 0 errors, 19 warnings | `v13-api-build-final.log` |
| Client Release | 0 errors, 95 warnings | `v13-client-build-final.log` |
| Windows Mobile Release | 0 errors, 26 warnings | `v13-mobile-build.log` |
| CSS and whitespace | Five changed CSS files plus generated Client/Mobile bundles brace-balanced; `git diff --check` passed | Native PowerShell scanner |

Regression checks failed before the fixes: unresolved branding/stale-period data (3 cases), actor identity/captured cutover callback (2), PostgreSQL shortfall/prior coverage (2), compact foreign actor ID (1), and out-of-order period responses (2). Earlier compile/fixture/caller errors were corrected; they are not counted as green validation. Builds are not warning-free; no dependency upgrade was mixed into this change.

Commands used, after dependency restore (run suites separately):

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore --filter 'FullyQualifiedName~Audit|FullyQualifiedName~OnlinePayment|FullyQualifiedName~PayMongo|FullyQualifiedName~Weighing|FullyQualifiedName~FishMeatRequest|FullyQualifiedName~FishMeatRegistrationRequestSchema|FullyQualifiedName~CollectionSourceAuthorityMap' --logger 'console;verbosity=normal'
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --no-restore --filter 'FullyQualifiedName~AuditRepositoryTests|FullyQualifiedName~AuditTrailScopeTests|FullyQualifiedName~AuditInterceptorTests' --logger 'console;verbosity=normal'
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Branding|FullyQualifiedName~SidebarNavigationTests|FullyQualifiedName~FishMeatWorkspaceTests|FullyQualifiedName~RemittanceWorkspaceTests|FullyQualifiedName~RevenueTrendPanelTests|FullyQualifiedName~MonitoringTabs' --logger 'console;verbosity=normal'
dotnet test EEMOCantilanSDS.Mobile.ComponentTests/EEMOCantilanSDS.Mobile.ComponentTests.csproj -c Release --logger 'console;verbosity=normal'
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~RemittanceWorkflowTests|FullyQualifiedName~CollectionSessionTests|FullyQualifiedName~OnlinePaymentCheckoutExpiryConcurrencyTests|FullyQualifiedName~ForwardOnlyMigrationSafeguardTests|FullyQualifiedName~AuditActorIdentityTests' --logger 'console;verbosity=normal'
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AuditActorIdentityTests' --logger 'console;verbosity=normal'
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~RemittanceWorkflowTests' --logger 'console;verbosity=normal'
dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj -c Release --no-restore
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj -c Release --no-restore
dotnet build EEMOCantilanSDS.Mobile/EEMOCantilanSDS.Mobile.csproj -c Release -f net10.0-windows10.0.19041.0
git diff --check
```

The PostgreSQL run applied the existing EF history to an empty database and exercised the NPM forward-only evidence safeguard, Fish/Meat tombstone rollback protection, and missing-request-schema diagnostic. No schema change is introduced by this feature. A captured monthly-cutover callback persisted provider evidence without a Collection/allocation, kept its monthly source unpaid, and replayed without a second settlement. Weighing report checks kept original frozen rate evidence and original Collection totals intact after a reversal while reducing the net report.

Browser/native UI surfaces were unavailable in the connected computer-use inventory; opening the in-app browser returned `Browser is not available: iab`. Component checks and public HTTP checks are **not** rendered desktop/Android/Windows acceptance. No screenshots or live authenticated financial acceptance are claimed.

## Release and rollback

Local review commits: `d4e292ad` (UI/branding), `fc3634ae` (captured payment reconciliation), `647e2fee` (weighing), `24487325` (remittance modal/evidence), `fe90ed37` (audit identity). Final documentation and fixture cleanup are a separate local commit; inspect `git log` for its SHA. No PR was opened or branch pushed.

Review the separate change groups, then obtain release authorization. No new migration is required by these changes. Deploy reviewed API/Client together for the remittance guard/read DTO and online reconciliation fix; publish Mobile separately through its approved signed release workflow/versioning.

Code rollback must retain new reconciliation evidence, shortfall reasons and historical remittance coverage. Do not downgrade/delete financial facts or restore a database to undo this UI/application change. The existing reconciliation state and Remarks/coverage columns remain readable by the prior schema. Policy enablement and any later canonical-online cutover require their own approval and rollback plan.
