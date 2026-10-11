# Optional verified NPM / Fish-Meat registration association

Date: October 10, 2026. Starting branch `codex/vegetable-fruit-space-workspace`, HEAD `c1573752`.

## Accepted scope and authority

The project owner authorized an optional, office-confirmed association between an exact NPM contract and an independent annual Fish/Meat registration. This is an additive refinement to IA-068, not a reversal of its independent-registry decision. Fish/Meat vendors still do not require NPM occupancy. Equal names are never identity. No new Business Payor prerequisite is introduced.

Fine Settlement was committed at `28e15446` / `c1573752` before this task began. The existing Login, AuditTrail and Menu edits and the three protected planning-document diffs were left untouched and excluded from staging.

Relevant authority reviewed: AGENTS, CONTEXT, the V2 master specification and release checkpoint, documentation map, operational rulebook/business rules, IA-064/067/068, ADR-007, architecture/application patterns, tenant/security guidance and Web V3 direction. The optional relationship does not revise any financial classification or settlement authority.

## Persistence and server boundary

`FishMeatNpmAssociation` stores municipality, annual vendor-registration ID, exact ContractId/StallId, original occupant snapshot, source-version fingerprint, office confirmation evidence, confirming actor/time and durable operation/fingerprint evidence. Correction records operation, reason, actor and time while preserving the original confirmation. No physical deletion or reassignment is implemented.

The additive migration `20261010063119_FishMeatOptionalNpmAssociation20261010` creates only the association table and the supporting alternate contract key `(MunicipalityId, Id, StallId)`. Composite restricted foreign keys enforce the vendor municipality and exact contract/stall tuple. Unique constraints cover confirmation operation, correction operation and the active exact vendor/contract pair within a municipality. Corrected evidence remains; a later explicit reconfirmation creates a separate record. There is no global person/stall or account/month uniqueness assumption.

The table participates in global tenant filtering and tenant backup/export/restore coverage. No `IgnoreQueryFilters()` path was added. The bounded partial workflow deliberately uses the existing Office registry transaction context for atomic vendor creation plus association; its architecture-boundary allowance documents that decision.

Both API attributes and application guards require SuperAdmin for confirmation, linked creation and correction. Admin may read evidence; Collector cannot use these office reads or mutation endpoints. All contract, stall, vendor, municipality, type/area exception and source-version checks occur on the server. Historical source evidence requires explicit confirmation. A stale source version fails closed.

### APIs

- `GET api/office-sources/fish-meat/npm-candidates?taxYear=...&historical=...&exceptions=...`
- `GET .../fish-meat/registrations/{id}/npm-links`
- `POST .../fish-meat/registrations/{id}/npm-links`
- `POST .../fish-meat/npm-links/{id}/correct`
- Existing registration POST accepts an optional typed NPM confirmation. Its operation ID must match the registration operation ID; registration and evidence commit in one serializable transaction.

Serializable transactions, database uniqueness and immutable request fingerprints protect concurrent requests. A race loser may receive a conflict; retrying the same operation returns the saved result. Reusing a saved key with altered payload fails. Removing the association from a linked registration replay also fails. Invalid linkage cannot leave an orphan registration.

## Staff workflow and routes

The existing Fish/Meat toolbar adds **From NPM occupants**. Its count means current, recorded-active Fish/Meat-area NPM contracts without an uncorrected verified association to a registration in the selected tax year. It counts occupancy reviews, not people or proven unregistered vendors. History and other areas require explicit filters and are excluded from this default count. A failed count read is omitted, never represented as zero.

The shared `NpmVendorRegistrationWorkflow` uses VendorDrawerShell and the existing VendorRegistrationDrawer. Candidate rows show actual occupant, stall, area, contract reference/effectivity and recorded active/historical state. Verified links and possible exact-name matches are distinct. The latter are warnings requiring manual review; no name match is chosen or saved automatically. NPM has no reliable recorded vendor business/address in this source contract, so neither is invented or prefilled.

