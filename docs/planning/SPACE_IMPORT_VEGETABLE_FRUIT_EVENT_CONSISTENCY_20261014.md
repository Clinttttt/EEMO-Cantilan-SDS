# Vegetable/Fruit and event import consistency — 2026-10-14

Baseline `61294a23`; branch `codex/space-import-register-refinement`, isolated worktree `C:\dev\stalltrack-v2-worktrees\space-import-register-refinement`.

The owner requested applying Kanmanggay's contract-entry presentation to the remaining space imports, then explicitly confirmed **Fiesta/Araw stays event-only**. The canonical [contract clarification](../business/MEEDO_CONTRACT_CLARIFICATION_20261009.md) records this scope.

## Implementation

`SpaceHolderImport` now uses one monthly manual-entry workflow for Kanmanggay and Vegetable/Fruit: no Lease type selector in either review table or row drawer, one Add row with contract action, three-year manual default, editable actual contract evidence, and the existing FhSelect presets/custom control. Contract evidence entered explicitly on an uploaded unsigned/unknown monthly row selects signed arrangement internally; simply opening/editing its occupant or approved amount does not. CSV/XLSX values, historical nullable terms, explicit unsigned arrangements, dates, and numbering requests are preserved. No default is applied by the upload parser.

Vegetable/Fruit retains server-controlled numbering, independent actual approved amount versus contract-stated amount, derived/read-only whole-year rent, and ledger-controlled delinquency. Its Add New form also defaults to three years; existing selectable actual/custom terms and unknown historical evidence remain supported. No API/domain/schema/accounting calculation changed in this follow-up.

Fiesta/Araw already had an Event column. It is now the first editable column, after row number, in the table and row editor. This puts the Fiesta/Araw choice where users can identify the event immediately without duplicating it. Occupant, lot number, event date, approved event amount, compact status, and actions retain the existing components/styling. CSV template/parser order is unchanged. No monthly rates, contract years, fabricated contract evidence, or monthly date defaults appear in event mode.

## Verification

Actual results are recorded in the final task report. Tests cover both monthly families' three-year defaults, absence of Lease type selectors, recorded custom-term payloads, independent approved amount, preserved uploaded unsigned rows, server numbering, and Event-first headers/selection with event-only payloads. Browser automation uses the actual feature components in the existing disposable fixture at all five required widths; it is distinct from an authenticated portal import.

- Full Release component suite: **1,226 passed, 0 failed, 0 skipped**. The final run used process-local `DOTNET_PROCESSOR_COUNT=2` to reduce existing parallel debounce timing flakes; no committed runner settings changed. The new payload test now waits for its duration menu to finish rendering before selecting Custom.
- Focused obligation component suite: **70 passed** before the additional Vegetable/Fruit Add New default test; that additional test is included in the final full suite.
- Client Release build succeeded with **0 errors**. A recompiling run emitted existing repository warnings; the final incremental build is reported separately. No unrelated warning fixes were made.
- Chrome fixture checks passed for all three import families at **1440, 1280, 1024, 768, and 390px**: contained scrolling/cards, drawer sizing/focus/navigation, custom seven-year edits, invalid zero-year correction, table/drawer synchronization, Ready-row import/removal, status popup keyboard/touch and edge positioning. The default NCC/TCC shared-grid and existing blue-hero fixtures also passed all five widths. Fiesta/Araw has Event first and remains event-only.
- Existing fixture evidence is outside Git at `C:\dev\stalltrack-v2-worktrees\space-import-register-qa`, including `browser-results.json`, screenshots, and the final test TRX. No authenticated financial import was submitted by the agent.
- `git diff --check` and generated scoped CSS brace verification passed. Main checkout retains its eight protected modifications, unstaged and untouched.

Files: `SpaceHolderImport.razor` (shared monthly contract workflow and Event-first presentation), `ObligationWorkspace.razor` (Vegetable/Fruit Add New duration default), `ObligationWorkspaceTests.cs` (supported payload/default/evidence regression assertions), `MEEDO_CONTRACT_CLARIFICATION_20261009.md` (owner's confirmed scope), and this handoff. The local task commit is the commit containing this document.

The full portal remains `https://localhost:7569/login`, using the existing feature API on `https://localhost:7598` with startup migrations/seeding disabled. Only this task's portal and disposable QA processes are restarted to load the changed Client. Protected main-checkout files and other worktrees/services are untouched. No migration, production access, deployment, push, or merge is performed.
