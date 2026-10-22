# Multi-LGU Terminal and custom rental workstream (22 October 2026)

**Isolation:** EEMO branch `agent/tenant-terminal-facility-v10-20261022` in `C:\dev\stalltrack-v2-worktrees\tenant-config-agent-20261022`. Platform branch `agent/onboarding-terminal-facility-20261022` in `C:\dev\stalltrack-v2-worktrees\tenant-config-platform-20261022`. Based respectively on source `origin/master` e6bd88ce and Platform `origin/main` ab1f077. Neither the main checkout nor Codex's Fish/Meat worktree was modified. No production action, push, merge, or migration is authorized by this handoff.

## Implementation partition

- **Completed in isolated worktrees (candidate, not deployed):** Route existing Custom1–Custom5 monthly-rental facilities under the Portal's *Rent Income — Stall Rental* group instead of *Configured enterprises*, preserving `/facility/{slug}`, canonical OR display and same underlying stall/contract/payment writers. Propose the existing `RENT_OTHER` official row under Rent rather than Pending without changing any monetary mapping, classification or row key. Align Landing onboarding copy with its current *Other* / Custom1–Custom5 monthly-stall facility semantics; preserve five shared slots and per-stall rates after activation.
- **Requires acceptance before merge:** Official Monthly Income `RENT_OTHER` group move must be confirmed against the approved municipality statement; it is presently a deliberate pending group in production. If unapproved, revert the group-only change while retaining a clearly marked pending report row. Do not silently reclassify historical posted money.
- **Not implemented in this checkpoint:** Terminal section onboarding, post-activation settings, tenant terminal-source activation/disabling; arbitrary custom monthly *Space Rental* source. Adding UI controls that only alter labels without financial authority is specifically prohibited.

## Terminal — confirmed design dependencies

Existing source identities are `TerminalSection.ComfortRoom`, `PullPulVansCargoVans`, `Tricycad`, mapped by `OfficeCollectionWorkflow.SectionCode` to stable `TERMINAL_*` revenue classification codes. Three sections are rendered via `Enum.GetValues` in `Client/Components/Pages/Menus/Terminal.razor`; Mobile session discovery in `Infrastructure/Repositories/Revenue/CollectionSessionSources.Native.cs` also loops over them; `GetCollectorOperationCapabilitiesQueryHandler` assumes all three when checking readiness. Source-native posting resolves a classification/policy in `OfficeCollectionWorkflow.ResolveAsync`, and official report rows are enumerated in `OfficialMonthlyIncomeStructure`.

The Portal has *transport vehicle classes* and *per-trip TRM collections*, while `Income From Terminal` is a separate source-native CT family. Changing the `TRM` facility name or rate is not a substitute for defining its three income sections.

**Approved target contract (needs implementation and migrations):**
1. Tenant-scoped `TerminalSectionSetting(MunicipalityId, stable TerminalSection, DisplayName, IsEnabled, Version, audit fields)` or a demonstrably equivalent authoritative store; do not reuse NPM section labels, `Facility.Description`, browser storage, or mutable classification/policy names as a display-label hack.
2. Onboarding defaults show all three with deselect/rename; the approved selections survive save/reload/validation/activation. Absent block on old drafts/tenants preserves Cantilan's established behavior. The operator sees exact stated choices; none implies none.
3. Post-activation Head can edit each section with optimistic concurrency and tenant-scoped authorization; disabled means *not offered for new collections*, NOT deletion, refund restriction, or historical reclassification. Existing vehicle-section references remain auditably intact.
4. One shared read projection supplies Web Terminal cards, vehicle-class mapping choices, Mobile session source choices, capabilities and reports. Display names never determine classification, rate, OR/CT or collector permissions.
5. New LGUs do **not** automatically inherit Cantilan's CT approval just because a section is selected. Classifications and effective-dated instrument policy must be separately verified/approved. Unknown/unconfigured/disabled sections fail closed at actual posting, not just hidden by UI.
6. Historical collections/reversals and official monthly rows remain accessible for the correct period after a section is disabled or renamed. Existing tenants' data must not be rewritten. Tests: old tenant fallback, all/none/one enabled, local rename, mismatched tenancy, authorization, disabled posting, vehicle mapping, Mobile choice, report historic reconciliation.

## Custom monthly rental category decision

The current five `Custom1..Custom5` identities unconditionally use `BillingArchetype.MonthlyRental` and established facility/stall/contract/payment machinery. They are therefore valid *custom Stall Rent* candidates; separate generic monthly Space Rental is **not** a routing flag.