Staff may select any verified annual registration from the selected tax year and confirm its office evidence. This links the existing ID and creates no registration. Alternatively, reviewed distinct registration uses the original registration handler and shared form, prefilled only with the recorded occupant and Fish/Meat-area suggestion. Type remains reviewable; historical, other-area and differing-type combinations require explicit exception confirmation. Existing matches or links require duplicate-review confirmation before distinct new creation. Legitimate separate businesses/types and multiple stalls remain possible.

The existing NPM profile offers the same workflow with a server-resolved exact ContractId. It does not guess from names or stall number. Ambiguous active source records omit this shortcut and remain available for explicit review in the source drawer.

Deep links:

- `/operations/fish-meat-vendor-fees?taxYear=...&npmContractId=...`
- `/operations/fish-meat-vendor-fees/manage?taxYear=...&registrationId=...`

Registration details show optional NPM evidence and original occupant. A current, active exact source may open the existing NPM profile. Historical source links open the exact occupancy/link review instead of presenting the replacement occupant's current profile as that historical account. Corrected evidence is readable in that review, including reason and actor.

New/Renew/Active/Closed behavior remains source-owned. Renewal creates the existing legitimate new annual identity with no copied association. Staff may explicitly reconfirm the relationship for that year. Neither NPM closure nor vendor closure changes the other source. Imports remain independent and retain their existing review/validation; no import backfill or mass linking is performed.

## Duplicate limitations

Exact association and retry duplication are server-enforced. Real-world business identity cannot be proved from names, because no verified universal incident/person/business key exists here. Similar names and existing links therefore prompt explicit office review; a Head-confirmed distinct business/type may still be registered. This is not a claim of perfect real-world duplicate-person prevention.

## Financial safety

No collection writer, source discriminator, rent rule, rate, receipt policy or reporting calculation was changed. These registration commands do not create Collections, SRCs, allocations, fees, rental periods or payments. They neither close/reopen NPM accounts nor transfer historical liability. Existing revenue and immutable cash records stay on their original sources. PostgreSQL tests check no new Collection/allocation/obligation-period side effects.

## Verification

- Focused link PostgreSQL tests: **12 passed**, zero failed/skipped. Real PostgreSQL 16 Testcontainers; blank disposable `stalltrack_tests`, full migrations, container disposal.
- Full PostgreSQL suite: **421 passed, 7 skipped, zero failed**. Skips are optional snapshot-data scenarios; no local tenant connection was supplied.
- Authorization regression proof: temporarily removing the server Head guard made both Admin/Collector cases fail; restored guard passes.
- Focused Fish/Meat/link Web tests: **40 passed**, zero failed/skipped (existing import and registry tests included).
- Full Unit suite: **2,529 passed**, zero failed/skipped.
- Full Web suite: **905 passed, 1 failed, zero skipped** (906 total). The sole failure is `AriaStateAttributeTests.NoAriaStateIsBoundStraightToABool` in the unrelated concurrent AuditTrail boolean ARIA bindings; that page was not edited by this task. This prevents claiming a completely green Web suite.
- Client/API Release builds: no errors. Existing recompilation/nullability/analyzer and AngleSharp advisory warnings remain unrelated to the association.
- Source/scoped CSS braces and `git diff --check` verified.

Rendered component fixtures cover source review and existing registration prefill at 1440, 1024 and 390px. Screenshots were inspected; document width matched the viewport and fixed footer buttons remained within it. Screenshot inspection caught and corrected a literal string/error binding and HTML disabled-attribute binding; regression assertions now check those controls. These fixtures are not authenticated runtime acceptance.

### Execution commands

```powershell
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj --configuration Release --filter 'FullyQualifiedName~CollectionSessionTests.Npm_'
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj --configuration Release
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --filter 'FullyQualifiedName~NpmVendorAssociationTests|FullyQualifiedName~FishMeat'
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj --configuration Release
dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj --configuration Release
git diff --check
```

## Activation and remaining acceptance

