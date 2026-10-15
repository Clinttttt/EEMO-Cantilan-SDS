# Mobile Records/Reports and nonfinancial NPM registration reuse

CORE BRAIN V6 handoff — 2026-10-15. Implementation is committed locally and verified as described below. Native Windows visual acceptance and municipal-database migration approval remain outstanding. This document is an implementation checkpoint, not deployment authority.

## Repository and integration

| Item | Verified value |
| --- | --- |
| Primary checkout | `C:\dev\stalltrack\eemo` |
| Primary baseline, unchanged | `40a3b1cb`, `codex/local-unified-v8-v10-20261013` |
| Completed Fish/Meat refinement incorporated | `bd442d89` |
| Isolated integration merge | `c47135dc` — contains both histories; no primary-checkout merge |
| Worktree | `C:\dev\stalltrack-v2-worktrees\mobile-reports-npm-reuse-20261015` |
| Branch | `codex/mobile-reports-npm-reuse` |
| Implementation commit | `59805b47567d7107fdb8bc811c1bc59af3dffad4` |

The eight protected primary modifications remain present and unstaged in that checkout: Login Razor/CSS, AuditTrail Razor/CSS, ACTIVE_WORKSTREAMS, CURRENT_RELEASE_STATE, DIRECT_FINE_COLLECTION_HANDOFF_20261011, and VEGETABLE_FRUIT_WORKSPACE_20261008. None was edited, staged, stashed, reset or discarded. Existing Fish/Meat refinements are incorporated through their completed Git history, not copied over with old UI code.

## Root causes and presentation changes

Records used unrelated markup for canonical operation collections and facility/legacy receipts. Both now use `CollectionRecordRow`: compact identity/amount heading, date, source/fee context, restrained status, actual partial-payment evidence and recorded receipt evidence. Canonical collections retain their real SRC; legacy rows retain verified payor/source identity and recorded OR evidence where available. Missing evidence displays “No linked receipt”; multiple receipts are counted, not synthesized into one receipt. Details, grouping and offline-history behavior remain intact. The canonical section now also receives the live search term; the actual Records page regression protects against passing a literal string to that parameter.

Reports previously gave three money figures equally cramped tiles. Server-provided Unremitted is now the primary figure, with Collected and Remitted secondary. Money uses tabular numerals and no digit wrapping; at phone width the secondary figures use full-width label/value rows. Historical pre-activation collections remain separately explained and excluded from ordinary remittance position. Forms on hand has a concise empty state. Period navigation uses accessible buttons; all four tabs retain their data. No report arithmetic or server readers were replaced.

Fish/Meat source discovery already exposed verified NPM links. The dedicated Fish/Meat page's operation display filter could nevertheless hide an NPM charge after selecting a linked stall. A narrowly scoped linked-stall handoff now switches to that stall's own server-returned capabilities, retaining assignment, quote and posting validation. Multiple links remain explicit choices. No vendor fee item is carried into the stall checkout.

The runtime Mobile API is wrapped in `CachingMobileApiClient`. New request methods must be forwarded there as well as on the HTTP client: otherwise interface defaults return “unavailable.” A failing-then-passing unit regression now protects live forwarding without financial-queue or stale-approval caching.

Browser review caught a literal `_association` label in the Head request dropdown; the Razor string parameter now binds the actual value. A component assertion protects that rendering. New drawers use the existing shell, header icons, footer, styles and Escape behavior; the parent NPM drawer is hidden while the picker is open, preserving its form model and avoiding two active modal dialogs.

## Registered Fish/Meat → NPM

NPM's new-registration footer has `Import list` and opt-in `Registered Fish / Meat` secondary actions. The registry action is SuperAdmin-only and absent when editing an existing stall. The picker reads same-tenant management registrations and verified NPM association evidence, supports search/type/year selection, shows available office identity fields and active/historical links, disables closed vendors, and prioritizes active vendors without a recorded active link. Failed evidence reads prevent selection. A generation guard prevents stale year responses replacing the current list.

Selection preserves the registration ID and reuses the recorded display name. Fish/Meat type suggests the section, which the Head may correct. It does not copy a stall, rate, area, contract name, term or effective date from the Fish/Meat registration. Existing NPM-only form values remain independently editable and validated. Business/address context is displayed read-only. Absence of a link is explicitly not proof of no NPM occupancy; names are review warnings only.

