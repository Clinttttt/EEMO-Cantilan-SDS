# NPM / Fish-Meat cross-registry import review — 2026-10-23

## Scope and authority

Reviewed current EEMO `master`, existing NPM `BulkImportStallholdersCommandHandler`, Fish/Meat `OfficeCollectionWorkflow.Import`, and `NPM_FISH_MEAT_OPTIONAL_LINK_HANDOFF_20261010.md`. Both registries remain independent under ADR-007. Equal names are suggestions, **not identities**. This work does not backfill, mutate Collections/OR/CT, or install schema.

## Fish/Meat import

- Preview now exposes possible active NPM occupancy matches with exact ContractId/StallId and an already-linked indicator. A name match alone does not create any link.
- Existing registration candidates are returned for the selected annual record. Head may select an **exact Active registration of the same tax year, vendor type, and normalized name**. Server revalidates this decision inside the existing serializable import-save transaction; it returns the existing registration ID, with `Reused=true`, without making a new row.
- Invalid, stale, conflicting, wrong-year/type, closed/deleted and duplicate reuse selections cannot save. Import confirmation for a genuinely distinct registration remains available. The UI can omit deliberately skipped rows; a request with no remaining rows is not saved.
- Successful rows with possible NPM matches link to the existing exact-contract office workflow. Association requires Head confirmation and server validation. No automatic association or payment posting occurs.

## NPM import

- NPM-only review retrieves registered Fish/Meat names for the current tax year, showing a compact advisory on possible matching occupants. Other facility imports remain unchanged.
- After saving, successful NPM contracts with possible matches offer a Head-only link-review route using the **persisted OccupancyId**. The existing NPM association workflow owns source-version, tenant, status, role, audit and replay validation. NPM import continues creating/renewing stalls and contracts only, never annual Fish/Meat registrations.
- If registry discovery fails, the NPM importer says the review is unavailable, but continues its original independent occupancy import semantics. It does not report a link as confirmed.

## Restrictions and release

No new database migration, financial writer, new person master, cross-tenant match, automatic link, backfill, payment, or collection policy change. A correct match can remain unlinked until explicitly confirmed by the Head. Runtime desktop/Android acceptance is separate from bUnit and disposable PostgreSQL tests. Nothing was pushed or deployed as part of this local development task.

## Verification on primary local `master`

- API/Head Release build: passed (existing warnings remain).
- Full Unit suite: **3,075 passed**, 0 failed/skipped.
- Full Head component suite: **1,414 passed**, 0 failed/skipped.
- Focused disposable PostgreSQL suite: **2 passed**, 0 failed/skipped (existing annual reuse, correct/wrong type, NPM candidate without automatic link).
- Import component slice: **45/45 passed on retry**. The initial parallel test build hit one intermittent prior upload-header assertion; the isolated rerun and focused import rerun passed. A new NPM UI test initially needed NPM rate mocks; updated fixture passed.
- `git diff --check`: passed. No runtime browser acceptance or signed APK test performed.

## Primary implementation files

- `EEMOCantilanSDS.Application/Common/Revenue/OfficeCollectionWorkflow.Import.cs`
- `EEMOCantilanSDS.Application/Dtos/Mobile/VendorRegistryImportDtos.cs`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/FishMeatVendorImport.razor`
- `EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/ImportStallholders.razor`
- Import bUnit tests and `CollectionSessionTests.Productivity` / `CollectionSessionTests.NpmVendorLinks` PostgreSQL tests.
