# StallTrack — Active Workstreams and Checkout Model

**Status:** Current coordination guidance as of 2026-10-08; older linked-worktree map retained below as archive
**Primary working checkout:** `C:\dev\stalltrack\eemo`
**Current local integration branch / application checkpoint:** `integration/report-governance-ui` at `6051a66d` (documentation checkpoint follows; inspect Git for exact HEAD)
**Shared local Web preview:** `https://localhost:7167`

Read `CURRENT_RELEASE_STATE.md` before using this file for implementation status. Business authority still comes from the Decision Registry, ADRs, operational rulebook, office evidence and source-specific architecture—not from branch naming.

## Current development model

StallTrack uses one Git repository. The default workflow is intentionally simple:

1. Work from `C:\dev\stalltrack\eemo`.
2. For a normal sequential ticket, create a short-lived feature branch in that same checkout.
3. Finish and commit the ticket, then return to the local integration branch and integrate the accepted commit.
4. Do **not** create a separate worktree for a small sequential tweak.
5. Create an additional linked worktree only when two agents genuinely need different branches checked out at the same time.
6. A worktree is only an isolated checkout of the same repository. It does not become a separate authority, and accepted work is not considered the current local baseline until it is integrated into `C:\dev\stalltrack\eemo`.
7. Never reset, overwrite or discard another session's dirty files. Inspect `git status --branch` before every branch switch or merge.
8. After integrating code into the primary checkout, rebuild/restart the affected API/Client/Mobile from `C:\dev\stalltrack\eemo` before visual/runtime review. A current Git HEAD does not prove the running `bin/Debug` or MAUI process was rebuilt; verify process paths and binary timestamps when UI appears stale.

The current primary checkout intentionally contains canonical documentation/evidence work. Agents must not stage, reset or rewrite those files unless their task explicitly includes documentation.

## Current accepted October 7 work

October 8 consolidation was performed sequentially in this same primary checkout on `codex/final-eemo-consolidation`, application commit `6051a66d`, accepted for local fast-forward integration. Automated validation and local API/schema verification passed. Native/browser UI-control initialization failed, so the user performed the fresh-app visual checkpoint and explicitly confirmed “Checked — all passed.” No additional worktree was created. See [the verification record](FINAL_SINGLE_REPO_CONSOLIDATION_20261008.md).

The local integration checkpoint includes the accepted collector/productivity chain through `6d3362f9`, including source-native collection, Fish/Meat registry lifecycle/import, Mobile correction atomicity, NPM Daily/Whole and Collect All readiness behavior, and the latest Web/Mobile V3 presentation follow-ups. See `CURRENT_RELEASE_STATE.md` and the four October 7 handoffs for exact validation scope.

## Historical linked-worktree archive

The remainder of this file records the earlier V2 many-worktree coordination model. It is retained only to explain old branch/session references. **Do not create or assign new work from the archived map below.** The former Business Payor target is superseded by IA-068/ADR-007 and the former TRM/Transportation convergence is superseded by IA-067.

## Current consolidated baseline

All active linked worktrees were synchronized to the current consolidated V2 baseline after the Market Fees, WCF, ECF, and Tabo work was integrated.

The canonical baseline already contains:

- Operations workspace
- Market Fees workspace
- Market Fees report
- Water Consumption Fees / WCF workspace
- WCF accounts page
- WCF report
- General Distribution / ECF workspace
- ECF accounts page
- ECF report
- Tabo / TPM refinement
- Tabo structured report
- WCF and ECF Operations navigation wiring

Every active session can therefore inspect the latest accepted V2 work instead of designing against an older copy.

## Session / branch / worktree map

