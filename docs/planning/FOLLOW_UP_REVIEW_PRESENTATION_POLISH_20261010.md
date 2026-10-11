# Follow-up account review — presentation polish

Local feature branch `codex/vegetable-fruit-space-workspace`, starting HEAD `9190865e`. This follow-up supersedes the modal wording/layout in [the Phase 1 handoff](FOLLOW_UP_ACCOUNT_REVIEW_HANDOFF_20261010.md), not its source contracts or financial limitations.

## Changes

- Reuse the same `FollowUpAccountReview`, `VendorDrawerShell`, status pills, period reads and source-specific action orchestration. No new drawer or payment engine.
- One concise As of date; show Queue dated only when its date differs. This is a current read, not a reconstructed historical balance.
- Summary: Outstanding balance, Periods with balance, Earliest period with balance, Collected in displayed periods. The earliest label deliberately includes partial periods rather than calling them completely unpaid. Small screens retain two compact summary columns.
- Actual year groups and period amounts remain unchanged. Months render compact ledger rows; expanding one opens a full-width subordinate detail row. Eligible NPM dates appear in a bounded, collapsible Date / Amount / Status table, not an unstructured list inside the month cell. No new dates or fees are generated.
- Account information separates source, recorded billing basis, follow-up reason and coverage. It does not repeat the queue's combined status sentence. Obligation lifecycle is shown from ActiveTo; facility queue data does not prove lifecycle and is not relabeled Active by inference. Recorded contract evidence remains available.
- Other assessed charges explicitly says saved fine assessments are not supported and no fine balance is included. An absent read API cannot prove “no saved fines.” Receipt-allocation/correction limitations are one short note; period totals remain authoritative.
- Existing Close, source navigation and eligible payment actions remain unchanged. Period selection still passes the exact source account/term/month into the existing form. No payment, OR, SRC, assessment, registration or correction is created by review.

## Others / Fines / future actions

Reviewed the Phase 1/fine-design handoffs and registry/action-content handoffs. Operations already places Others after Slaughterhouse with Arrears & Delinquency and Fines & Penalties, preserving routes and official classifications; no further navigation rewrite was needed.

[Fine assessment design](FINE_ASSESSMENT_DESIGN_REVIEW_20261010.md) now records the accepted review staging structure and source-native account-selection direction. Saved-before-payment remains approved product direction, not approval of assessment permissions, effective dates, partial payments, cancellation/waiver, source eligibility or settlement allocation. Fines still has actual Penalty setup and Collected views. No fake Assessments tab, enabled Assess fine button, note writer or migration was added.

Remaining gates: fine assessment persistence and its approved API/migration; Composer settlement of an existing assessment exactly once; account-linked source eligibility; audited non-financial follow-up notes; complete allocation/correction-history read; qualifying Arrears recovery. No universal financial writer or same-name relationship is authorized.

## Verification

- Focused Web (including ARIA state checks): **54 passed / 0 failed / 0 skipped**.
- Full Web, separately: **880 passed / 0 failed / 0 skipped**.
- Full Unit, separately: **2,526 passed / 0 failed / 0 skipped**.
- Client Release: **0 warnings / 0 errors**. Existing recompilation warnings and AngleSharp NU1902 are unrelated to this patch.
- Source/generated scoped CSS: **164 files checked / 0 unbalanced**; `git diff --check` clean.
- Rendered fixtures at 1440 / 1024 / 390: twelve screenshots generated, no document overflow; nine drawer cases retain visible footers and focus containment. Inspected monthly/multi-year, NPM daily-breakdown and three-action narrow-footer screenshots. The amount summary stays compact; daily details remain subordinate and bounded.
- An initial full-suite run caught boolean ARIA rendering (879 passed / 1 failed); explicit true/false strings corrected both disclosure controls, then focused/full validation passed. An earlier focused test counted the separate settled-period group as an outstanding year; its assertion was corrected without changing the ledger.

Commands: `dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~FollowUpAccountReviewTests|FullyQualifiedName~FollowUpQueueCompositionTests|FullyQualifiedName~OperationsFacilitiesHubTests|FullyQualifiedName~OperationReportsAndArrearsTests|FullyQualifiedName~FinesWorkspaceTests|FullyQualifiedName~AriaStateAttributeTests'`; full Web with `--no-build --no-restore`; Unit `EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj` Release `--no-build --no-restore`; Client Release `--no-restore`. Ignored logs/screenshots are under `artifacts/account-review-polish-*` and `artifacts/account-review/`.

The tested Client was restarted on its existing local ports without launch-setting changes. The Codex browser redirected to Login; renewed authenticated checks were requested, and are pending rather than inferred from the previous pass. Earlier authenticated success remains evidence for that earlier build only. Owner visual acceptance is separate.

No backend/shared DTO changed; API, PostgreSQL and Mobile expansion is not required for this presentation slice. No local/production migration, push, integration or deployment.

The protected concurrent planning edits are untouched and must remain unstaged: ACTIVE_WORKSTREAMS (4/0), CURRENT_RELEASE_STATE (7/1), VEGETABLE_FRUIT_WORKSPACE (10/0). Their owners retain canonical reconciliation authority.
