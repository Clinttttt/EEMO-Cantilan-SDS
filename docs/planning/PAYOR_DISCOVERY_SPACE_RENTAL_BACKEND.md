# Payor discovery and space rental — backend handoff

Base: `review/latest-integrated` at `0b0de3fe`. Implementation is isolated on `codex/payor-space-backend`.
This is a backend/shared-contract follow-up, not a frontend redesign or deployment. ADR-001 and IA-064 still govern
identity and additional direct Vendor Fees. Rates, rent, classifications, posting, SRC and remittance retain their authority.

## Local finding

The subsequent [space/payor domain backend handoff](SPACE_PAYOR_DOMAIN_BACKEND_HANDOFF.md) adds typed occupancy context,
operation/event-scoped automatic numbering, detailed import preview/save results and vehicle-class rate history.
Use that contract for the latest frontend integration; the original baseline and data findings below remain historical evidence.

Read-only inspection of the configured localhost development database found Ana Reyes as an active NPM Vegetable
Area occupant of Stall 1 (occupancy effective 2026-06-05), with no `Contract.PayorId` and no Business Payor row named
Ana Reyes. She is occupant text, not an authoritative payer. No records were created or linked during the inspection.
Empty browsing was separately blocked by the former two-character search guard. Removing that guard does not create
Ana's identity: the office must explicitly create/confirm or select a Payor and link the occupancy.

## Payer browse/search

`GET /api/mobile/collection-session/payors?search=` remains Collector-only and checks authenticated active collector
and tenant. Empty/whitespace input browses; one-character and partial input search case-insensitively. Up to 50
non-deleted Payors are returned, ordered by lowercased display name and then stable PayorId. No paging contract is
needed for this bounded picker. Payor has no separate active/inactive flag; soft deletion is its validity boundary.

Result: `CollectionPayorDto(PayorId, DisplayName, Contexts?)`. Contexts contain linked occupancy/account descriptions,
not financial eligibility. Select by PayorId. Same-name Payors remain separate. Context queries are bounded to the
returned identities. Existing clients can ignore the additive Contexts field.

Office `GET /api/business-payors/candidates?search=` and Current Collection search use the same identity search rule.
The office result remains `PayorCandidateDto(PayorId, DisplayName, Kind, Contexts)`.

## Explicit occupancy linkage

New Add New responses (`StallDto`) and successful stall import rows (`BulkImportRowResult`) include `OccupancyId`.
This is the saved occupancy/Contract identity; it is not a receipt or a Business Payor. The office can follow creation
with either established endpoint:

- `POST /api/business-payors/links`: `LinkPayorRequest(ContractId = OccupancyId, PayorId)`.
- `POST /api/business-payors/creations`: `CreatePayorAndLinkRequest(ContractId, DisplayName, Kind, ConfirmDuplicate)`.

These endpoints remain Admin/SuperAdmin-only. Creation of a same-named person requires explicit duplicate confirmation;
selection never silently merges people. A failed linkage leaves the occupancy unlinked and uncollectible through payer-required
flows. This is an explicit two-step workflow, not atomic creation plus automatic identity inference. Existing unlinked
occupancies remain accessible through `GET /api/business-payors/occupancies?filter=1` (`NeedsPayor`). Posted history is untouched.

## Itemized and Vendor Fee discovery

`GET /api/mobile/collection-session/eligible?payorId=...` reuses authorized work/capability data. Source-backed utility,
Vendor Fee, weighing and NPM Whole capabilities cannot be added without an eligible linked source for that exact payer.
An empty set returns `CanAdd=false`, `ReasonCode=NoEligibleSource`; unsupported adapters retain `NotSupportedYet`.
Existing malformed ECF isolation and quote/post revalidation remain intact. Discovery is not permission to post stale money.

`DirectVendorFeeSource` keeps existing fields and adds `OccupancyId`, `HasExistingPayorLink` and derived structured fields:

| Status enum | ReasonCode | RequiredAction enum |
| --- | --- | --- |
| Available = 1 | null | None = 0 |
| NeedsPayor = 2 | RequiresPayorLink | LinkBusinessPayor = 1 |
| Unavailable = 3 | PayorLinkUnavailable or SourceNotAvailable | OfficeReview = 2 |

An unavailable/deleted linked Payor needs office review; it must not be silently repointed. Messages are display text;
frontend behavior branches on status/code/action. Collector cannot perform the office linking mutation.