| Session | Workstream | Branch | Worktree |
|---|---|---|---|
| A | Rent Income | `interface-v2/a-rent-income` | `C:\dev\stalltrack-v2-worktrees\a-rent-income` |
| B | Space Rental | `interface-v2/b-space-rental` | `C:\dev\stalltrack-v2-worktrees\b-space-rental` |
| C | Market Fees / canonical UI continuity | `interface-v2/clean-adoption` | `C:\dev\stalltrack-v2-clean` |
| D | Water Consumption Fees / WCF | `interface-v2/d-wcf` | `C:\dev\stalltrack-v2-worktrees\d-wcf` |
| E | General Distribution / ECF | `interface-v2/e-ecf` | `C:\dev\stalltrack-v2-worktrees\e-ecf` |
| F | Tabo | `interface-v2/f-tabo` | `C:\dev\stalltrack-v2-worktrees\f-tabo` |
| G | Fish / Meat Vendor Fees | `interface-v2/g-fish-meat-vendor` | `C:\dev\stalltrack-v2-worktrees\g-fish-meat-vendor` |
| H | Landing / Berthing | `interface-v2/h-landing-berthing` | `C:\dev\stalltrack-v2-worktrees\h-landing-berthing` |
| I | Transportation Fees | `interface-v2/i-transportation` | `C:\dev\stalltrack-v2-worktrees\i-transportation` |
| J | Weight & Measure / Registration | `interface-v2/j-weight-measure` | `C:\dev\stalltrack-v2-worktrees\j-weight-measure` |
| K | Large Cattle Transfer | `interface-v2/k-large-cattle` | `C:\dev\stalltrack-v2-worktrees\k-large-cattle` |
| L | Ice Plant | `interface-v2/l-ice-plant` | `C:\dev\stalltrack-v2-worktrees\l-ice-plant` |
| M | Integration / Visual QA | `interface-v2/m-integration` | `C:\dev\stalltrack-v2-worktrees\m-integration` |
| N | Itemized Collections Core | `interface-v2/n-itemized-core` | `C:\dev\stalltrack-v2-worktrees\n-itemized-core` |
| O | Collection Composer / Web | `interface-v2/o-collection-composer` | `C:\dev\stalltrack-v2-worktrees\o-collection-composer` |
| P | Itemization Migration / Legacy Adapters | `interface-v2/p-itemization-migration` | `C:\dev\stalltrack-v2-worktrees\p-itemization-migration` |
| Q | Collection Activity / RCD Reporting | `interface-v2/q-collection-reporting` | `C:\dev\stalltrack-v2-worktrees\q-collection-reporting` |
| V2 | Planner / Reviewer | `interface-v2/v2-planner` | `C:\dev\stalltrack-v2-worktrees\v2-planner` |

Session C remains attached to the canonical checkout so its existing Codex conversation/worktree continuity is preserved. It must avoid broad shared-file changes while another feature session is actively editing related files.

## Itemized Collections single-MASTER implementation governance

N/O/P/Q are paused candidate workstreams, not active implementation agents.

**Current status: PERMANENTLY PAUSED as implementation sessions.** Their partial, uncommitted worktrees remain available as read-only candidate evidence. Do not resume, modify, merge, discard, reset or synchronize them. Clint approved the canonical baseline and directed MASTER / V2 Planner to be the sole primary implementation agent, proceeding sequentially on `interface-v2/clean-adoption`, one bounded phase at a time. The role descriptions below and in the session guides record provenance, file history and architectural concern boundaries only; they do not assign work or authorize parallel implementation. MASTER may selectively reuse, adapt, reject, defer or reimplement candidate pieces after review.

- **N — Itemized Collections Core (candidate provenance)** records shared financial domain/application/persistence/API concerns for Collection, CollectionLine, allocations, posting, instrument policy, concurrency and accountable documents. Its partial work is read-only evidence and has no implementation contract authority.
- **O — Collection Composer / Web (candidate provenance)** records Current Collection/Composer UX and payor/operation entry concerns. Its UI assumptions do not define backend contracts or shared financial models.
- **P — Itemization Migration / Legacy Adapters (candidate provenance)** records historical compatibility, source mapping, cutover/reconciliation and adapter concerns. Candidate work must not fabricate historical receipt groupings or allocations.
- **Q — Collection Activity / RCD Reporting (candidate provenance)** records read models, activity, derived RCD/category aggregation and itemized reporting concerns. Candidate reporting must not create a second write path or financial totals authority.

MASTER proceeds through the approved phases sequentially. Candidate guidance does not authorize O/P/Q to work simultaneously or N to resume as a contract authority. MASTER decides whether candidate pieces are kept, adapted, rejected or deferred; prior implementation is not architectural approval.

