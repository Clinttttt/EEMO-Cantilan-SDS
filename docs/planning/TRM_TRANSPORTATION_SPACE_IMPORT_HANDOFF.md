# Transport Terminal authority and shared space import contracts

Backend consolidation from integrated `96723668`. No UI, schema, rate, classification or production change.

## Current transportation authority

Transport Terminal/TRM and Transportation/Parking are one current operation. New money uses the existing governed Transportation writer, effective-dated vehicle class, CT instrument, canonical Collection and server SRC. Standalone and itemized posting remain the same authority; no transporter registration, TripNumber or duplicate TrmTrip is created.

The first existing Head-enabled Transportation setting is the business-date boundary. Earlier dates remain legacy. Disabling the canonical service refuses new collection but never reopens legacy trip posting. IA-065 does not invent or backdate activation.

### Shared current read

Head/Admin authenticated clients can call either alias; both execute `GovernedServiceWorkflow.GetTransportationCurrentAsync`:

- `GET /api/trm/current?from=yyyy-MM-dd&to=yyyy-MM-dd`: `ITrmApiClient.GetCurrentAsync`.
- `GET /api/governed-services/transportation/current?from=yyyy-MM-dd&to=yyyy-MM-dd`: `IGovernedServicesApiClient.GetTransportationCurrentAsync`.

`TransportationCurrentActivityDto` provides From, To, server Today, CollectedToday, TransactionsToday, ActiveVehicleClassCount, CollectedInPeriod, TransactionsInPeriod, and Collections. Periods use Collection business date; existing authorization, tenant filters and maximum 367-day interval apply. Today's summary is independent of the selected historical interval.

Each `GovernedServiceActivityDto` carries CollectionId, ReferenceCode (SRC), BusinessDate, RecordedAtUtc, Instrument, PayerName/PayorId where recorded, Reference, CollectorId/CollectorName, frozen VehicleClassCode/VehicleClassName, VehicleClassRateId/VehicleClassRateEffectiveDate/FrozenVehicleRate, and typed State. Amount remains the original audit amount for compatibility; NetAmount includes canonical correction effects. Use server summary figures for collected totals. Reversed rows stay visible; transaction counts include only positive net financial effect. State: Posted=1, DocumentCorrected=2, Reversed=3. Disposition remains compatibility display text, not logic.

The ordinary governed activity endpoint returns the same canonical rows. Current TRM must consume `/current`; old `/overview` and trip queries have not been repurposed. ActiveVehicleClassCount counts active configured identities, not transporters or receipt stock. No Pending OR metric is supplied.

Driver, plate, organization, route, dispatch order and old TripNumber cannot be reliably reconstructed. Do not fabricate them or parse Reference to recover identity. Vehicle names/rates are frozen snapshots, not today's configuration.

### Legacy compatibility

TrmTrip, TrmTransporter, ITrmRepository, old ITrmApiClient overview/transporter/trip/history methods and API routes remain unchanged for legacy compatibility. Present their reads separately from canonical activity. Existing trip posting guards respect the persistent canonical activation boundary. No historical money is migrated, deleted or combined with canonical revenue. Historical OR annotation is compatibility, not current SRC posting.

## Shared import table

Use existing `POST /api/obligations/import/preview` and `POST /api/obligations/import`; ImportSpaceHoldersRequest.Rows contains ImportSpaceHolderRow(Account, ClosedOn). CSV/Excel/manual data maps into these typed rows; do not reuse Stall/Contract financial persistence.

Account is CreateObligationAccountRequest: Kind, explicit PayorId, optional StallId, SubjectLabel (space/lot number; blank requests allocation), Event/EventDate when applicable, ActiveFrom, approved Amount, optional Arrangement and ContractReference. Obtain approved operation/event identities from the existing space-operation endpoint, never display text. Kanmanggay Amount is monthly; Fiesta/Araw Amount is the event/lot charge. Events retain separate identities with the existing shared official classification.

Preview returns RowNumber, Status, Code, Message, AccountId, normalized Facts and computed RequiredAction. Facts include Account, ClosedOn, NumberOrigin (Supplied=1, ServerSuggested=2), and PayorDisplayName. Status: Ready=1, NeedsPayor=2, Invalid=3. RequiredAction: None=0, SelectPayor=1, CorrectRow=2, ResolveDuplicate=3. Use typed fields and Code, not Message parsing. Preview is read-only and never reserves numbers.

