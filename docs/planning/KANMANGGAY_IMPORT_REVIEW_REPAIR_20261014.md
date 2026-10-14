# Kanmanggay import review repair — local implementation evidence

Date: 2026-10-14. Baseline: `ea101648`. Branch: `codex/space-import-review-repair`.
Worktree: `C:\dev\stalltrack-v2-worktrees\space-import-review-repair`.
This is verification evidence for a presentation repair, not a new business rule or production release.

## Cause and correction

The former row-action summary joined plain text directly to a Razor conditional (`Details@if ...`). A rendered bUnit regression reproduced that literal source in Kanmanggay, Vegetable/Fruits and Fiesta/Araw: **3 failed / 0 passed** before the fix. The replacement button uses one evaluated label: **Details** for Ready, **Review** otherwise. Remove remains a separate button.

The monthly review list now shows row number, actual occupant, space number, lease type, effectivity, approved actual monthly rental, status and actions. Secondary register fields use the existing `VendorDrawerShell`, grouped form classes and blue header. The drawer identifies the row and holder, retains edits immediately in the review list, supports previous/next row, Escape/Close/Back to review, and restores focus to the originating action. Existing validation reasons remain readable in the list and drawer; known field errors are associated with their inputs.

`ImportGrid.Compact` is opt-in. It removes permanent minimum column widths and uses labelled stacked rows when the containing workspace is narrow. The visually hidden table headers remain available to assistive technology; the card repeats their labels visibly. Other import workspaces keep their existing grid presentation. `RentalImportColumns` remains the source of register metadata; the full upload/template fields remain unchanged.

Fiesta/Araw keeps Event, Event date and approved one-time amount. Its editor does not offer monthly contract fields. A final template check also exposed an existing header-reader mismatch: `Approved Event Amount (₱)` was absent from the recognized amount aliases. That existing template header now binds to the same approved amount field for CSV/XLSX header processing. A regression reads the actual template and verifies the one-time amount/date and null contract fields; it failed before the fix and failed again when the alias was temporarily removed, then passed with the alias restored. Vegetable/Fruits retains server-controlled numbering and optional recorded contract terms. Non-Ready server states remain distinct and are counted as needing review rather than being misreported as Ready or Invalid.

## Safeguards

Apart from the existing event-template header alias, parsing/normalization is unchanged. No changes to billing calculations, local contract validation, typed server preview, account requests, Ready-row filtering, import operation IDs or backend code. Kanmanggay's existing three-year validation is preserved. Approved actual monthly rent and contract-stated rent remain separate. Whole Year Rental remains derived and read-only; delinquency remains read-only and is never imported. Signed versus space-only clearing/locking follows the existing behavior. Closure information still binds to the existing request field.

## Verification

- Focused `ObligationWorkspaceTests`: **55 passed, 0 failed, 0 skipped**.
- Full component suite: **1,167 passed, 0 failed, 0 skipped**.
- Client Release build, `--no-restore`: **0 warnings, 0 errors** on the final standalone incremental build. Earlier compilation emitted existing repository warnings.
- `git diff --check` and brace checks on both edited scoped stylesheets and the generated Client CSS bundle passed.
- Real headless Chrome, interactive Blazor fixture host, at **1440, 1280, 1024, 768 and 390px** for **all three operation modes**: review list and row editor (15 viewport pairs). Checks cover page/grid/editor/footer overflow, mobile form stacking, raw Razor absence, keyboard Enter opening, correct row identity, focus return, edits retained through previous/next, closure-date editing/scrolling, Escape closing, removal/renumbering, fixture-only import and CSV upload. Kanmanggay included **1 Ready / 2 Invalid** rows, a server duplicate and an approved-amount review reason; changing a term to two years and back tested revalidation. **Zero browser errors**. XLSX was not separately uploaded in the browser; the existing reader and shared header mapping remain covered by automated checks.

Browser evidence and disposable harness: `C:\dev\stalltrack-v2-worktrees\space-import-review-qa` (`browser-results.json`, review/editor PNGs and `check.cjs`). The host renders the actual shared components and current portal styles with an administrative layout fixture. It uses fake API responses and no database or persisted account data. It is not an authenticated integrated portal session.

## Local viewing and ownership

The disposable interactive preview is at `http://127.0.0.1:5298/`; use `?kind=vf` or `?kind=event` for the other modes, then **Use sample data**. All imports there are fixture-only. Restart only that QA host when rebuilding its referenced Client assembly; refresh normally for CSS updates. Existing Head Portal/API processes were not stopped or restarted. The actual `/operations/kanmanggay/import` wrapper and role authorization are unchanged and covered by the component suite; integrated authenticated portal acceptance remains a manual checkpoint after authorized integration.

The eight protected main-checkout modifications remain untouched and unstaged. No unrelated worktree edits, merge, push, deployment, database access or migration occurred. Changes are local to this feature branch.