[IA-038 / ADR-001 — Business Payor identity](../decisions/ADR_001_BUSINESS_PAYOR_IDENTITY.md) was the approved target at this historical checkpoint. **It is superseded prospectively by IA-068 / ADR-007.** Preserve the still-valid safety rules—no name-based merging, no cross-tenant linkage, and frozen posted payer evidence—but do not build new product workflows around a Business Payor master.

[IA-039 / Q42 and IA-040 / Q43 — Per-source canonical settlement cutover](../decisions/ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md) are confirmed target architecture: an explicit scoped reconciliation gate before freezing opening evidence and activating Canonical authority, followed by canonical posting with atomic compatibility projections. MASTER owns the transition/posting contracts, writer enforcement and reconciliation evidence. Every affected writer and in-flight channel must be covered, including physical issued documents. Unready sources remain Legacy; late old submissions remain preserved reconciliation exceptions.

[IA-041 / Q44 — Durable posting operation identity](../decisions/ADR_003_DURABLE_POSTING_OPERATIONS.md) binds tenant + ClientOperationId to normalized immutable intent and one durable outcome. MASTER owns registry, normalization, transaction behavior and consumer integration. The registry replaces mutable source keys as canonical idempotency authority. No new key bypasses document/source/reconciliation rules.

[IA-042 / Q45 — Versioned Web drafts](../decisions/ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md) makes the server authoritative for durable tenant/user-owned draft state. MASTER owns revision-bound mutation/review, recovery UX and atomic single-post linkage. One DraftId yields at most one Collection, independently of operation-key retries. Shared editing/handoff is not approved; abandoned-draft retention is deferred.

[IA-043 / Q46 — AsOf and LatestCorrected reporting](../decisions/ADR_005_CORRECTION_REPORTING_BASES.md) preserves immutable original/correction events and distinct business/effective/recorded dates. MASTER owns that evidence, financial effects and explicit reporting bases while preserving legacy-history limits. Official cross-period RCD treatment remains pending Office confirmation. Clint approved the [MASTER implementation baseline](../v2/ITEMIZED_COLLECTIONS_CANONICAL_IMPLEMENTATION_BASELINE.md) and sequential single-MASTER workflow.

All N–Q sessions must read root `CONTEXT.md`, `docs/business/REVENUE_ARCHITECTURE.md`, `docs/decisions/DECISION_REGISTRY.md`, and the current code before acting.

## Baseline synchronization

Before the multi-session launcher opens Codex tabs, it runs:

`C:\Users\ASUS VIVOBOOK\Downloads\Sync-StallTrack-Worktrees.ps1`

The sync helper is intentionally conservative.

For each linked worktree:

- clean + only behind canonical → fast-forward to canonical;
- already current → no action;
- uncommitted changes present → skip;
- branch has unmerged commits → skip;
- no automatic reset;
- no automatic rebase;
- no forced checkout;
- no work is discarded.

This ensures idle sessions inherit the latest accepted UI and business corrections without overwriting active work.

## Session continuity

The launcher is:

`C:\Users\ASUS VIVOBOOK\Downloads\StallTrack-Sessions-PowerShell.lnk`

which runs:

`C:\Users\ASUS VIVOBOOK\Downloads\StallTrack-Sessions.ps1`

Each tab resumes its pinned Codex conversation by exact session ID using no-daemon mode. The launcher does not rely on `resume --last`, which previously allowed multiple tabs to compete for the same recent conversation.

The launcher does not inject the old `You are StallTrack Session ...` assignment prompts.

Do not move/rename the existing worktree directories casually; the stable worktree paths and pinned session mapping preserve Codex session continuity.

## Parallel-work rule

Parallel work is allowed only when workstreams are genuinely separated.

A feature session owns its contextual page/component files.

Do not independently edit another feature session's files.

Shared files require explicit coordination, especially:

- `EEMOCantilanSDS.Client/Components/Pages/Menus/Operations.razor`
- `Operations.razor.css`
- shared sidebar/navigation
- `MainLayout`
- `FacilityHero.razor` / `FacilityHero.razor.css`
- global CSS/design tokens
- shared domain/revenue models
- API contracts
- migrations
- canonical business/decision documentation
- deployment/workflow files