Migration authored and tested only on disposable PostgreSQL. No existing local Cantilan or hosted database was migrated, and no API was started against either database. Authenticated end-to-end acceptance of the new association remains pending an explicitly approved disposable runtime/fixture environment. Earlier authenticated reviews of other features do not verify this new schema/UI.

No push, integration, deployment, APK publication, financial backfill or automatic linking. Next increment is authenticated source selection, existing-link/new-registration/retry/correction acceptance on a disposable runtime, followed by separately authorized release/migration decisions.

## Source-control checkpoint and exact inventory

Implementation/test commit: `43b05669` (`feat(registry): add verified optional NPM Fish-Meat links`), 30 files. Documentation is committed separately. Branch remains `codex/vegetable-fruit-space-workspace`. No protected planning file, Login, AuditTrail or Menu edit is included.

```text
EEMOCantilanSDS.Api/Controllers/Revenue/OfficeSourcesController.cs
EEMOCantilanSDS.Application/Common/Interface/ApiClients/IOfficeSourcesApiClient.cs
EEMOCantilanSDS.Application/Common/Interface/Persistence/IAppDbContext.cs
EEMOCantilanSDS.Application/Common/Revenue/OfficeCollectionWorkflow.NpmAssociations.cs
EEMOCantilanSDS.Application/Common/Revenue/OfficeCollectionWorkflow.cs
EEMOCantilanSDS.Application/Dtos/Mobile/NpmVendorAssociationDtos.cs
EEMOCantilanSDS.Application/Dtos/Mobile/SourceNativeCollectionDtos.cs
EEMOCantilanSDS.Client/Components/Pages/Menus/FishMeatVendorFees.razor
EEMOCantilanSDS.Client/Components/Pages/Menus/FishMeatVendorManage.razor
EEMOCantilanSDS.Client/Components/Pages/Shared/Actions/Profile.razor
EEMOCantilanSDS.Client/Components/Shared/NpmOccupancyVendorContext.razor
EEMOCantilanSDS.Client/Components/Shared/NpmOccupancyVendorContext.razor.css
EEMOCantilanSDS.Client/Components/Shared/NpmVendorLinkedContext.razor
EEMOCantilanSDS.Client/Components/Shared/NpmVendorLinkedContext.razor.css
EEMOCantilanSDS.Client/Components/Shared/NpmVendorRegistrationWorkflow.razor
EEMOCantilanSDS.Client/Components/Shared/NpmVendorRegistrationWorkflow.razor.css
EEMOCantilanSDS.Client/Components/Shared/VendorRegistrationDrawer.razor
EEMOCantilanSDS.ComponentTests/Pages/FishMeatVendorRegistryTests.cs
EEMOCantilanSDS.ComponentTests/Pages/NpmVendorAssociationTests.cs
EEMOCantilanSDS.Domain/Entities/Revenue/FishMeatNpmAssociation.cs
EEMOCantilanSDS.HttpClients/ApiClients/OfficeSourcesApiClient.cs
EEMOCantilanSDS.Infrastructure/Migrations/AppDbContextModelSnapshot.cs
EEMOCantilanSDS.Infrastructure/Persistence/AppDbContext.cs
EEMOCantilanSDS.Infrastructure/Persistence/Configuration/FishMeatNpmAssociationConfiguration.cs
EEMOCantilanSDS.Infrastructure/Persistence/Migrations/20261010063119_FishMeatOptionalNpmAssociation20261010.Designer.cs
EEMOCantilanSDS.Infrastructure/Persistence/Migrations/20261010063119_FishMeatOptionalNpmAssociation20261010.cs
EEMOCantilanSDS.Infrastructure/Repositories/SystemHealth/TenantDataTables.cs
EEMOCantilanSDS.Infrastructure/Repositories/SystemHealth/TenantExportRepository.cs
EEMOCantilanSDS.IntegrationTests/CollectionSessionTests.NpmVendorLinks.cs
EEMOCantilanSDS.Testing/Architecture/ApplicationEfBoundaryTests.cs
docs/planning/NPM_FISH_MEAT_OPTIONAL_LINK_HANDOFF_20261010.md
```
