# Space / Payor backend handoff

Local backend follow-up to the integrated `7cd4c997` baseline. This is a contract/identity improvement, not an accounting migration or production-release claim. Frontend presentation remains Claude's lane.

## Business Payors

Existing `GET /api/business-payors/occupancies` returns `PayorOccupancyDto`. Existing fields remain compatible. Added:

| Field | Meaning |
|---|---|
| `OccupancyId` | Alias of existing `ContractId`; use this stable identity for explicit link actions. |
| `FacilityId`, `FacilityCode`, `FacilityName` | Persisted facility identity and display context; do not derive operation identity from its name. |
| `Arrangement` | Existing `OccupancyArrangement`: SignedContract = 1, SpaceOnly = 2 (other existing arrangements remain contextual). |
| `LinkStatus` | Linked = 1, NeedsPayor = 2, Invalid = 3. A non-null link whose Payor cannot be read is Invalid. |

Recorded `ActualOccupant` and `NameOnContract` remain display evidence. Neither performs identity matching. The endpoint lists active stall occupancies; it does not manufacture Contract entities for obligation accounts.

Existing explicit actions remain: `POST /api/business-payors/links` (`ContractId`, `PayorId`), `/creations` (create and link with explicit duplicate confirmation), and `POST /api/business-payors` (explicit standalone Payor creation). Search/browse remains bounded to 50, tenant-scoped, empty/one-character/partial/case-insensitive. Browsing never mutates. Account occupants are represented by their explicitly chosen Payor; there is no separate recorded-occupant column on `ObligationAccount`.

## Approved operation identities and Add New

`GET /api/obligations/space-operations` returns `SpaceRentalOperationDto(Kind, Event, DisplayName, IsMonthly)` through `IObligationsApiClient.GetSpaceOperationsAsync()`.

| Operation | Kind | Event | Billing |
|---|---|---|---|
| Kanmanggay | KanmanggaySpaceRental = 2 | null | Existing effective-dated monthly obligation. |
| Fiesta | FiestaArawLotRental = 3 | Fiesta = 1 | Existing single approved event-lot charge. |
| Araw | FiestaArawLotRental = 3 | Araw = 2 | Existing single approved event-lot charge. |

Fiesta and Araw retain their separate event/date/account identity. **The accepted model currently shares the Fiesta/Araw lot-rental revenue classification.** This task does not invent separate income classifications or turn event lots into monthly rental. Any classification split requires an explicit accounting decision.

`POST /api/obligations/accounts` continues accepting `CreateObligationAccountRequest` and returning `ObligationAccountDto`:

- Required: Kind, explicitly selected valid PayorId, ActiveFrom for Kanmanggay, approved positive centavo Amount. Start/event date must be 2020 onward and at most one year ahead.
- Fiesta/Araw additionally require explicit Event and EventDate. The event date remains the account start and single assessment period.
- `SubjectLabel` is the actual space/lot number or office location label. Blank/empty now requests automatic numbering. Supplied values remain supported and are checked for duplicates.
- `Arrangement`: SignedContract (1) or SpaceOnly (2) for new reviewed rows. Null remains backward-compatible unspecified evidence. ContractReference is optional for signed occupancy, at most 200 characters; space-only forbids a nonblank reference. No Domain Contract is created. Both event lots and monthly spaces can carry this metadata.
- StallId must be null for spaces/event lots. Account ID and allocated SubjectLabel are returned. The existing close endpoint (`accounts/{id}/close`, ActiveTo) preserves Payor, rates, assessments and collections. No reopening rule was introduced.

Opening/rating/importing/closing remains SuperAdmin-only; office reads retain Admin/SuperAdmin access. Tenant and role come from authenticated context, not input.

## Numbering and concurrency

Numbering scope is **tenant + Kind + Event + EventDate**. Kanmanggay has null event/date; each Fiesta/Araw event date has its own sequence. Next number is highest positive numeric label in that scope + 1, starting at 1. Free-text historical labels are retained literally rather than extracting digits from display text. Numeric leading zeros normalize for duplicate detection (`7` and `007` conflict). Case/outer whitespace normalize for text labels. Closed accounts retain their numbers/history.

