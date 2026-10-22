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