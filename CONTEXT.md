# StallTrack Domain Context

This glossary defines business terms used across StallTrack V2. It contains domain meaning only; implementation details belong elsewhere.

## Business Payor (compatibility / historical target)
A tenant-scoped business identity introduced by the earlier ADR-001 design. **ADR-007 / IA-068 supersede Business Payor as the target collection-discovery model.** Existing BusinessPayor IDs/links may remain during compatibility migration, but new product workflows must not require a Business Payor master, manual Business Payor linking, or a Business Payors page.

The safety rules survive the supersession: never merge identities from matching names/text, never cross tenant boundaries, and never rewrite posted payer evidence because a master/source record later changes.

## Source Identity
The stable, tenant-scoped identity owned by the specialized operation that proves a real relationship and collection eligibility. Examples include an NPM occupancy/stallholder, monthly rental account/space holder, Fish/Meat vendor registration, utility subject/account, or another approved operation-owned record. Unified New Collection searches typed Source Identities and asks the server which operations are actually eligible.

## Payer Snapshot
Name/reference text frozen on a posted Collection when an operation permits direct/one-off collection without a registered Source Identity. A Payer Snapshot is historical evidence only; it never creates, merges, or proves a cross-operation identity.

## PayorUser
The authentication/access identity. It may be linked to source-owned business records for portal access where supported, but authentication identity is not the authoritative source of collection eligibility.

## Obligation
An amount owed under a specialized business domain, such as stall rental or a utility assessment. An obligation is not the same thing as money received.

## Collection
One posted money-received event for one source/payer context and business date. A Collection may reference a typed Source Identity or preserve only a permitted Payer Snapshot. It contains one or more itemized Collection Lines and receives one immutable SRC when canonical.

## Collection Line
One classified portion of a Collection. Each line states what revenue the money represents and its amount, while preserving the source obligation/activity when applicable.

## Revenue Classification
The official reporting meaning of a Collection Line, such as Stall Rental, ECF, WCF, Landing/Berthing, or Weight & Measure. It is distinct from facility and from the physical receipt/ticket.

## Itemization
The breakdown of a Collection into its Collection Lines so that one document may show multiple compatible revenue items while reports can still total each revenue classification independently.

## Calculation Detail
Supporting detail that explains how one Collection Line amount was calculated, such as quantity, unit, rate, meter readings, slaughter package components, or lot area. Calculation Detail does not automatically become a separate Revenue Classification.

## Collection Allocation
The explicit application of a Collection Line amount to one or more source obligations. Allocation must be visible and intentional; StallTrack must not silently decide where partially paid money goes.