When a feature needs a shared navigation change, record the requested route in its handoff and integrate it through the canonical branch rather than letting several sessions edit the same shared file concurrently.

## Canonical integration flow

Use this fast local sequence for every feature:

1. Sync the feature branch to the latest canonical baseline before starting.
2. Work only inside that session's linked worktree.
3. Inspect the latest accepted canonical V2 pages before styling so the feature inherits the current UI language and structure.
4. Keep business/domain rules specific to the feature; visual consistency never justifies copying the wrong workflow.
5. Run `git diff --check`.
6. Run the relevant local Release build/tests required for that scoped work.
7. Preview through the shared localhost slot when visual review is needed.
8. Create one focused feature commit containing only that workstream's files.
9. Merge the finished feature locally into `interface-v2/clean-adoption` promptly; do not leave completed code isolated on a feature branch.
10. If the merge touches or conflicts with an actively edited shared file, stop and coordinate instead of forcing the merge.
11. Run the combined Client build from `C:\dev\stalltrack-v2-clean`.
12. Confirm the integrated page from `https://localhost:7167`.
13. Sync every clean/idle linked worktree forward again so later sessions inherit the newest canonical UI and structure.

This V2 UI integration cycle is local-first. Do not push, deploy, or wait for GitHub Actions/remote CI merely to integrate and inspect a completed feature.

Do not let completed feature branches accumulate without integration; that recreates the stale-UI problem.

## Shared localhost preview

All sessions use one browser endpoint:

`https://localhost:7167`

Only one StallTrack Client preview runs at a time.

PowerShell helper:

`preview <SESSION>`

Examples:

`preview C` — canonical / Market Fees checkout
`preview D` — WCF branch
`preview E` — ECF branch
`preview F` — Tabo branch
`preview K` — Large Cattle branch

The preview helper switches the running Client process only. It does not close or modify Codex sessions.

Do not assign permanent per-session ports such as 7267, 7367, or 7467.

## Visual consistency contract

Multiple branches do not mean multiple design systems.

Every feature must inherit the accepted V2 language:

- dark navy application identity;
- restrained municipal gold;
- white/warm-white content surfaces;
- compact administrative spacing;
- strong readable typography;
- restrained gray borders/dividers;
- no random page-specific colors;
- no automatic green for ordinary states such as Active or Paid;
- consistent hero proportions and gold outlined icon treatment;
- consistent primary/secondary button hierarchy;
- consistent modal/form spacing and controls;
- one-border money inputs;
- no unnecessary Notes fields;
- no giant native dropdowns for searchable entities;
- formal government-document styling for reports;
- official EEMO report structure when evidence exists;
- no generic SaaS redesign;
- no invented business semantics.

A page may differ structurally because its business workflow differs, but it must still look like the same StallTrack system.

## Business consistency

Worktree ownership never changes business authority.

Preserve these distinctions:

- Facility != Revenue Classification
- Obligation != Collection
- Collection != Remittance
- Billing Basis != Payment Cadence
- Cash Ticket collection != Cash Ticket inventory/accountability
- Collection Efficiency != Revenue Target Attainment
- Delinquency != Arrears
- WCF = CT
- ECF = OR
- Tabo = OR
- Vegetable/Fruit full/whole payment = OR; daily transaction = CT
- ECF/WCF are broader EEMO Utility Operations, not globally NPM-owned

Do not invent unresolved Cantilan rules merely to finish a page.

## Commit / merge discipline

Before approval:

- do not merge into canonical;
- keep changes scoped to the feature branch;
- run diff/build validation;
- report the review URL.

After approval:

- stage exact files only;
- commit one focused feature change;
- merge into canonical;
- run the combined build;
- sync idle branches.

Never use broad reset/clean/stash operations across worktrees.

Do not push/deploy production merely because a feature branch builds.

## Pre-final destination

All approved V2 feature branches ultimately converge into:

`C:\dev\stalltrack-v2-clean`
`interface-v2/clean-adoption`

That checkout is the pre-final V2 integration source that will later be merged deliberately back into the main StallTrack repository/production path.