The office table need not have a permanent Payor column, but unresolved rows require an explicit selection step before save. No name matching, silent creation or same-name merge. Existing OccupancyArrangement supports signed-lease and space-only metadata; space-only requires no contract reference or fake Domain Contract.

Save revalidates tenant, Payor, basis, amount, dates and uniqueness under existing transaction/advisory lock. Numbering scopes: tenant + operation, additionally event/date for Fiesta/Araw. Concurrent blank allocations are serialized; preview suggestions are not promises. Supplied duplicates are skipped under existing semantics. Other invalid/unresolved rows block inserts; failures roll back. Result Rows return actual AccountId and assigned SubjectLabel. Existing start/close and assessment rules remain unchanged.

These accounts do not persist independent ActualOccupant or ContractName. PayorDisplayName is the linked Payor display, not separate occupant evidence. Only ContractReference is supported optional contract text. Do not invent unsupported fields or create Payors from uploaded occupant names. Template/sample generation and editable presentation remain frontend responsibilities.

## Preserved behavior

WCF unprepared sources remain discoverable for approved direct collection; prepared balance/partial rules still win. ECF direct/prepared and malformed-bill isolation remain unchanged. Itemized sessions retain deterministic children, atomic posting, one item per Collection/SRC and mixed instruments. Only canonical children count in reports. Remittance/void changes position, never Collection count, SRC or income. No migration is required.

## Local validation

- Full Unit suite: 2,511 passed, zero failed/skipped.
- PostgreSQL: 141 selected CollectionSession, governed service, Market Fee, WCF, ECF, obligation account, remittance and legacy TRM reconciliation tests passed; 8 additional WCF direct-entry/readiness tests passed. Throwaway databases only.
- New transportation scenario proves standalone/itemized replay, shared current reads, no duplicate trip/registry, register/activity/collector/income/remittance counts, remittance void, legacy isolation, frozen rate history, changed-intent conflict, correction-aware totals and role/tenant refusal.
- The disabled-service authority regression failed against the original implementation before the fix (expected canonical, actual legacy), then passed with the persistent activation boundary.
- API, HttpClients and Mobile.Core Release builds passed. EF reports no pending model changes. No Android or UI runtime review was needed for this backend-only change.
- Claude's primary checkout advanced independently to `333a54cf` on `claude/strict-ice-component-reuse` during validation. This feature remains isolated for integration; no frontend files or other-worktree changes were copied or overwritten.

## Changed files

- API: `EEMOCantilanSDS.Api/Controllers/Facilities/TrmController.cs`, `EEMOCantilanSDS.Api/Controllers/Revenue/GovernedServicesController.cs`.
- Client interfaces: `EEMOCantilanSDS.Application/Common/Interface/ApiClients/ITrmApiClient.cs`, `EEMOCantilanSDS.Application/Common/Interface/ApiClients/IGovernedServicesApiClient.cs`.
- Workflows: `EEMOCantilanSDS.Application/Common/Revenue/GovernedServiceWorkflow.cs`, `EEMOCantilanSDS.Application/Common/Revenue/GovernedServiceWorkflow.Transportation.cs`, `EEMOCantilanSDS.Application/Common/Revenue/TransportationCollectionAuthority.cs`.
- DTOs: `EEMOCantilanSDS.Application/Dtos/Revenue/GovernedServiceDtos.cs`, `EEMOCantilanSDS.Application/Dtos/Revenue/ObligationDtos.cs`.
- HttpClients: `EEMOCantilanSDS.HttpClients/ApiClients/TrmApiClient.cs`, `EEMOCantilanSDS.HttpClients/ApiClients/GovernedServicesApiClient.cs`.
- Tests: `EEMOCantilanSDS.IntegrationTests/CollectionSessionTests.cs`, `EEMOCantilanSDS.IntegrationTests/ObligationAccountTests.cs`.
- Documentation: `docs/business/EEMO_BUSINESS_RULES.md`, `docs/decisions/DECISION_REGISTRY.md`, `docs/planning/CANONICAL_AUTHORITY_LANDING_FEES_HANDOFF.md`, this handoff.
