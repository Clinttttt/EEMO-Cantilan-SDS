# Remittance and Monthly Income evidence — second-pass verification (2026-10-28)

**Branch/worktree:** `fix/v18-sidebar-navigation-render-20261027`, `C:\dev\stalltrack\v18-sidebar-navigation-render-20261027`.
**Scope:** Defense-in-depth review of `d2b9f718`, local only; preserve main checkout, production, and canonical accounting data.

## Reviewed invariants

- Remittance status persists as `Recorded` or `Voided`; a nonzero `DifferenceAmount` is separate persisted financial evidence, **not a correction** and not a third status.
- Remittances are immutable except an accountable, reasoned void; no source Collection, SRC, receipt allocation, Month-End or Monthly Income actual is updated by this feature.
- The authorized `RemittancesController` requires Head/Admin roles; `RemittanceWorkflow.GetRegisterAsync` filters by the current municipality, year dates, and identity at the application boundary.
- Monthly Income revisions come from their existing append-only governance reader. Remittance rows come from their authoritative register, remain distinguishable, and link to original remittance details.
- Export uses the existing `CsvCell.Row` sanitizer. Where the recorder/voider was not stored, the UI does not guess an identity.

## Findings addressed after the first implementation

1. **Read availability coupling:** The first combined view suppressed *all* report revisions if remittance evidence could not be fetched. The second pass now preserves already-returned report revisions with an explicit `role=alert`, disables combined CSV/print when remittances are unavailable, and never claims the source list is complete in that state. Added tests for an API failure result and a thrown exception.
2. **Route-year drift:** The revision page previously loaded only on initialization, so navigating from `?year=2026` to `?year=2025` could display stale data with the new year selected. It now loads when the routed year parameter changes and ignores late responses from superseded requests. Added a navigation-based regression test verifying both report and remittance reader dates.

## Verification evidence

- Focused web component regression suite after second-pass changes: **36 passed; 0 failed**.
- Backend Release suite after the financial display changes and before the second-pass Razor-only fix: **3,273 passed; 0 failed**.
- Full web component regression suite after second-pass changes: **1,511 passed; 0 failed; 0 skipped**.
- `git diff --check`: must be rerun before commit; no migrations or financial writes authorized.

## Release/acceptance limitations

No production deployment, schema migration, correction, backfill, or mutation of authoritative financial records. Still requires an authenticated browser check with the permitted local tenant to validate responsive column layout, filter/CSV/print behavior, and the actual actor names on existing records. Local API readiness is a transport/database-connection check, **not** a substitute for end-to-end transactional validation. The existing dependency vulnerability warning for AngleSharp is unrelated to these changes but should remain in the release backlog.