Add New and import Save acquire the same transaction-scoped PostgreSQL advisory lock for the tenant's space-account writes before reading numbering state. A tenant-wide lock avoids deadlocks for imports mixing events; the allocated sequence is still operation/event-scoped. ReadCommitted ensures a waiter reads the previous allocator's committed state. Lock hashes are parameterized, and released on commit/rollback/disposal. No schema, sequence table, reservation, historical rewrite or financial writer change is needed. Raw SQL/manual database insertions outside these application workflows do not participate in this lock.

## Typed import preview and Save

`POST /api/obligations/import/preview` and `/import` both accept `ImportSpaceHoldersRequest(Rows)` (1–200), where each row is `ImportSpaceHolderRow(Account, ClosedOn?)`. The shared client now exposes `PreviewSpaceHoldersAsync`; this HTTP wiring is necessary for the existing frontend to consume the typed backend preview, and changes no Razor/CSS.

Preview `SpaceHolderImportPreview.Rows[]` includes:

- RowNumber; Status Ready (1), NeedsPayor (2), Invalid (3).
- Stable Code: RequiresPayor, InvalidPayor, InvalidRow, InvalidAccount, DuplicateSpace (null for Ready); concise Message accompanies it. UI must branch on Status/Code, not parse Message.
- `Facts.Account`: normalized input including suggested SubjectLabel, explicit PayorId, Kind/Event/EventDate, amount/start, occupancy basis/reference.
- `Facts.ClosedOn`, `Facts.PayorDisplayName` (only a valid tenant Payor), and NumberOrigin Supplied (1) or ServerSuggested (2).
- AccountId is null during preview. No account, assessment, collection, Payor or number reservation is persisted.

Suggestions account for supplied numbers anywhere in the batch. **Keep the original blank SubjectLabel on Save for ServerSuggested rows**; the server allocates again under the lock. If the office edits/explicitly accepts a number as supplied, send that number and accept possible DuplicateSpace on revalidation. Never treat a preview number as a reserved allocation.

Save replans every row: Payor validity/tenant, dates, approved amount, basis/reference, closure and normalized duplicates. Existing semantics remain: duplicate spaces are skipped, but any other invalid/unresolved row prevents all new accounts. Ready rows are saved together in one transaction. Successful result Rows include persisted AccountId and actual Facts.Account.SubjectLabel; Imported/Skipped/NeedsReview remain compatible. NeedsPayor cannot save until an explicit existing Payor selection or separately confirmed Payor creation occurs.

No ICE stall parser was copied into this domain: its UI-local upload/sample parsing and Stall/Contract creation are coupled to rental tenancy. The typed Rows contract supports manual rows, uploaded rows and contract/space-only template rows equally. Frontend may reuse safe sheet cell/CSV parsing, but must map to this contract and obtain server preview. Do not invoke the stall bulk-import writer for obligation accounts.

## Transportation and preserved boundaries

Existing vehicle-class read response adds optional `History[]` of `VehicleClassRateVersionDto(RateId, EffectiveDate, Amount, CreatedBy, CreatedAtUtc)`, newest effective date first. Existing Id/Code/DisplayName/IsActive/CurrentAmount/CurrentEffectiveDate, save-rate and active/retire endpoints are unchanged. Historical rates and frozen collection evidence are never repriced.

ECF/WCF rules, Business Payor identity, Fish/Meat direct Vendor Fee, weighing, NPM, source authority, SRC, session quote/choice fields and queue contracts are unchanged. No space adapter was added to itemized checkout. Existing eleven supported source adapters retain one item → one canonical posting/Collection/SRC, atomic rollback and replay. Remittance/income readers are unchanged.

## Validation

The new concurrent allocation test first failed on the old required manual label, then passed against PostgreSQL. Focused regression covers space basis, independent event sequences, preview without writes, Save reallocation/created IDs/deleted Payor refusal, duplicate normalization, monthly amounts/closure, explicit linkage/tenant isolation, rate history, itemized and remittance behavior. Exact final run counts are recorded in the task checkpoint. Existing `SpaceOccupancyBasis` migration supplies both metadata columns; no migration is added or applied outside throwaway test databases.

Final validation: 2,511 unit tests passed; 76 focused PostgreSQL tests passed (no failures/skips), followed by 12 obligation tests after preserving the historical transaction branch. Reintroducing the old manual-label defect produced the expected failing allocation test, then the fix was restored. API Release, Mobile.Core Release and HttpClients Release builds passed; EF reports no pending model changes; `git diff --check` passed. No Android or UI/component review was performed in this backend lane. The initial API build emitted existing repository warnings; the final incremental build completed without warnings/errors.
