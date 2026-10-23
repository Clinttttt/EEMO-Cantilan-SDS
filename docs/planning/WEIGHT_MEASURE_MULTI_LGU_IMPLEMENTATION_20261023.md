# Weight & Measure — independent multi-LGU configuration (2026-10-23)

## Authority and scope
Implemented as a feature-branch candidate following the 2026-10-06 Cantilan office clarification, ADR-007, and the 2026-10-23 owner request. This is not production release authorization. Existing Angular onboarding and Blazor V3 presentation patterns are retained; no global UI redesign.

- Registered Fish/Meat vendors remain source-owned, independent from NPM and Fish/Meat Vendor Fee.
- A vendor has exactly one Fish or Meat type; unregistered text cannot create weighing Collections.
- Amount = positive weighed kilograms × approved tenant/date/type rate, rounded by the server.
- Cantilan's legacy NPM Fish 1/kg and Meat 66/kg rates continue to apply until the office adopts an explicit independent configuration. Never seed them for another LGU.
- Official classification remains WEIGHT_AND_MEASURE (OR under Cantilan policy only). Other LGUs must have their own approved instrument policy before collection.

## Rate revisions
WeighingRateVersions is new, municipality-owned, effective-dated, and append-only in the application workflow. Two independent types carry explicit enabled/disabled state; disabled types carry no amount. The Head saves both choices in one database transaction. Edits take effect on the following Philippine business date; the latest revision recorded for the effective date wins. Existing rate rows and posted canonical Collections are never rewritten. An older LGU without revisions resolves its own effective NPM weighing rate for compatibility, never a reference-municipality fallback.

The SourceNative Weight & Measure posting path freezes vendor identity/type, kilograms, approved rate and rate-version ID in the calculation snapshot; offline quote versions for **unchanged legacy NPM FacilityRate rows** retain their previous JSON shape. When independent configuration becomes effective, the legacy NPM daily writer refuses *new* kilogram charges and directs collectors to registered-vendor weighing. Already-posted NPM evidence and earlier business dates remain intact.

## Onboarding and operation setup
The existing Weight & Measure card gains checkboxes and rate inputs (Fish, Meat). The version-one `weighing` draft block is carried intact from Landing via Operator review into `ActivateMunicipalityCommand.WeighingConfiguration` and seeded atomically with activation. Unknown/future block versions fail closed; legacy drafts without this block continue to activate without guessed config. The operation declaration is still a listing, not policy/collector authorization. Onboarding records a classification when Weight & Measure is selected, but **does not assume another LGU's OR policy**. Heads must approve the revenue/instrument setup and assign eligible collectors. Fish only, Meat only, both, or neither can be configured.

## Presentation and reporting
Existing `/operations/weight-and-measure` and report pages retain V3 composition. The Head can edit the tenant's Fish/Meat rates within the established operation page. New registered-vendor canonical collections are shown separately from legacy NPM fish/meat evidence. Official Monthly Income classification stays WEIGHT_AND_MEASURE; no legacy amount is duplicated into a new Collection. Historical NPM reports still use frozen evidence; no retrospective re-pricing.

## Backup and migrations
The additive migration `AddIndependentWeightMeasureRateVersions` only creates a tenant-owned table and index. The new table is included in the restorable manifest and tenant-scoped export; the restore pipeline already enumerates restorable tables from EF metadata. No existing financial table is rewritten. Migration execution on production is a separate deployment authorization.

## Verification and release gates
- Regression cases should cover tenant isolation, Fish/Meat enabled combinations, disabled availability, different rates, missing policies and registered sources, date changes, old NPM pricing, frozen posted amounts and idempotent Mobile retries.
- Run full .NET unit and Blazor component suites separately, Platform Admin and Landing suites, and PostgreSQL integration tests. Verify scoped CSS integrity and manually review existing screen composition.
- Do not promote to master or production until test results, compatibility risks and release authorization are confirmed.
- Platform registry `apps/landing/src/app/core/municipalities.registry.json` was dirty before this feature branch and is **not part of this change**. Preserve it untouched.

## Local verification results (2026-10-23)
- `dotnet build` API and Client Release: 0 errors after repairing corrupt *generated* static asset caches and .NET binary/reference outputs in `obj/bin`. No source or database data was removed during repair.
- `dotnet test EEMOCantilanSDS.Testing`: **3,123 passed, 0 failed**.
- `dotnet test EEMOCantilanSDS.ComponentTests`: **1,417 passed, 0 failed**, including historic NPM frozen-weighing presentation.
- `nx build landing` and `nx build admin`: passed.
- `nx test admin`: **158 passed, 0 failed**, including explicit onboarding rate mapping and privacy/reveal coverage.
- `nx test landing`: **87 passed, 3 failed**, all in existing `municipality.service.spec.ts` with Angular TestBed reinitialization ("Cannot configure ... already instantiated"); no production code change was made to hide these failures.
- No PostgreSQL migration was applied and no production/local financial records were mutated. No authenticated E2E browser walkthrough was run; independent W&M posting and year-boundary scenarios still require environment-backed integration verification.
- The Head UI preserves V3 shell, historical NPM frozen-rate and unresolved-kilo indicators; the operator's review page masks submitted Fish/Meat rates until the existing reveal control is used.