A truly custom monthly Space Rental must use the `ObligationAccount`/period/allocation source contract and supported `ObligationKind` semantics. Current source kinds include `KanmanggaySpaceRental` and `VegetableFruitSpaceRental`; the latter are not interchangeable with facility stall rent. Do not create an unrestricted `Space + Add New` that silently maps to Custom1 or to Kanmanggay merely because both collect monthly cash. Contract duration (including the Cantilan-specific three-year rule), receipts, policy classification, Mobile source discovery and official report grouping need explicit LGU decisions and integration tests first.

## Merge discipline

Codex owns the Fish/Meat/NPM task in its own worktree. The multi-LGU workstream must never cherry-pick a Codex WIP commit or touch its checkout; integrate both against updated remote master after separate review and run conflict/diff checks, full relevant tests, disposable Postgres test suites and one tenant-isolation browser walkthrough. Keep AWS deploy, live data, APK and migrations untouched until that release is approved.
## Terminal implementation checkpoint (22 October 2026)

**Candidate code completed in the isolated agent branches, not merged/deployed:**

- Versioned three-section Terminal declaration in the Landing onboarding workspace, with independent local names and selection, reusable shared validation library, preservation of unreadable newer drafts and operator activation mapping.
- Backend `TerminalSectionSetting` entity (tenant ID + stable `TerminalSection` + enabled flag + local name + concurrency version + audit fields), isolated through automatic tenant-owned EF query filters.
- Additive migration `20261022135153_AddTenantTerminalSectionSettings20261022` creating only `TerminalSectionSettings` with one unique row per `(MunicipalityId, Section)`. The migration has NOT been applied to any database.
- Backend draft save/submission validation and activation-time stamping of explicitly selected OR disabled sections. No CT policy or collector assignment is created from the declaration.
- Head-only settings change in `/api/terminal-sections` with GET for signed-in roles, concurrency conflict handling, first-edit legacy adoption, exact municipality scoping, and unique local names.
- Head Terminal page editor, named cards, vehicle mappings and operational Terminal Report labels / CSV use tenant configuration. Disabled sections with historical collections remain visible in history, and the section's stable classification is unchanged.
- Mobile collector options, vehicle choice quoting and source-native posting honor the backend enabled setting; disabled sections cannot be added to new collections, and current policy checks continue to govern whether Cash Ticket recording is allowed.
- Existing municipalities with no configuration row retain the original three Cantilan section defaults for backward compatibility; explicitly configured municipalities with all three disabled do NOT automatically re-enable them.

**Checks executed during isolated work:** EEMO Client and API builds successful (existing compiler warnings), 13 Operations component tests, 9 targeted EEMO unit tests, Landing tests 88/88 passing, Admin tests 153/153 passing. Initial parallel Vitest runs crashed due Node workers; sequential reruns passed. These do not replace a full disposable-Postgres integration run or actual browser/Collector APK E2E.

**Important release gates before any integration/deployment:**

1. Compare and regenerate EF model snapshot against the latest `master` **after Codex Fish/Meat migrations**; two agents may independently change `AppDbContextModelSnapshot.cs`. Resolve integration conflicts without discarding either migration.
2. Run disposable PostgreSQL migration and tenant isolation tests (Cantilan legacy, Carmen all-disabled, Madrid renames, cross-tenant edits refused). Test Head edits and Collector quote/post rejection end-to-end, not only unit tests.
3. Confirm approved Terminal instruments for each LGU: activation does NOT seed Cash Ticket classification policies for a new municipality. A selected terminal section is therefore initially *configured but not collectible* until an approved policy exists.
4. Confirm report wording semantics: the operational Terminal report uses the latest local alias for stable section identities; official archival reporting must continue to reconcile original stable classification codes and collection evidence. No historical amounts have been reclassified or changed.
5. Browser-check responsive Head drawer, Terminal screen and report; Mobile choices are API-derived but an APK E2E was not performed.

### Custom Space Rental — separate financial model, not yet safely implemented

The user's requested Space Rental `+ Add New` cannot use `Custom1..Custom5` as though it were a Stall Rent facility: those identities use `BillingArchetype.MonthlyRental`, the facility / Stall / Contract / PaymentRecord source. Existing Spaces instead use `ObligationAccount`, `ObligationKind`, a tenant and source-specific registration lifecycle, billing/allocations, collection authority and official report group.

A production-valid generic custom Space Rental needs an explicit new stable source identity/tenant space-category registry, configured period and rate policy, contract terms where applicable, source versioning, obligor/space lookup, OR policy, Mobile permissions/choice generation, corrections/reversals, and report/source totals. Until that is built and tested, do not ship an onboarding `+ Add New` that invents an unsupported `ObligationKind` or misroutes it to the Custom Stall Rent endpoint. An LGU can already rename/select the existing built-in space operations through tenant operation settings; that is NOT the same as adding multiple independent sources.