Vendor Fee remains itemized-capable: `SessionVendorFeeIntent(StallId)` plus collector-entered ConfirmedAmount. Standalone
and basket use `FishMeatVendorFeeCollectionWorkflow`, OR and the Vendor Fee classification. Rent and weighing remain separate.
V1 retains one item per Collection/SRC, mixed OR/CT boundaries, stable retry identities and Serializable atomic posting.

## Kanmanggay Add New

Head `POST /api/obligations/accounts` accepts the existing `CreateObligationAccountRequest` with additive nullable fields:

- Kind = KanmanggaySpaceRental (2), explicit PayorId, StallId = null, SubjectLabel = space identifier.
- ActiveFrom and approved Amount retain existing monthly-account date/rate rules; Event/EventDate are null.
- Arrangement = SignedContract (1) or SpaceOnly (2).
- ContractReference: optional (up to 200 characters) for SignedContract; null/blank for SpaceOnly.

This reference is descriptive contract evidence, not a fabricated `Contract` entity or a new term/assessment engine.
Existing accounts with unspecified basis remain null. `ObligationAccountDto` returns the basis/reference. Contract
metadata does not determine rent or automatically end an account. Closing uses the existing account close endpoint
and ActiveTo; history and collections stay intact. No reopening policy is invented. Existing-space duplication is
refused for Add New, matching the existing import protection, including historical closed accounts.

The existing monthly reader resolves approved rates at the first day of the billing month. The frontend should submit
the selected month as its first day; it must not invent mid-month proration or copy ICE's contract-term arithmetic.
Kanmanggay remains an ObligationAccount operation. This change does not invent Mobile collector assignment or enable
its currently unsupported itemized adapter.

## Space-holder import

`POST /api/obligations/import/preview` (Head-only) takes `ImportSpaceHoldersRequest(Rows)` with 1–200
`ImportSpaceHolderRow(Account: CreateObligationAccountRequest, ClosedOn?)` records.
Result: `SpaceHolderImportPreview(Rows: SpaceHolderImportRowResult[], CanSave)`.

Row fields: RowNumber (1-based submitted position), Status, Code, Message, optional AccountId (reserved; currently null).
Status enum: Ready = 1, NeedsPayor = 2, Invalid = 3. Missing PayorId produces RequiresPayor; an unknown/foreign/deleted
identity produces InvalidPayor without foreign details. Other codes: InvalidRow, InvalidAccount, DuplicateSpace.
Blank contracts are accepted for SpaceOnly. Amounts retain positive whole-centavo validation. ClosedOn uses existing
closure validation. Duplicate space/event identities within the batch or existing data are identified explicitly.

Create a new Payor explicitly with `POST /api/business-payors` and `CreatePayorRequest`, obtain its PayorId and re-preview;
import never resolves names or creates identities. Existing same-name identity confirmation rules remain in force.

`POST /api/obligations/import` revalidates using the same planner inside a Serializable transaction. Existing behavior is
preserved: duplicate rows are skipped; any other unresolved/invalid row prevents all additions. Only Ready rows persist.
The backward-compatible result retains Imported, Skipped, NeedsReview and adds typed Rows. CanSave permits ready
additions with duplicate-only skips; it is false if identity/validation needs review or there are no additions.
No accounts/assessments are persisted during preview. Frontend must read typed row codes, not parse NeedsReview strings.

## Persistence and validation

Additive migration `20261005115827_SpaceOccupancyBasis` adds two nullable columns to ObligationAccounts: Arrangement
and ContractReference. They are needed to retain the approved signed/no-contract choice across reload, import and API
reads; legacy nulls are preserved. It changes no financial columns, allocations or classifications. Applied only to
throwaway PostgreSQL during tests, not the local development or production database.

HTTP authorization/not-found/conflict behavior follows existing API Result mappings. Session quote and record problems
remain typed CollectionSessionProblem codes. No new identity source, receipt serial, classification or financial engine
was added. Frontend wiring and explicit office linkage of existing data remain separate follow-up actions.

Validation: 2,511 unit tests and 100 relevant PostgreSQL tests passed. API Release, Mobile.Core Release and Web
Release builds passed; Web retains 54 existing compiler warnings. EF reports no pending model changes, and
`git diff --check` passed. Browse and duplicate-space regressions were proved red by restoring the old guard behavior
and disabling the new duplicate check respectively, then restored and verified green. PostgreSQL tests cover explicit
linkage, same-name identity safety, itemized Vendor Fee/rent/weighing isolation, malformed-source isolation, import
review states, remittance retry/void and unchanged classified Monthly Income after remittance voiding.
No frontend/component changes or Android builds were made. Kanmanggay/Fiesta itemized Mobile adapters remain deferred
until explicit collector authorization semantics exist; this work adds office account/import contracts only.
