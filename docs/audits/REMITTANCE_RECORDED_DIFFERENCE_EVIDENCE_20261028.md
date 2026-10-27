# Remittance status and revision-evidence presentation — 2026-10-28

**Scope:** Isolated Web Office/Head Portal changes on `fix/v18-sidebar-navigation-render-20261027`.
**No release implied:** not merged, pushed, deployed, migrated, or applied to production data.

## Verified model and intended presentation

`CollectionRemittance.Status` has only `Recorded` and `Voided`. An outstanding amount difference (`ExpectedAmount - RemittedAmount`) is separately persisted as `DifferenceAmount`; `NeedsReview` is a derived accountability/exception indication, **not** a remittance lifecycle status. A recorded remittance is immutable; corrections require a reasoned void and new record. A remittance is cash turnover, **not a revenue event or Monthly Income adjustment**.

The Remittance history table now has a distinct `Difference` column (showing dash if zero) and determines lifecycle text from persisted members' `Recorded`/`Voided` statuses. Detail and submission views use the same distinction. No financial money or transactional rule has changed; existing accountability exception counts remain.

Monthly Income revision evidence now contains an additional selectable `Remittance` record type. It reads the existing tenant-authorized remittance register for the selected remittance year, listing actor, collector, expected/remitted figures, difference, reference, remarks, lifecycle state, and void actor/reason when available. Records link to their authoritative remittance detail. Reporting targets/adjustments and remittances remain separate source records and are differentiated in the table, hero counts, CSV columns, and explanatory limitations.

The read DTO extends with recorded/void evidence that is already available to authorized users in the original detail endpoint. No new database columns or migrations, no new writes, no constructed revisions, and no historical actor inference. The combined view fails closed rather than implying complete evidence if either source fails.

## Verification

- Focused Web component suite: **34 passed, 0 failed**, after correction of a Razor attribute expression and preservation of the existing report-limitation wording. Tests cover displayed Recorded with nonzero difference, tenant-read contract consumer behavior, genuine report revisions, filtered Remittance record type, CSV source IDs, voided-group rendering, and failure path.
- Backend Release unit suite: **3,273 passed, 0 failed**.
- Full Web Component suite (Release): **1,509 passed, 0 failed**.
- Static check: `git diff --check` clean; no migrations or database files touched.

## Remaining validation

Authenticated browser review of remittance history and Monthly Income evidence at a disposable or authorized local tenant; confirm responsive table, screenshot presentation, and actor/remarks from actual read-only data. No production deployment authorization given. Build/test passing cannot alone verify interactive browser navigation or release safety.