Saving requires a note and deliberate duplicate review; a classification/section mismatch requires explicit exception confirmation. The server checks active registration, tenant, fresh expected name, effectivity tax year, and operation intent. It sends the normal `CreateStallCommand` through MediatR, preserving canonical validation, vacancy, fees and occupancy rules. The canonical writer's save and existing `FishMeatNpmAssociation` insert share one serializable transaction. Failure rolls both back; replay returns the original stable stall, contract and association IDs. No Fish/Meat registration or financial posting is created by this adapter.

## Confirmed policy and request lifecycle

The owner explicitly confirmed **registration requests only; no cash before approval**, with SRC terminology for the current workflow. [Policy decision and receipt boundary](NPM_REGISTRATION_REQUEST_POLICY_20261015.md) records the answers to the cash, custody, receipt, rejection, tenancy authorization, rate and remittance questions.

Draft is client form state. Persisted statuses are `Submitted = 1` (pending review), `Approved = 2`, `Returned = 3`, `Rejected = 4`. Submission records tenant, collector ID/name, vendor registration ID/year/name, operation fingerprint, proposed stall/section, note and server timestamps. Review records the operation ID, expected version, reviewer name/time, reason, and verified association for approval. Terminal decisions are immutable. Returned submissions are corrected through a new request referencing the old record; the old evidence remains.

Collectors see their own requests and may submit only when actively assigned Fish/Meat Vendor Fee or Weight & Measure. Admin may read the tenant queue; SuperAdmin (Head) may decide. Approval requires a valid active current-year vendor and an uncorrected association to the active NPM stall's current answering occupancy, already effective today. The Head creates/confirms that real tenancy through normal registration/association first. Approval here attests that verified relationship; it neither creates a speculative stall nor posts payment.

The mobile no-link action explains office approval and no cash/SRC. It is online-only, retains operation identity during retry in the mounted form, disables duplicate submission, preserves fields when backing out, and links to paged own-request history. Linked vendors instead offer explicit navigation to the verified stall's NPM collection context. No free-text fallback grants NPM eligibility.

The Head queue is separate at `/monitoring/npm-requests`, linked from Direct Collection Review. It shows evidence, actor, dates, proposed tenancy, status and no-cash state. Live existing-occupancy warnings aid review without establishing identity by name. Details require a reason and explicit confirmation for return/rejection/approval; approval is disabled without verified association. Ordinary posted Direct Collection Review still changes only its existing administrative review information.

## Typed API contract

Routes use the existing authenticated clients and `ApiBaseController` result/error conventions. Clients serialize the existing project JSON conventions; DTOs use typed IDs, dates and enums rather than display labels as identity.

| Route | Authorization | Contract |
| --- | --- | --- |
| `POST /api/office-sources/fish-meat/npm-occupancies` | SuperAdmin | `CreateNpmVendorOccupancyRequest` → `NpmVendorOccupancyResult` |
| `GET /api/office-sources/npm-requests?page=1&pageSize=25` | SuperAdmin/Admin; assigned Collector sees own only | `NpmRegistrationRequestPage` |
| `POST /api/office-sources/npm-requests` | Assigned Collector | `SubmitNpmRegistrationRequest` → `NpmRegistrationRequestDto` |
| `POST /api/office-sources/npm-requests/{id}/review` | SuperAdmin | `ReviewNpmRegistrationRequest` → `NpmRegistrationRequestDto` |

Request pages validate page 1–100000 and size 1–100. Ordering is pending first, submitted UTC descending, then stable ID. The response includes Rows, Total, Page and PageSize. Tenant comes from authenticated context, never an accepted municipality parameter. Reads retain global filters plus explicit tenant predicates; tenant-scoped foreign keys prevent cross-tenant references. Unknown cross-tenant IDs return not-found. Invalid input returns the existing invalid/validation response, forbidden access the existing authorization response, and stale/concurrent/conflicting replay intent a conflict. Successful replay returns the original result.

No request DTO contains money, receipt serial, SRC, rental period or client-provided collector identity. UI timestamps render Philippine local time; tax-year/current-occupancy decisions use the server Philippine business date. Approval means registration verified, never “Collected.”

