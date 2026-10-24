# CORE BRAIN V15 — continuation checkpoint (2026-10-25)

## Working tree
- Device: DESKTOP-93OK679
- Worktree: `C:\dev\stalltrack\v14-revenue-next-20261024`
- Branch: `feat/v14-revenue-integrity-20261024`
- All changes remain uncommitted; no production deployment or database migration applied by V15.
- Do not discard or overwrite existing V14 work and prior progress note `docs/planning/V14_OFFLINE_NPM_STALL_INTEGRITY_20261025.md`.

## Recovered V14 changes
- Mobile rejected offline checkout: item names, button spacing, online/server-checked discard, replacement draft guidance and component tests.
- NPM Registered Fish/Meat picker: store selected existing registration ID/tax year as provenance, require exception confirmation for differing section.
- Soft-deleted stall-number uniqueness: EF mapping and PostgreSQL migration plus tests for live-only uniqueness and retained historical rows.

## Additional V15 implementation
- `EEMOCantilanSDS.Client/Components/Shared/NpmVendorRegistrationWorkflow.razor`: centralized direct registration into `Select(row)`, so ordinary occupant-list clicks **and** InitialContractId deep links enter the existing Fish/Meat registration drawer immediately for an active, unlinked, known-type occupant without possible matches.
- Retains identity review for existing links, possible matches, inactive/historical sources, missing types, and non-Head roles.
- `EEMOCantilanSDS.ComponentTests/Pages/NpmVendorAssociationTests.cs`: updated old intermediate-drawer assertions to validate direct selection while keeping explicit duplicate, existing-link, identity, and historical paths.

## Verified in this V15 session
- Focused Mobile CollectionSessionEditorTests: 70 passed, 0 failed.
- Focused Web NpmVendorAssociationTests after V15 change: 23 passed, 0 failed.
- Isolated PostgreSQL DeletedStallNumberReuseTests: 2 passed, 0 skipped, 0 failed.
- Full Web component suite: 1,462 passed, 0 failed, 0 skipped.
- Full Mobile component suite: 180 passed, 0 failed, 0 skipped.
- Targeted extra NPM/stall integration filter: 2 passed, 0 failed, 0 skipped.
- `git diff --check` clean at last check.
- Expanded isolated PostgreSQL collection-session, NPM association, soft-deleted stall and stall lifecycle regression: 136 passed, 0 failed, 0 skipped. (The 2 new reuse tests are included in this wider suite.)

## Release and remaining risks
- No full multi-target MAUI solution build was established: an earlier long-running broad build was stopped; focused project builds/tests did compile.
- No physical Android offline/online sync exercise, staging verification, or production DB migration rehearsal yet.
- Database migration relaxes uniqueness only for soft-deleted historical stalls; plan migration backup, rehearsal and release/rollback review. Old-index rollback may be impossible after reuse.
- In `OfficeCollectionWorkflow.NpmOccupancy.cs` and `OfficeCollectionWorkflow.NpmAssociations.cs`, request-resolution work occurs after commit inside an outer catch; a post-commit database error can be reported as a registration conflict even though association has committed. This is a separate pre-release integrity/reporting issue: address with explicit post-commit failure semantics and tests; do not silently change transactional effects.
- Existing dependency notices include AngleSharp NU1902 and MAUI AndroidX NU1608.
- Do not commit unrelated agents' work, run destructive database operations or deploy production without explicit authorization.
