# Custom Facility monthly category — implementation handoff (2026-10-23)

## Decision, scope and source of authority

Per the 2026-10-22 owner clarification in `TENANT_TERMINAL_CUSTOM_RENTAL_WORKSTREAM_20261022.md`, Custom1–Custom5 retain their **existing monthly Facility / Stall / Contract / PaymentRecord engine**. Each facility now declares a stable **Stall Rent** (default) or **Space Rental** category only for operation/registry/report identity. No new obligation writer, fee rate, OR/CT policy, tenant identity or collection calculation is created.

**Backwards compatibility:** Existing custom facilities and earlier Platform onboarding drafts default to Stall Rent (numeric zero). A tenant cannot change an activated facility's category. Old financial records and history are never retroactively rewritten. Only custom codes may declare Space Rental; the server rejects unknown enum values and built-in space declarations.

## Implemented

- Domain/EF: `Facility.CustomCategory` (tenant-owned entity), `CustomFacilityCategory` enum, EF migration `20261023025930_AddCustomFacilityRentalCategory20261023`: one `Facilities.CustomCategory integer NOT NULL DEFAULT 0` column. The EF snapshot is current. Production migration was NOT applied.
- Landing onboarding: separate `Add new space rental` under Space Rental, draft category selector, shared five custom-slot cap, unfinished validation, draft preservation and display grouping. Both categories stay MonthlyRental.
- Platform Operator: validation preview shows each custom type. Activation mapper maps category with `Custom1`–`Custom5` stable codes, rejects unknown categories and noncustom Space Rental.
- Backend activation and Head Add Facility: tenant-scoped category stored at create; default ensures legacy compatibility; validators reject invalid declarations.
- Head: Facility Configuration category selection and display; Operations shows Custom Space under Space Rental and custom stalls under Stall Rent. Existing CustomFacility monthly contracts, payment and report routes are retained.
- Collector: tenant-scoped custom category exposed on Mobile menu DTO/description; monthly rental eligibility, route and server-approved collection behavior are unchanged.
- Account-level Month End Report: custom facility detail displays Stall Rent or Space Rental badge. **Official** `RENT_OTHER` classification and reconciliation remain unchanged pending explicit LGU approval; do not silently move or split this row into official Space receipts.

## Verification evidence

- API Release build: passed (23 pre-existing warnings on the recorded run).
- Head Release build: passed (104 warnings on the concurrent run; no errors).
- Landing production build and Admin production build: passed.
- Admin Angular: **155/155** passed.
- Targeted Landing onboarding: **12/12** passed.
- Full Landing: **87 passed / 3 failed**, isolated in `municipality.service.spec.ts`; the failures reproduce when the test is isolated and involve pre-existing municipality-registry expectations (`Upcoming` vs `Active`) and TestBed teardown. The user-owned dirty `apps/landing/src/app/core/municipalities.registry.json` is **preserved and excluded from this work**. Do not assert entire Landing suite green.
- Full EEMO unit suite: **3,078/3,078** passed at that checkpoint; a later focused added suite for Head/activation guardrails passed **34/34**.
- Operations bUnit: **14/14** passed; Custom Space/Custom Stall group and route assertions included.
- Monthly report bUnit: new Custom Space account-label/Pending-preservation test passed after targeting the correct account header.
- Full Head component suite: **1,415 passed / 1 failed**, existing Terminal retry test on the broad run; isolated Terminal retry filter **2/2** passed. Treat as a possible timing flake, not proof full suite green.
- Disposable PostgreSQL activation test was **SKIPPED**, not passed: Docker Desktop Linux engine was unavailable. No live database was used as a test fixture.
- EF has-pending-model-changes: none. Idempotent script generated at `C:\dev\stalltrack\_archive\custom-category-migration-script-20261023.sql`; git diff whitespace checks passed.

## Release restrictions and known limits

1. Do **not push EEMO master before installing the new additive EF migration on production** or running a coordinated schema-first deploy gate: the new code's ordinary Facility reads require the new column. Production database changes require an explicit backed-up migration release with verified target DB; no production database, GitHub release, AWS host, or APK was modified by this development task.
2. Platform local `main` has previously merged PR history; the remote tip is not necessarily its tracking branch. A local `municipalities.registry.json` modification was present before this task. Reconcile remote Git non-destructively, preserve that file, and deploy Platform/Admin/Landing after compatible API/schema.
3. Before release: run disposable PostgreSQL migration/tenant-isolation/activation tests, browser onboarding->validation->activation->Head add->operations->report acceptance, and signed Android acceptance proportional to scope.
4. **Official statement classification remains an open LGU policy question.** `RENT_OTHER` stays Pending, even for a newly categorized Custom Space. Obtain an authorized classification and effective-date policy before changing official monthly rows. No automatic historical reclassification.