## Persistence and rollout gate

Additive migration `20261015030030_NpmRegistrationRequests20261015` creates the request table, tenant-scoped references to Users, vendor, association and prior request, valid-state checks, a concurrency version, unique client/decision operations and one pending request per collector/vendor. It adds a tenant/ID alternate key to the existing association for the composite FK. No financial table or existing cash data is rewritten. Request evidence is included in tenant export and the existing FK-ordered backup/restore set.

The migration was scaffolded and exercised **only in disposable PostgreSQL Testcontainers**. It was not applied to the municipal local database or production. The API normally migrates at startup: **CORE BRAIN V6 must obtain separate migration approval before launching this branch's API against that database.** Use disposable fixtures until that gate is resolved. No second ledger, rate calculator or Business Payor dependency was introduced.

## Verification

| Check | Result |
| --- | --- |
| Full unit suite | 2,614 passed; 0 failed; 0 skipped |
| Full Head component suite | 1,221 passed; 0 failed; 0 skipped |
| Final picker/review component checks after final header markup | 4 passed; 0 failed; 0 skipped |
| Full Mobile component suite | 124 passed; 0 failed; 0 skipped |
| Relevant PostgreSQL integration suite | 171 passed; 0 failed; 0 skipped |
| Client Release build | Passed, 0 errors |
| API Release build | Passed, 0 errors |
| Windows Mobile Debug, `net10.0-windows10.0.19041.0` | Passed, 0 errors; 24 warnings |
| Diff whitespace / changed scoped CSS | Clean; eight changed scoped CSS files brace-balanced |

Suites were invoked separately. The final integration filter was `FullyQualifiedName~CollectionSessionTests|FullyQualifiedName~Npm|FullyQualifiedName~TenantRestore`. It covers existing NPM canonical money paths as well as new nonfinancial requests. Full-unit discovery initially caught missing backup coverage and explicit EF-boundary entries; both were corrected and the full suite rerun successfully. New partials intentionally share the existing registry workflow/context transaction, documented in the architecture allowlist, instead of adding a second persistence boundary.

New regressions exercise real JWT API authentication/role boundaries; tenant isolation; stable vendor IDs despite equal names; closed registrations; validation preservation; duplicate occupied stalls; replay conflicts; concurrent Head decisions; expected-version checks; immutable returned history; rollback after the canonical writer saves; no duplicate vendor; separate source choices; dedicated Fish/Meat linked-NPM capability display; partial/absent-receipt Records presentation; server position values; and live caching-wrapper forwarding. Pending, rejected and approved registration requests all assert zero canonical cash rows. No automatic-payment path exists to test or enable.

The authorization test was proved red by temporarily removing the Head workflow guard (expected Forbidden became Ok), then restoring it. The caching forwarding regression also failed against the missing implementation, then passed. No fault injection or removed guard remains in production code; injected save failures exist only in the test host.

Raw verification logs are local, outside Git, at `C:\dev\stalltrack-v2-worktrees\mobile-npm-*.log`: `unit-full-final`, `head-last`, `picker-final`, `component`, `integration-final-all`, `client-build`, `api-build`, `windows-final`, and the earlier red-proof logs. Fresh build/test output also reports existing advisory/nullability/analyzer warnings; no dependency upgrade was included in this task.

## Browser evidence and limits

Isolated actual-component preview: `http://localhost:5325`, external host at `C:\dev\stalltrack-v2-worktrees\mobile-npm-qa-20261015`. It renders the compiled Razor components and scoped CSS with in-memory mocked API replies; it is **not the authenticated municipal portal or the native MAUI application**. No database is configured there and no real registration, review decision or financial transaction was submitted.

| Surface | Actual browser checks |
| --- | --- |
| Records and Reports | 320, 360, 390, 430, 768px; all four report tabs; canonical SRC and legacy partial row; details opening; contained page width |
| Report money | Both ₱1,738.00 and ₱1,234,567.89 stay on one text line at all five widths; historical ₱777 stays separate |
| Nonfinancial mobile request form | All five widths; editable proposal, Back/reopen preserves fields; no cash fields; no submit performed |
| Head NPM registration/picker | 390, 768, 1280px; keyboard Enter opens picker, stable selected vendor name returns, pre-entered stall retained, one modal at a time, no settled page overflow |
| Head request details | 390, 768, 1280px; evidence and no-cash state readable, reason and return confirmation reachable, approve disabled with no verified link; no decision posted |

