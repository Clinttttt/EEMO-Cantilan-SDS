# StallTrack Domain Context

This glossary defines business terms used across StallTrack V2. It contains domain meaning only; implementation details belong elsewhere.

## Payor
The tenant-scoped canonical business identity of a person or organization whose identity StallTrack intentionally maintains across approved operational relationships. Payor identifies who those relationships belong to; specialized domains determine what is owed. It is independent of portal activation and cannot be inferred from matching names or other non-authoritative text. A single accountable document has one payer context, which may remain anonymous, named-snapshot-only, or one-off where the operation permits it. Posted payer evidence is preserved independently of later master-record edits. See [ADR-001](docs/decisions/ADR_001_BUSINESS_PAYOR_IDENTITY.md) for the accepted target constraints and conservative migration rules.

## PayorUser
The authentication/access identity. Its relationship to a business Payor, where supported, is explicit and optional; it is not the authoritative financial/business identity.

## Obligation
An amount owed under a specialized business domain, such as stall rental or a utility assessment. An obligation is not the same thing as money received.

## Collection
One posted money-received event for one payor context and business date. A collection contains one or more itemized Collection Lines.

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

## Accountable Document
The physical Official Receipt or Cash Ticket identity associated with a Collection. The document number belongs to the document, not separately to each Collection Line.

## Official Receipt (OR)
An Accountable Document for OR-compatible revenue lines. In Cantilan, one OR may contain multiple compatible itemized lines for the same payor. OR-compatible and Cash-Ticket-compatible lines are not mixed on one document.

## Cash Ticket (CT)
An Accountable Document for CT-compatible revenue lines. Cash Ticket issue/accountability remains distinct from OR. Whether one physical CT may carry multiple CT-compatible lines is not assumed without explicit office policy.

## Document Total
The sum of the Collection Lines represented by the active accountable document.

## Instrument Compatibility
The rule that determines whether a Revenue Classification belongs to OR or Cash Ticket for the applicable tenant/date. Latest Cantilan clarification: Weight & Measure = OR, WCF = CT, Tabo = OR, and Vegetable/Fruit Space Rental uses OR for full/whole payment and CT for daily transactions. IA-046 is therefore resolved. A posted Collection still resolves to exactly one instrument family and never mixes OR and CT lines on one accountable document.

## Fish/Meat Vendor Fee
A reportable revenue classification distinct from Weight & Measure when the office records them separately.

## Weight & Measure
A reportable revenue classification for the confirmed Cantilan policy and an Official Receipt item.

## Slaughterhouse Breakdown
Transparent calculation detail for the fixed/approved slaughterhouse charge package. The component breakdown does not become separate revenue classifications unless the office formally reports those components independently.

## Collection Composer
The shared working surface used to assemble compatible Collection Lines before money is posted. It may be entered from a specialized Operation or from a Payor/Account, but both paths represent the same collection workflow.

## Draft Collection
An unposted working collection. In the approved Web target, it is server-persisted with a stable DraftId and revision, but creates no revenue, money allocation, document consumption, RCD entry or Collection Activity. Review and successful canonical posting create the resulting financial history. See [ADR-004](docs/decisions/ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md).

## Collection Session
A user-facing visit/work session that may result in more than one Collection when the payer is settling both OR-compatible and CT-compatible items. A Collection Session is not itself a financial transaction.

## Explicit Allocation
A visible user-confirmed allocation of a payment amount to specific obligations or periods. StallTrack may suggest an allocation, but it must not silently decide the final allocation without confirmed policy.

## Utility Operation Scope
ECF and WCF are broader EEMO Utility Operations, not globally owned by the NPM facility. An NPM stall may be one utility service subject/context, but NPM must not be the architectural parent of every ECF/WCF assessment. Existing NPM-bound UtilityBill rows remain valid legacy/current source evidence. Target generalization must be additive and preserve those records rather than rewriting them.

## Partial Utility Payment
A valid partial settlement of an ECF or WCF obligation that leaves the exact remaining balance outstanding.

## Cash Ticket Transaction
One recorded issuance/use of a Cash Ticket for a CT-compatible collection. Individual CT transactions are the source from which category totals and RCD-style summaries are derived.

## Accountable Form Assignment
Custody of an OR or CT series/range assigned to a collector/accountable officer. Normal collection should consume a valid assigned number rather than accept unrestricted free-form numbering.

## Issued Document Correction
An issued OR or CT is not edited in place. Correction uses attributable void/reversal/replacement history, and the original physical number remains permanently consumed.

## Historical Charge Snapshot
The preserved quantity, rate, readings, category, basis, and other calculation detail applicable when a charge/collection was posted. Later configuration changes do not recalculate historical itemization.

## Policy-Resolved Instrument
The OR-versus-CT choice is resolved from the Revenue Classification policy. Staff do not override the instrument ad hoc. If a user attempts to combine incompatible items, StallTrack separates them into distinct collections/documents.

