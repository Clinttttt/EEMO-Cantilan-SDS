# StallTrack V2 — Active Workstreams

**Status:** Active linked-worktree development
**Repository model:** One Git repository, multiple linked worktrees/branches
**Canonical integration checkout:** `C:\dev\stalltrack-v2-clean`
**Canonical integration branch:** `interface-v2/clean-adoption`
**Shared browser preview:** `https://localhost:7167`

## Development model

StallTrack V2 uses one Git repository.

The directories under:

`C:\dev\stalltrack-v2-worktrees`

are Git worktrees linked to the same repository as:

`C:\dev\stalltrack-v2-clean`

They are not separate clones or independent projects.

This lets multiple Codex sessions work on different branches simultaneously while sharing the same repository history.

The canonical integration branch is:

`interface-v2/clean-adoption`

Feature branches are merged back into this branch after review.

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
| V2 | Planner / Reviewer | `interface-v2/v2-planner` | `C:\dev\stalltrack-v2-worktrees\v2-planner` |

Session C remains attached to the canonical checkout so its existing Codex conversation/worktree continuity is preserved. It must avoid broad shared-file changes while another feature session is actively editing related files.

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
- Tabo = CT

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