Evidence includes `browser-results.json`, `head-browser-results.json`, `extra-browser-results.json`, and screenshots `reports-*`, `large-reports-*`, `records-*`, `record-detail-*`, `request-*`, `head-picker-*`, `head-selected-*`, `head-request-*` in that external folder. Screenshots at 320/360/390 were visually inspected. Final overflow checks wait for the existing drawer entrance transition to settle before measuring.

Windows native automation could not connect: the native computer-use pipe failed twice with OS error 2, including its prescribed retry. No alternative native automation was substituted. The requested native 360×800 Windows rendering check is therefore **not verified**. Build success is not native visual acceptance. No signed APK was built or published, no version numbers changed, and collectors have not received these changes.

## Exact source changes

Paths below are relative to this worktree. Directory headings are prefixes; each listed filename is an exact changed file. Generated migration designer is large because EF captures the full model; the snapshot diff is scoped to the new request entity/association key.

| Prefix | Files | Purpose |
| --- | --- | --- |
| `EEMOCantilanSDS.Api/Controllers/Revenue/` | `OfficeSourcesController.cs` | Existing authorized route family, four new endpoints |
| `EEMOCantilanSDS.Application/Common/Interface/ApiClients/` | `IMobileApiClient.cs`, `IOfficeSourcesApiClient.cs` | Typed request/review/registration contracts |
| `EEMOCantilanSDS.Application/Common/Interface/Persistence/` | `IAppDbContext.cs` | Request DbSet in existing unit of work |
| `EEMOCantilanSDS.Application/Common/Revenue/` | `OfficeCollectionWorkflow.NpmOccupancy.cs`, `OfficeCollectionWorkflow.NpmRequests.cs` | Atomic canonical registration reuse and separate nonfinancial request commands/queries |
| `EEMOCantilanSDS.Application/Dtos/Mobile/` | `NpmVendorOccupancyDtos.cs`, `NpmRegistrationRequestDtos.cs` | Stable typed identities, version and lifecycle payloads |
| `EEMOCantilanSDS.Domain/Entities/Revenue/` | `NpmRegistrationRequest.cs` | Nonfinancial evidence and guarded decisions |
| `EEMOCantilanSDS.Client/Components/Pages/Menus/Facilities/` | `NPM.razor` | Identity picker integration, deliberate confirmation and busy guard |
| `EEMOCantilanSDS.Client/Components/Pages/Shared/` | `AddVendorModal.razor` | Opt-in new-NPM registry action/context |
| `EEMOCantilanSDS.Client/Components/Pages/Menus/` | `PendingNpmRequests.razor`, `PendingNpmRequests.razor.css` | Separate paged review surface and responsive details/actions |
| `EEMOCantilanSDS.Client/Components/Shared/` | `RegisteredFishMeatPicker.razor`, `RegisteredFishMeatPicker.razor.css`, `DirectCollectionReview.razor` | Shared-shell picker and separate-queue link only |
| `EEMOCantilanSDS.HttpClients/ApiClients/` | `MobileApiClient.cs`, `OfficeSourcesApiClient.cs` | Existing authenticated HTTP implementations |
| `EEMOCantilanSDS.Infrastructure/Persistence/` | `AppDbContext.cs` | Request persistence in current context |
| `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/` | `NpmRegistrationRequestConfiguration.cs` | Tenant FKs, indexes, concurrency/state checks |
| `EEMOCantilanSDS.Infrastructure/Persistence/Migrations/` | `20261015030030_NpmRegistrationRequests20261015.cs`, `20261015030030_NpmRegistrationRequests20261015.Designer.cs` | Additive migration and generated model metadata |
| `EEMOCantilanSDS.Infrastructure/Migrations/` | `AppDbContextModelSnapshot.cs` | Current EF snapshot |
| `EEMOCantilanSDS.Infrastructure/Repositories/SystemHealth/` | `TenantDataTables.cs`, `TenantExportRepository.cs` | Preserve new evidence in tenant backups/exports |
| `EEMOCantilanSDS.Mobile.Core/Services/` | `CachingMobileApiClient.cs` | Live uncached forwarding for requests |
| `EEMOCantilanSDS.Mobile/Components/Pages/Menus/` | `Record.razor`, `Report.razor`, `Report.razor.css`, `NpmRequests.razor`, `NpmRequests.razor.css` | Compact records/reports and separate own-request history |
| `EEMOCantilanSDS.Mobile/Components/Shared/` | `CollectionRecordRow.razor`, `CollectionRecordRow.razor.css`, `OperationRecordsSection.razor`, `OperationRecordsSection.razor.css`, `CollectorPositionPanel.razor`, `CollectorPositionPanel.razor.css`, `CollectionSessionEditor.razor`, `NpmRegistrationRequestForm.razor`, `NpmRegistrationRequestForm.razor.css` | Shared density, server-position layout, verified source handoff and nonfinancial entry |
| `EEMOCantilanSDS.ComponentTests/Pages/` | `RegisteredFishMeatPickerTests.cs` | Head picker/form/review/rendering regressions |
| `EEMOCantilanSDS.Mobile.ComponentTests/` | `CollectionSessionEditorTests.cs`, `CollectorPositionPanelTests.cs`, `RecordsPresentationTests.cs`, `RegistrationRequestAndRecordTests.cs`, `EEMOCantilanSDS.Mobile.ComponentTests.csproj` | Actual-page/shared-component tests and compiled linked Razor/CSS |
| `EEMOCantilanSDS.IntegrationTests/` | `CollectionSessionTests.NpmRequests.cs`, `CollectionSessionTests.cs`, `CollectorApiHost.cs` | API/transaction/tenant/replay tests and isolated fault injection |
| `EEMOCantilanSDS.Testing/Architecture/` | `ApplicationEfBoundaryTests.cs` | Explicit reasons for existing workflow transaction partials |
| `EEMOCantilanSDS.Testing/Mobile/` | `CachingMobileApiClientTests.cs` | Real runtime-wrapper forwarding regression |
| `docs/planning/` | `NPM_REGISTRATION_REQUEST_POLICY_20261015.md`, `MOBILE_REPORTS_NPM_REGISTRATION_REUSE_20261015.md` | Confirmed policy and this handoff |

