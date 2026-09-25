# StallTrack V2 — Active Workstreams

**Status:** Active coordination record  
**Scope:** Temporary file/worktree ownership for concurrent StallTrack V2 development.  
**Authority:** This file controls edit ownership only. It does **not** override the canonical business, architecture, security, decision, or interface documents.

## Why this file exists

StallTrack V2 is being developed in parallel across multiple isolated Git worktrees. Each coding session must have one bounded workstream and must not edit another session's files or shared integration files unless Clint explicitly reassigns ownership.

Before editing code:

1. Read `AGENTS.md`.
2. Read this file.
3. Read the canonical V2/business/decision documents required by `AGENTS.md`.
4. Confirm that the file you intend to edit is owned by your current workstream.
5. If the file is shared or owned by another active session, stop and request integration instead of editing it.

## Primary V2 checkout

| Purpose | Path | Branch |
|---|---|---|
| Current V2 visual/integration checkout | `C:\dev\stalltrack-v2-clean` | `interface-v2/clean-adoption` |

### Temporary exception — Session C

Session C currently uses the primary checkout because the accepted/in-progress Market Fees UI already exists there as intentional uncommitted work.

Session C must preserve all unrelated working-tree changes and evidence files. Do not reset, stash, clean, or broadly stage the primary checkout.

Once Market Fees is visually approved and committed, Session C may be moved to its own isolated worktree if further parallel work requires it.

## Active session registry

| Session | Workstream | Worktree / checkout | Branch | Owned feature boundary | Status |
|---|---|---|---|---|---|
| A | Rent Income | `C:\dev\stalltrack-v2-worktrees\a-rent-income` | `interface-v2/a-rent-income` | Rent Income contextual work explicitly assigned by Clint | Reserved / wait for task |
| B | Space Rental | `C:\dev\stalltrack-v2-worktrees\b-space-rental` | `interface-v2/b-space-rental` | Space Rental contextual work explicitly assigned by Clint | Reserved / wait for task |
| C | Market Fees | `C:\dev\stalltrack-v2-clean` | `interface-v2/clean-adoption` | `MarketFees.razor`, `MarketFees.razor.css`, and only specifically approved Market Fees changes | Active |
| D | Water Consumption Fees / WCF | `C:\dev\stalltrack-v2-worktrees\d-wcf` | `interface-v2/d-wcf` | WCF contextual page files only | Active / isolated |
| E | General Distribution / ECF | `C:\dev\stalltrack-v2-worktrees\e-ecf` | `interface-v2/e-ecf` | ECF contextual page files only | Active / isolated |
| F | Tabo | `C:\dev\stalltrack-v2-worktrees\f-tabo` | `interface-v2/f-tabo` | Tabo contextual page files only | Active / isolated |
| G | Fish / Meat Vendor Fees | `C:\dev\stalltrack-v2-worktrees\g-fish-meat-vendor` | `interface-v2/g-fish-meat-vendor` | Fish/Meat Vendor contextual page files only | Active / isolated |
| H | Landing / Berthing | `C:\dev\stalltrack-v2-worktrees\h-landing-berthing` | `interface-v2/h-landing-berthing` | Landing/Berthing contextual page files only | Active / isolated |
| I | Transportation Fees | `C:\dev\stalltrack-v2-worktrees\i-transportation` | `interface-v2/i-transportation` | Transportation contextual page files only | Active / isolated |
| J | Weight & Measure / Registration | `C:\dev\stalltrack-v2-worktrees\j-weight-measure` | `interface-v2/j-weight-measure` | Weight & Measure contextual page files only | Active / isolated |
| K | Large Cattle Transfer | `C:\dev\stalltrack-v2-worktrees\k-large-cattle` | `interface-v2/k-large-cattle` | Large Cattle contextual page files only | Active / isolated |
| L | Ice Plant | `C:\dev\stalltrack-v2-worktrees\l-ice-plant` | `interface-v2/l-ice-plant` | Ice Plant contextual page files only | Active / isolated |
| M | V2 Integration / Visual QA | `C:\dev\stalltrack-v2-worktrees\m-integration` | `interface-v2/m-integration` | Explicitly approved shared integration, route wiring, conflict resolution, cross-page visual QA | Integration only |
| V2 | Planner / Reviewer | `C:\dev\stalltrack-v2-worktrees\v2-planner` | `interface-v2/v2-planner` | Read-only by default; planning/review/handoff | Planner |

## Income from Market assignment map

The current parallel Income from Market sprint is divided as follows:

| Operations entry | Session | Intended route |
|---|---|---|
| Market Fees | C | `/operations/market-fees` |
| Water Consumption Fees / WCF | D | `/operations/water-consumption-fees` |
| General Distribution / ECF | E | route to be approved during workstream |
| Tabo | F | route to be approved during workstream |
| Fish / Meat Vendor Fees | G | route to be approved during workstream |
| Landing / Berthing | H | route to be approved during workstream |
| Transportation Fees | I | route to be approved during workstream |
| Weight & Measure / Registration | J | route to be approved during workstream |
| Large Cattle Transfer | K | route to be approved during workstream |
| Ice Plant | L | route to be approved during workstream |

Do not invent a route merely to make a page navigable. Feature sessions report the desired route in their handoff; Session M or the explicitly designated integration owner wires shared navigation after visual approval.

## Shared localhost preview policy

All V2 workstreams use one browser-review endpoint:

`https://localhost:7167`

