# Utility approval and SRC trace refinement — October 14, 2026

Branch: `fix/ecf-wcf-approval-accountability-src-20261014`
Starting point: `9c04cb9a`

## Findings

The Accountability form previously called `IOfficialReportsApiClient.TraceDocumentAsync` for every entered value. That authoritative reader queries `AccountableDocuments.DocumentNumber` (physical OR/CT serial), not `Collections.ReferenceCode` (SRC). Thus a canonical collection such as `SRC-2026-000016` can be visible in Collection Activity yet return "No document" in Accountability. This is an identifier-type mismatch, not evidence that the collection or its remittance is absent.

## Change

* Utility ECF/WCF preparation review: compact copy, smaller contextual sentence, separate responsive Back and Save approved amount buttons rather than a joined button group. `PrepareAsync` workflow and confirmation behavior unchanged; no collection or SRC is created by approval.
* Accountability now labels its input "Trace a serial or SRC". Physical OR/CT still uses the existing custody/document reader. Well-formed SRC uses the existing tenant-scoped, exact Collection Activity reference lookup (independent of the report month), then uses the existing official collection details reader for original amount, revenue lines, linked physical document if any, and active remittance coverage/status.
* A collection without active coverage is labeled "Not yet covered by a remittance"; a covered collection displays the actual remittance status and link. This does not infer a remittance from the posting state. Invalid or unknown references get type-specific messages. No database, API, or billing changes.

## Verification

* Focused affected suite: 49 passed on initial run, with one newly introduced test-fixture assertion failure; the isolated invalid-SRC test passed after clarifying its fixture. Full Head Portal component suite then passed: **1,208 passed, 0 failed, 0 skipped**.
* Client Release build succeeded: **0 errors**, existing compiler warnings.
* Tests cover covered and uncovered SRC, invalid SRC, existing physical serial traces, and UI review text/buttons. Authenticated visual acceptance and real tenant-specific SRC lookup remain manual follow-up.
* No production deployment, migration, financial write, push, or main checkout changes. Codex's concurrent space-import refinement worktree and the eight protected main-checkout modifications were left untouched.
