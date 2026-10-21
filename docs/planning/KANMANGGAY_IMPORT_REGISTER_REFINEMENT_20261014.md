# Kanmanggay import register refinement — 2026-10-14

## Scope and checkout

Baseline: `9c04cb9a`. Branch: `codex/space-import-register-refinement`.
Worktree: `C:\dev\stalltrack-v2-worktrees\space-import-register-refinement`.
The local task commit is the commit containing this document; obtain its SHA with `git log -1` on this branch. No merge, push, deployment, migration, or production access is part of this task. Main checkout and its eight protected modifications were left untouched and unstaged.

This continues `KANMANGGAY_IMPORT_REVIEW_REPAIR_20261014.md`. It changes presentation and client preview synchronization only. Backend requests, numbering, tenant authorization, persistence, and financial rules are unchanged.

## Causes and implementation

- Inline server explanations expanded Status cells. `ImportStatusDetails` now renders a compact badge and accessible button associated with the exact message in a native auto popover. Hover, focus, click/tap, Escape, and outside dismissal were exercised. Fixed positioning follows its anchor on scrolling/resizing, clamps to viewport edges, and flips above when needed. Native top-layer placement avoids scroll-container clipping without changing table dimensions ([Popover API](https://developer.mozilla.org/en-US/docs/Web/API/Popover_API/Using)). The drawer retains full validation and existing field errors.
- The compact monthly register omitted contract columns. It now projects the existing `RentalImportColumns` metadata into the requested 14-column register: row, occupant, contract name, number, lease type, effectivity, years, area, contract amount, actual approved amount, derived whole-year amount, area location, status, actions. Contract reference, closure, and ledger-controlled delinquency remain in the drawer. No duplicate canonical column metadata was introduced.
- Compact native selects retain canonical values with Contract/Space only labels, a single existing caret, token-based focus/hover styling, and adequate padding. Compact numeric cells allow fractional area/rates; duration retains an integer step.
- Opt-in `PreserveColumnWidths` contains desktop scrolling within the register; the existing container-responsive cards remain. Status/action minimum widths keep controls on one line. Default NCC/TCC grids do not opt in and retain their existing layout.
- `AddRow` did not schedule preview, and older asynchronous previews could overwrite newer edits. Every change/add/remove now invalidates the prior preview revision and hints. Only the current revision may apply results. Returned row numbers must cover the candidate batch exactly once before association. Pending checks disable import. Unavailable/malformed checks retain the existing honest fallback and final server validation.
- Rapid drawer navigation exposed stale focus references and could terminate the Blazor circuit. Connected-target focus with current-editor lookup avoids that race and prevents delayed restoration from taking focus behind a newly opened editor.

## Numbering and date provenance

`ObligationWorkflow.PlanSpaceImportAsync` and `SpaceAccountNumbering` were inspected. Existing tenant/source records and supplied batch numbers already govern suggestions; event scope includes event/date. Preview does not reserve a number, and save allocates/checks again. No backend numbering defect requiring a change was found.

The client shows `Auto · pending` during refresh, `Auto · N` only from authoritative ServerSuggested facts, and `Auto` when unresolved. Explicit identifiers remain unchanged; Vegetable/Fruits retains server-only numbering. Suggestions are never assigned to `SubjectLabel` or submitted as office-supplied identifiers. Preview still sends only locally well-formed candidates, as before; hints describe that checked batch, not invalid rows.

Only the manual row factory defaults monthly effectivity to `PhilippineTime.Today` in `yyyy-MM-dd`. Both contract and space-only added rows receive it. Uploaded missing/historical dates, edits, revalidation, lease changes, and row navigation are not defaulted or rewritten. Fiesta/Araw event dates remain blank for a new manual row.

## Business safeguards and remaining policy issue

The table and drawer edit the same row model. Space-only changes still clear/lock contract-only values. Actual approved monthly rent remains separate from contract evidence; whole-year rent is a derived read-only display, and delinquency remains ledger-controlled. Existing three-year validation, Ready-only submission, retry identity, server checks, CSV/XLSX handling, and billing-start normalization remain intact. Fiesta/Araw retains event, event date, and one-time amount, with no monthly contract fields; Vegetable/Fruits retains optional evidence and server numbering.

Inspection found a pre-existing backend policy gap: bulk planning does not call `SignedContractDurationProblemAsync`, the municipality-confirmation guard used by individual creation/correction. The domain also enforces the Kanmanggay three-year term by kind. This task does not broaden or repair that policy. A separately authorized backend review should reconcile bulk import with the confirmed-tenant policy before asserting equivalent policy enforcement for unconfirmed municipalities. No contract duration is automatically generated by this UI.

## Files changed

- `SpaceHolderImport.razor`: monthly columns, shared status feedback, pending/latest preview handling, manual defaults, drawer focus lifecycle.
- `SpaceHolderImport.razor.css`: remove permanent status styles and prevent action text wrapping.
- `ImportGrid.razor`: opt-in retained widths, select placeholder, compact numeric steps.
- `ImportGrid.razor.css`: scoped register/selector/action sizing; default grids preserved.
- `ImportStatusDetails.razor` and `.razor.css`: accessible floating server feedback using existing tokens.
- `wwwroot/js/import-status.js`: popover positioning/interactions and safe connected drawer focus.
- `ObligationWorkspaceTests.cs`: regression assertions and pending-aware submission helper.
- This scoped handoff; no protected planning document changed.

## Verification

- Focused `ObligationWorkspaceTests`: **68 passed, 0 failed**.
- Full Release Head Portal component suite: **1,218 passed, 0 failed, 0 skipped**.
- Client Release build, `--no-restore`: **0 warnings, 0 errors**.
- Defect proof: temporarily removing the manual-date default produced 2 monthly failures while the event case passed. Temporarily removing the post-await revision guard caused the stale-result test to show `Auto · 99` instead of `Auto · 9`. Both fixes were restored and tests passed.
- `git diff --check` passed; scoped and generated CSS brace counts were checked.

Real Chrome automation used the actual feature components/scripts/styles in an isolated disposable API fixture, not production or authenticated financial records. At **1440, 1280, 1024, 768, and 390px**, all three import families passed review-table/card and drawer checks (15 viewport pairs): no page-level horizontal overflow, contained monthly scrolling, correct fields/actions, stable desktop row heights, accessible feedback, correct holder identity, drawer/footer sizing, and focus restoration. Additional checks covered hover/focus/keyboard/touch/outside/Escape, right/bottom viewport edges, rapid Previous/Next, synchronized editing, three-year correction, space-only clearing, read-only amounts, remove, fixture import, and manual defaults. Default NCC/TCC shared-grid fixture also passed all five widths without Compact enabled; this is not a full authenticated NCC/TCC workflow test.

Fixture evidence is outside Git at `C:\dev\stalltrack-v2-worktrees\space-import-register-qa`: `check.cjs`, `browser-results.json`, and viewport screenshots. QA uses disposable source records and the existing canonical number helper; it performs no database writes. Chrome was tested; other browser engines were not.

The **full feature Head Portal** is separately running at `https://localhost:7569/login`, using the existing local API `https://localhost:7597`. Login and API health returned HTTP 200. After signing in, visit `/operations/kanmanggay/import`; Vegetable/Fruits and Fiesta/Araw remain available through normal navigation. No authenticated import was submitted by the agent. Full-portal imports use the local API/data and are real writes, unlike fixture checks. Existing services were not restarted or stopped; only this task's disposable QA process was restarted. No committed launch settings were changed.