## StallTrack Reference Code (SRC)
The primary digital identity of every canonical Collection, in the form `SRC-YYYY-NNNNNN` (for example `SRC-2026-000127`). It is allocated by the server from one global, monotonic database sequence at posting; it never resets by year, collector, facility or remittance (the year only states when the Collection was recorded), is immutable, never typed, edited or recycled, and is stable when the same `ClientOperationId` is replayed. SRC is **not** an Official Receipt, a Cash Ticket or any government receipt, and a legacy-authoritative row has none (it keeps the source's own document). See IA-062.

## Accountable Document
An optional back-office record of a physical Official Receipt or Cash Ticket (accountable stock, custody, cancellation, loss). A Collection is identified by its SRC and neither requires nor consumes an Accountable Document (IA-062). The document number, where one exists, belongs to the document, not to each Collection Line.

## Official Receipt (OR)
An Accountable Document for OR-compatible revenue lines. In Cantilan, one OR may contain multiple compatible itemized lines for the same payor. OR-compatible and Cash-Ticket-compatible lines are not mixed on one document.

## Cash Ticket (CT)
An Accountable Document for CT-compatible revenue lines. Cash Ticket issue/accountability remains distinct from OR. Whether one physical CT may carry multiple CT-compatible lines is not assumed without explicit office policy.

## Document Total
The sum of the Collection Lines represented by the active accountable document.

## Instrument Compatibility
The rule that determines whether a Revenue Classification belongs to OR or Cash Ticket for the applicable tenant/date. Latest Cantilan clarification: Weight & Measure = OR, WCF = CT, Tabo = OR, and Vegetable/Fruit Space Rental uses OR for full/whole payment and CT for daily transactions. IA-046 is therefore resolved. A posted Collection still resolves to exactly one instrument family and never mixes OR and CT lines on one accountable document.

## Fish/Meat Vendor Fee
A reportable OR revenue classification distinct from NPM rent and Weight & Measure. Under IA-068 it belongs to an independent Fish/Meat vendor-registration context rather than NPM. The current clarified collection basis is the **actual amount received**; there is no approved fixed Vendor Fee rate. A permitted collection may freeze a typed vendor/payer snapshot when registration does not yet exist.

## Fish/Meat Vendor Registration
The source-owned annual/tax-year registration record for one vendor type: **Fish** or **Meat**. It records New/Renew plus office-evidenced registration facts, has an **Active/Closed** lifecycle, and is renewed by creating a new annual registration linked by ID rather than mutating history or matching by name. Closing is prospective: historical registrations and Collections remain readable, while future registered-source Weight & Measure collection is blocked. It is independent from NPM occupancy and is the required source identity for Weight & Measure.

## Weight & Measure
A separate OR revenue classification using weighed quantity × the server's effective configured rate. It requires a registered Fish/Meat Vendor Registration; there is no free-text unregistered-vendor fallback. Vendor Fee, weighing, and NPM rent never settle one another.

## Income From Terminal
The separate official CT operation/report family confirmed by IA-067. It contains **Comfort Room**, **Pull Pul Vans, Cargo Vans**, and **Tricycad**. Each section permits direct aggregate peso entry; optional Cash Ticket count is supporting evidence only. Vehicle-class-assisted entry maps Jeepney, Multicab, Van, Public Utility Bus and Public Utility Baby Bus to Pull Pul Vans/Cargo Vans, and Tricycle to Tricycad.

## Transportation / Parking
A CT revenue source separate from Income From Terminal. The clarified target basis is **direct amount received** with no required vehicle-class/rate evidence and no TRM/Terminal synonym. Historical TRM/Transportation records remain compatibility evidence and are not reclassified by guess.

## Official Monthly Income Adjustment
A Head-only audited report revision that changes the official reported cell without changing Collections, source balances, collector position, remittance, or accountable-form history. It preserves the system basis, signed delta/official amount, required reason, optional reference, actor/time, and revision/supersession history.

## Slaughterhouse Breakdown
Transparent calculation detail for the fixed/approved slaughterhouse charge package. The component breakdown does not become separate revenue classifications unless the office formally reports those components independently.

## Collection Composer
The shared working surface used to assemble compatible Collection Lines before money is posted. It may be entered from a specialized Operation or from source-native New Collection after selecting a Source Identity. The UI must show only server-confirmed eligible items; direct/one-off fallback is allowed only where the source policy permits it.

## Draft Collection
An unposted working collection. In the approved Web target, it is server-persisted with a stable DraftId and revision, but creates no revenue, money allocation, document consumption, RCD entry or Collection Activity. Review and successful canonical posting create the resulting financial history. See [ADR-004](docs/decisions/ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md).

## Collection Session
A user-facing visit/work session that may result in more than one Collection when the payer is settling both OR-compatible and CT-compatible items. A Collection Session is not itself a financial transaction.

## NPM Daily Collect All
A reviewed batch convenience for **NPM Daily only**. The server returns readiness per stall; only `CanCollect` stalls may be selected. Each selected stall posts its own canonical Collection/SRC. Unchecked or blocked stalls remain unpaid and are not marked absent. “Pending” on the round and “eligible for Collect All” are different facts: a pending stall may still be blocked by canonical/source/rate policy. `SourceStillLegacy` means canonical NPM batch collection is not enabled for that source; it must not be presented as “nothing left to collect.”

## Mobile Collection Correction
A collector-authorized correction never edits or deletes posted money. **Edit** atomically records the original reversal and a replacement Collection with a new SRC; **Remove** records an audited reversal with no replacement. If replacement posting fails, the entire edit transaction rolls back so the original remains financially effective. Ownership, business date, remittance/source locks and server capability flags determine whether correction is allowed.

## Explicit Allocation
A visible user-confirmed allocation of a payment amount to specific obligations or periods. StallTrack may suggest an allocation, but it must not silently decide the final allocation without confirmed policy.

## Utility Operation Scope
ECF and WCF are broader MEEDO Utility Operations, not globally owned by the NPM facility. An NPM stall may be one utility service subject/context, but NPM must not be the architectural parent of every ECF/WCF assessment. Existing NPM-bound UtilityBill rows remain valid legacy/current source evidence. Target generalization must be additive and preserve those records rather than rewriting them.

## Partial Utility Payment
A valid partial settlement of an ECF or WCF obligation that leaves the exact remaining balance outstanding.

## Cash Ticket Transaction
A Collection/Collection Line whose resolved instrument policy is Cash Ticket. Financial reporting derives from the posted Collection facts; separately recorded physical CT issue/custody belongs to Accountable Forms and is not required to create the Collection.

## Accountable Form Assignment
Custody of an OR or CT series/range assigned to a collector/accountable officer in the separate Accountable Forms register. Under IA-062, assignment/custody does **not** gate Collection posting and a Collection does not automatically consume a form. The register tracks the office's physical stock/custody events independently from money received.

## Issued Document Correction
An issued OR or CT is not edited in place. Correction uses attributable void/reversal/replacement history, and the original physical number remains permanently consumed.

## Historical Charge Snapshot
The preserved quantity, rate, readings, category, basis, and other calculation detail applicable when a charge/collection was posted. Later configuration changes do not recalculate historical itemization.

## Policy-Resolved Instrument
The OR-versus-CT choice is resolved from the Revenue Classification policy. Staff do not override the instrument ad hoc. If a user attempts to combine incompatible items, StallTrack separates them into distinct collections/documents.

## Current Collection
A persistent draft/working collection that may originate from an Operation or source-native collection flow. It keeps the selected Source Identity or permitted Payer Snapshot plus reviewed compatible items until explicitly posted or discarded. It must not infer eligibility from a matching name.

## Approved Charge Line
A Collection Line must map to an approved Revenue Classification/charge definition. Arbitrary free-text financial lines are not allowed; optional descriptive detail does not create a new revenue identity.

## Governed Configurable Service
A tenant-owned operational service whose structurally simple financial policy is defined through approved configuration rather than hard-coded guesswork. Required configuration may include stable service identity, Revenue Classification, effective-dated OR/CT policy, calculation basis/rate, source-identity or payer-snapshot rule, operational fields, active state, and allowed channels. It does not allow free-form collector-created charges. See [ADR-006](docs/decisions/ADR_006_GOVERNED_CONFIGURABLE_SERVICE_OPERATIONS.md).

## Setup Required Operation
A known operation that may appear in authorized Web directory/setup surfaces while required financial policy is incomplete. It cannot create a financial Collection until the required classification, amount/calculation and instrument/channel policy is valid and active. Physical accountable-form stock/custody is not a collection-readiness gate under IA-062. Transfer Large Cattle may use this state until its approved fee/instrument policy is complete.

## Mobile Configured Operation
A focused Collector Mobile workflow exposed only when the operation is Active, Mobile-enabled, authorized/assigned to the collector, and server-ready for the source. The collector supplies permitted transaction facts; the specialized source or approved configuration supplies classification, instrument policy, rate/calculation policy, and eligibility. Physical OR/CT stock is separate accountability metadata and never a canonical collection-readiness gate (IA-062).
## Obligation Payment Limit
An obligation-backed Collection Line cannot allocate more than the amount actually outstanding. V2 does not create unapplied customer credit, deposit, or advance balances unless a future approved business rule explicitly introduces them.

## Physical Form Use versus Collection Posting
Physical OR/CT issuance/custody is separate from canonical Collection posting. A canonical Collection neither requires nor consumes an AccountableDocument; it is identified by its SRC. If the office records a physical form as issued/cancelled/lost/returned in Accountable Forms, that is an auditable form event and must not create or alter revenue.

## Collection Activity Record
The primary Collection Activity row represents one posted collection/document event. Itemized Collection Lines appear as expandable/detail content beneath that event rather than as unrelated top-level transactions.

## Derived RCD
RCD/category totals are derived from authoritative posted Collection Lines and their resolved instrument/classification facts, grouped by the applicable business date, collector and revenue classification. Physical accountable-form usage is separate accountability evidence and must not add money or duplicate Collection totals. Staff do not re-enter the same financial totals manually.

## Legacy Financial History
Pre-itemization records remain valid historical evidence. StallTrack adapts only facts actually stored and never invents old receipt groupings, line breakdowns, allocations, CT numbers, vehicle classes, or document composition.

## Source-native Collect View
A selected Source Identity may start collection by showing only the server-confirmed eligible items for that source. Compatible items feed the same approved posting writers used by their standalone operations; the view is orchestration, not a second financial engine.

## Collection Detail Levels
The document/receipt view shows clean financial lines and totals. The audit/detail view additionally preserves source operation, facility, stall/space, obligation period, allocation, quantity/rate, readings, calculation basis, and historical policy snapshot where applicable.


## Web Collection Authority
Head and Admin may create, assemble, allocate, review, and post normal office collections through the shared Collection Composer.

## Mobile Collection Authority
Collector uses approved backend writers through focused Mobile workflows limited to assigned operations. The target New Collection flow may search across source-owned identities and assemble several eligible items, but every financial child still posts through its owning writer and receives its own Collection/SRC. Offline/retry identity remains server-safe and replay-stable.

## Manual Approved Authority
A Manual Approved amount may be entered operationally by Head or Admin only when the approved charge definition explicitly permits that basis. It does not authorize inventing a new charge or changing a configured ordinance/rate.

## Posting Authority
Head and Admin may post normal Web collections. Collector may post/sync Mobile collections within assigned operational authority. Payor-originated online payments remain a separate channel but converge on canonical collection truth.

## Correction Authority
Head and Admin may void/replace issued OR or CT records through attributable, audited correction flows. Issued document history is retained; Collector cannot silently delete or rewrite a synced issued document.

## Accountable Form Management Authority
Head and Admin may manage OR/CT books, series/ranges, assignment, transfer/return, cancellation/loss and office custody within their LGU. Authorized collectors may hold/receive physical forms according to office procedure. These form events remain separate from Collection posting.

## Physical Document Requirement
Under IA-062, a physical OR/CT serial is **not required to post a canonical Collection** on Web or Mobile. Instrument type remains policy metadata, and the real-world office may still hand over a physical form; its optional back-office record never blocks, quarantines, or identifies the digital Collection.

## Draft Ownership
A Web draft belongs to its tenant and owning user and can be recovered in a later authenticated session. Mutations require its expected revision; review binds the exact meaningful financial state/revision. Posting revalidates current facts and requires renewed review for material changes. One DraftId produces at most one successful Collection, even across different ClientOperationId values. Initial ownership excludes shared editing/handoff; discard remains non-financial. See [ADR-004](docs/decisions/ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md).

## Concurrent Posting Guard
Final posting revalidates outstanding amounts, allocations, source eligibility, reviewed intent, instrument policy, idempotency/replay identity, and totals atomically. Accountable-form custody is validated only inside its own form-management events, not as a canonical Collection gate. A stale concurrent attempt fails cleanly instead of double-settling an obligation.

## Itemized Collection Statement
StallTrack may print an Itemized Collection Details / Collection Statement identified by the Collection's SRC and may display separately recorded physical OR/CT evidence when available. It must never present that digital statement or SRC as a replacement government Official Receipt or Cash Ticket.