## Current Collection
The persistent draft collection visible while staff move between Operations and Payor/Account workflows. It may collect multiple compatible lines for one payor until explicitly reviewed, posted, or discarded.

## Approved Charge Line
A Collection Line must map to an approved Revenue Classification/charge definition. Arbitrary free-text financial lines are not allowed; optional descriptive detail does not create a new revenue identity.

## Governed Configurable Service
A tenant-owned operational service whose structurally simple financial policy is defined through approved configuration rather than hard-coded guesswork. Required configuration may include stable service identity, Revenue Classification, effective-dated OR/CT policy, calculation basis/rate, Payor requirement, operational fields, active state, and allowed channels. It does not allow free-form collector-created charges. See [ADR-006](docs/decisions/ADR_006_GOVERNED_CONFIGURABLE_SERVICE_OPERATIONS.md).

## Setup Required Operation
A known operation that may appear in authorized Web directory/setup surfaces while required financial policy is incomplete. It cannot create a financial Collection until the required configuration is valid and active. Transfer Large Cattle may use this state until its Cantilan fee/accountable-form configuration is complete; the Head has already confirmed the operation is a transfer with a corresponding direct approved amount.

## Mobile Configured Operation
A focused Collector Mobile workflow exposed only when the operation is Active, Mobile-enabled, authorized/assigned to the collector, and compatible with accountable-document custody. The collector supplies transaction facts; the specialized source or approved configuration supplies classification, instrument, rate/calculation policy, and document requirements.
## Obligation Payment Limit
An obligation-backed Collection Line cannot allocate more than the amount actually outstanding. V2 does not create unapplied customer credit, deposit, or advance balances unless a future approved business rule explicitly introduces them.

## Document Consumption
On Web, an OR/CT number is consumed when posting succeeds. On Mobile offline, a physically issued assigned CT is marked locally consumed immediately and remains consumed even if later synchronization fails.

## Collection Activity Record
The primary Collection Activity row represents one posted collection/document event. Itemized Collection Lines appear as expandable/detail content beneath that event rather than as unrelated top-level transactions.

## Derived RCD
RCD/category totals are derived from posted Collection Lines and accountable-document usage, grouped by the applicable business date, collector, instrument, and revenue classification. Staff do not re-enter the same financial totals manually.

## Legacy Financial History
Pre-itemization records remain valid historical evidence. StallTrack adapts only facts actually stored and never invents old receipt groupings, line breakdowns, allocations, CT numbers, vehicle classes, or document composition.

## Payor Collect View
A Payor/Account may start a collection from the payor context by showing open OR-compatible and CT-compatible items separately. It feeds the same Collection Composer used by Operations.

## Collection Detail Levels
The document/receipt view shows clean financial lines and totals. The audit/detail view additionally preserves source operation, facility, stall/space, obligation period, allocation, quantity/rate, readings, calculation basis, and historical policy snapshot where applicable.


## Web Collection Authority
Head and Admin may create, assemble, allocate, review, and post normal office collections through the shared Collection Composer.

## Mobile Collection Authority
Collector uses the same canonical backend collection model through focused Mobile workflows limited to assigned facilities/operations. The Mobile app does not initially expose the full cross-operation office Collection Composer.

## Manual Approved Authority
A Manual Approved amount may be entered operationally by Head or Admin only when the approved charge definition explicitly permits that basis. It does not authorize inventing a new charge or changing a configured ordinance/rate.

## Posting Authority
Head and Admin may post normal Web collections. Collector may post/sync Mobile collections within assigned operational authority. Payor-originated online payments remain a separate channel but converge on canonical collection truth.

## Correction Authority
Head and Admin may void/replace issued OR or CT records through attributable, audited correction flows. Issued document history is retained; Collector cannot silently delete or rewrite a synced issued document.

## Accountable Form Management Authority
Head and Admin may manage OR/CT books, series/ranges, assignment, and office custody within their LGU. Collectors may consume only accountable-form units assigned to them.

## Physical Document Requirement
A normal physical office collection cannot be posted without its required valid OR/CT number. Controlled exception workflows such as legitimate Awaiting OR online payments remain separate and do not weaken this rule.

## Draft Ownership
A Web draft belongs to its tenant and owning user and can be recovered in a later authenticated session. Mutations require its expected revision; review binds the exact meaningful financial state/revision. Posting revalidates current facts and requires renewed review for material changes. One DraftId produces at most one successful Collection, even across different ClientOperationId values. Initial ownership excludes shared editing/handoff; discard remains non-financial. See [ADR-004](docs/decisions/ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md).

## Concurrent Posting Guard
Final posting revalidates outstanding amounts, allocations, document-number uniqueness/custody, instrument policy, and totals atomically. A stale concurrent attempt fails cleanly instead of double-settling an obligation.

## Itemized Collection Statement
StallTrack may print an Itemized Collection Details / Collection Statement linked to the physical OR/CT number. It must not present that digital statement as a replacement government Official Receipt or Cash Ticket.
