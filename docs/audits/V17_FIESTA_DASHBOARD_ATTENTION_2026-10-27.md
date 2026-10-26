# V17 Fiesta/Araw collection eligibility and Dashboard attention — 2026-10-27

Scope: local isolated branch `fix/v17-fiesta-dashboard-revenue-20261027`; no production modification.

## Evidence

- The supplied Mobile screenshot shows event lot dates of November 1–2, 2026 while the local date was October 27. These are **future** event dates, not past-due balances.
- `CollectionSessionSources.Browse.cs` and `CollectionSessionSources.Native.cs` already permit a closed past event with an approved unpaid obligation.
- `ObligationAccount.PeriodStarts` binds a one-off event charge to its actual event date; a future event isn't collectible through the current workflow.
- Existing PostgreSQL test `Future_fiesta_lot_is_visible_by_registered_payor_but_not_selectable_until_event_day` covers both Fiesta and Araw, partial late posting, SRC/idempotency, fully paid refusal, and tenant isolation. **This test was inspected but not re-run in this branch.**

## Changes

1. The Mobile source browser and checkout picker describe the disabled state as `Event date ... · payment opens that day` rather than ambiguous `Available on ...`. No backend financial eligibility changes.
2. Dashboard Revenue sources originally mounted `RevenueSourcePerformancePanel` without `Facilities`, unlike Financial Reports, so recurring rent rows fell through to `Legacy data included`.
3. Dashboard now lazily reads the **same selected-month financial facility report** when its Revenue sources tab is first opened, and passes that report's `FinancialFacilityRowDto` values to the shared panel.
4. A recurring source with a known facility shows its actual selected-period unpaid figure, including zero; failed/missing financial data is labeled `Balance unavailable`, never invented. Non-facility legacy status remains separate.
5. New Dashboard regression covers legacy rent with a nonzero financial-register balance and ensures only one read when switching tabs.

## Verification

- Focused `DashboardOverviewViewsTests`: 18 passed, 0 failed (after correcting new test fixture setup).
- Focused `SourceBrowserTests`: 16 passed, 0 failed.
- `git diff --check`: clean.
- Full suite, PostgreSQL suite, physical Android device, and browser smoke not run for this isolated change.

## Operational notes

- `Facilities.Unpaid` is **selected reporting-period** unpaid, not lifetime receivables. Don't label it lifetime or sum it with arrears without a dedicated reporting rule.
- Future event early payments remain intentionally blocked; allowing advance payment would be a separate business ruling and backend implementation.
- Source availability and amounts still come from tenant-scoped server quotations. No OR/SRC history, assessment, migrations, or production data changed.
- Integration, production deployment, and release-signed Android APK require separate review/authorization.