## Remaining limits and CORE BRAIN V6 integration

1. Review this branch, including its additive migration, before integrating. Do not replace the completed Fish/Meat refinement with an old interface. Primary checkout and its eight dirty files remain untouched.
2. Obtain separate approval before applying the migration or starting the new API against the municipal local database. Until then only the isolated fixture preview/testcontainers exercise new request persistence.
3. Registration approval is deliberately a two-step office workflow: create/confirm the real occupancy/association, then attest the pending request. It does not auto-convert a collector's proposed stall into tenancy.
4. Request history is live and paged; request submission is online-only, outside the financial offline queue. Retry identity survives while the form is mounted, not across app termination. Own history and the server's one-pending guard allow reconciliation after an uncertain result. A persistent nonfinancial outbox would be separate work.
5. Existing registry/candidate readers are unpaged and the NPM page's loaded-stall list is bounded to its existing first 100 records. “Review existing” depends on that loaded list; otherwise it asks the Head to refresh/review NPM. This task did not redesign those readers. Do not interpret an absent loaded record or link as proof of vacancy.
6. Duplicate/association evidence is checked live during Head review. The request retains the proposal, actor and decision evidence, but does not snapshot every candidate/possible-match result at submission. No financial eligibility is inferred from the proposal.
7. Native Windows 360×800 visual acceptance is still required. Use the approved Windows Debug endpoint `http://localhost:5117/`; it was not changed. Do not point Debug at production or publish an APK. Current tests do not replace native acceptance.
8. New request text uses SRC. Historical OR/CT/instrument compatibility remains untouched; this task does not rewrite historical evidence or rename the financial system globally.

The isolated fixture remains available at port 5325 while its task-owned process runs. `/reports`, `/records`, `/head-registration`, `/head-requests` and `/request-fixture` are verification surfaces only. Stop/restart only that external fixture process if needed. No existing portal/API/collector service was replaced, and no municipal registrations, collections, assessments, receipts or remittances were mutated. No push, deployment, production access, APK publication, or merge into the protected primary checkout was performed.