No code, migrations, or production changes for a **generic custom Space Rental** were made in this phase. This is a deliberate explicitly recorded functional boundary rather than a dummy button.

## Candidate implementation update — 22 October 2026 (isolated worktrees)

This addendum supersedes the "not implemented" Terminal status in the original checkpoint above; that historical checkpoint is retained for traceability.

- Terminal sections are now modeled by tenant-owned `TerminalSectionSettings` with the stable three-section enum, optional local names, enabled state, version/concurrency, and audit properties. Migration `AddTenantTerminalSectionSettings20261022` adds a separate table/index; it does not alter collection lines or receipts.
- Versioned Landing Terminal declarations persist through draft editing, validation, operator review, and activation. Old drafts without declarations keep the old all-enabled fallback; unknown/future declarations remain preserved and block activation rather than being interpreted as empty.
- Activation seeds section settings and **selected classification identities only**, with **no Cash Ticket policy or collector assignment**. An LGU Head approves an effective-dated instrument policy separately in Revenue Setup.
- A Head-only tenant-matched API supports post-activation edits with optimistic concurrency. Mobile source discovery, capability checks, server quotes/posting, and vehicle mapping exclude disabled sections. The Web Terminal operations and register report render the LGU's section names; official classification codes and posted monetary records remain unchanged.
- Operator Validation and Activation screens display the exact selected/not-selected Terminal sections and local names.
- Verified during this workstream: successful Landing production build and 88 Landing tests, successful Admin production build and 153 Admin tests (verification must be repeated after any later edit), successful EEMO Client build, and passing targeted .NET tests. See local worktree logs for run details.
- **Not release approval:** Browser acceptance at actual viewport sizes, full suites, disposable PostgreSQL migration/tenant-isolation tests, and historical report reconciliation against a real multi-LGU data fixture remain prerequisites to a merge/deployment.
- Custom1–Custom5 remain **monthly Stall Rent**, not generic Space Rental. An arbitrary monthly Space Rental is **still not implemented**, pending an approved `ObligationAccount` source contract, classification, period allocation and report placement. Existing `RENT_OTHER` official grouping remains subject to office acceptance before merge.

No production migration, production release, merge to `master`, or collector APK is approved by this candidate work.

## Owner clarification — Custom Spaces monthly engine (2026-10-22)

The owner explicitly approved reusing the established monthly Stall Rent obligations, contracts, payments, correction and receipt engine for **Custom Spaces**. These two custom source types differ in **category and UI/report placement**, not in monthly charge cadence or the underlying money workflow.

Required future shape: a stable tenant-scoped custom facility identity with a declared `Space` versus `Stall Rent` category; display each in its appropriate existing Operations section; continue using its own source ID, period, occupant, rate and payment records. Changing category must not move old money or silently relabel historical official report rows. The official monthly statement's `RENT_OTHER` mapping for custom Spaces must be approved and made explicit (not inferred from shared billing code). This clarification supersedes the earlier recommendation that Custom Spaces necessarily require a new obligation engine. **No Custom Spaces implementation or financial-source migration is implied by this documentation change.**

## 2026-10-22 integration audit correction — tenant backup completeness

A disposable combined checkout with the Codex Fish/Meat registry change exposed a previously missing invariant: adding the tenant-owned `TerminalSectionSettings` table without including it in the tenant backup/export manifests fails `TenantBackupCoverageTests.EveryTenantOwnedTableIsRestorableExportedOrExcludedWithAReason`. This is a genuine release blocker, not a cosmetic issue.

Correction: `TenantDataTables.Restorable` includes `TerminalSectionSettings`, and `TenantExportRepository` exports the table with an explicit resolved-municipality filter. The pre-existing `TenantRestoreRepository` derives restorable table order and serializes physical table rows from the EF model; no ad hoc restore/recreation endpoint is added. Combined-test replay: all six `TenantBackupCoverageTests` pass. No production database was modified.

Integration rehearsal remains separate from the official candidate: `C:\dev\stalltrack-v2-worktrees\release-integration-audit-20261022` is a disposable worktree with Codex's uncommitted Fish/Meat diff copied in. A clean combined API Release build, EF `has-pending-model-changes` check, and idempotent migration script generation passed there; independently passing source branches do not replace multi-feature testing. Do not push `master` or deploy until combined migration/collection checks, APK procedure, and rollout/rollback compatibility are approved.