Coding remains parallel across isolated worktrees, but only one StallTrack Client preview may own the shared port at a time.

Local switching helper:

`C:\Users\ASUS VIVOBOOK\Downloads\Preview-StallTrack.ps1`

PowerShell convenience command:

`preview <SESSION>`

Examples:

`preview C` — Market Fees from the primary V2 checkout
`preview D` — WCF from Session D's worktree
`preview E` — ECF from Session E's worktree
`preview F` — Tabo from Session F's worktree

The helper stops the previous StallTrack V2 Client preview and starts the selected worktree on port 7167. It does not stop or modify Codex sessions.

Rules:

- Do not assign permanent per-session ports such as 7267, 7367, or similar.
- Do not edit committed `launchSettings.json` just to get a unique worktree port.
- Do not run multiple StallTrack V2 Client previews concurrently.
- A completion handoff should report the review URL using `https://localhost:7167` plus the feature route.
- If port 7167 is occupied by an unknown/non-StallTrack process, stop and report the conflict rather than killing it.

## Codex session continuity

The local multi-session launcher resumes the latest Codex chat associated with each worktree directory.

Closing and reopening `StallTrack-Sessions-PowerShell.lnk` must resume those worktree chats instead of starting fresh chats or replaying the assignment prompt.

Worktree isolation is therefore also the session-continuity boundary: A resumes A, D resumes D, and so on.

## Shared-file locks

Parallel feature sessions must **not** edit the following unless Clint explicitly assigns that shared-file change:

- `EEMOCantilanSDS.Client/Components/Pages/Menus/Operations.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Operations.razor.css`
- shared sidebar/navigation components
- `MainLayout` or other global shell files
- `FacilityHero.razor` / `FacilityHero.razor.css`
- global design-system CSS/tokens
- canonical business documentation
- `docs/decisions/DECISION_REGISTRY.md`
- shared revenue/domain models
- API contracts
- database migrations
- deployment/workflow files

A feature session that needs a shared-file change must place it in its completion report under **Shared-file integration requests**.

Example:

```text
Shared-file integration request:
Operations.razor
Water Consumption Fees / WCF
→ /operations/water-consumption-fees
```

## Feature-session editing rule

A feature session may:

- create or edit the page/component files explicitly assigned to its workstream;
- use component-local hardcoded state for approved UI-only work;
- inspect other accepted pages as visual/reference evidence;
- build and run tests needed to validate its own work.

A feature session may **not**:

- redesign another workstream;
- edit another session's owned files;
- modify shared integration files merely to make its own page reachable;
- change backend/domain/API/database behavior during a UI-only task;
- alter confirmed OR/CT policy;
- invent unresolved EEMO business rules;
- commit before Clint visually approves the localhost result;
- reset, clean, stash, or broadly stage work in another checkout.

## Business consistency

Active ownership never changes business authority. Every session still follows the canonical reading order in `AGENTS.md`, especially:

- `docs/v2/STALLTRACK_V2_MASTER_SPECIFICATION.md`
- `docs/README.md`
- `docs/business/EEMO_OPERATIONAL_RULEBOOK.md`
- `docs/business/EEMO_BUSINESS_RULES.md`
- `docs/business/REVENUE_ARCHITECTURE.md`
- `docs/decisions/DECISION_REGISTRY.md`
- relevant `docs/interface/` material

Important current distinctions remain:

- Facility != Revenue Classification
- Obligation != Collection
- Collection != Remittance
- Billing Basis != Payment Cadence
- Cash Ticket collection != Cash Ticket inventory/accountability
- Collection Efficiency != Revenue Target Attainment
- Delinquency != Arrears

## V2 visual consistency

Parallel UI sessions must preserve the accepted StallTrack V2 direction:

- dark navy and restrained gold LGU identity;
- dense administrative presentation;
- strong readable typography;
- compact white/warm-white surfaces;
- restrained borders and status colors;
- existing shell/sidebar visual language;
- no generic SaaS redesign;
- no random colorful icon system;
- no excessive whitespace;
- no prototype/demo wording in intended operational UI;
- no global component redesign just to satisfy one isolated page.

Build success does not equal visual approval. Clint's localhost review is the visual gate.

## Commit and integration policy

Before visual approval:

- leave feature work uncommitted;
- run `git diff --check`;
- run the relevant build/tests;
- report the exact files changed;
- stop for visual review.

After approval:

- stage only exact owned files;
- verify `git diff --cached --name-only`;
- make one focused workstream commit;
- report the commit hash to the integration session.

Session M must not cherry-pick or integrate a feature that Clint has not approved.

## Required completion handoff

Every implementation session reports:

```text
SESSION:
WORKSTREAM:
WORKTREE:
BRANCH:

FILES CREATED:
FILES MODIFIED:

BUSINESS ASSUMPTIONS USED:

UI / BEHAVIOR IMPLEMENTED:

SHARED-FILE INTEGRATION REQUESTS:

VALIDATION:
- git diff --check:
- build/tests:
- warnings/errors:

NOT IMPLEMENTED / STILL GATED:

VISUAL REVIEW URL:

COMMIT:
- uncommitted, or approved commit hash
```

## Updating this registry

Update this file whenever:

- a workstream moves to another session;
- a session receives a new file boundary;
- a worktree/branch is replaced;
- a workstream completes and becomes integration-ready;
- a shared-file lock is intentionally reassigned.

Do not use this file as implementation history. Completed long-lived decisions belong in the canonical docs or decision registry.
