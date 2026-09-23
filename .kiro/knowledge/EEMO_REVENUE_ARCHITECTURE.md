# EEMO Revenue Architecture

**Status:** Planning approved; implementation has not started.
**Scope:** The approved target architecture and information design for StallTrack's broader EEMO revenue-management capabilities.
**Business authority:** The latest direct clarification from the Cantilan EEMO Head, as recorded through Pass 1 and approved in Pass 2.

This document records intended future semantics and the approved migration direction. It does not claim that the current application already implements them. Current code, migrations, tests, workflows and verified production behavior show what the system does today. If evidence conflicts with an intended rule, surface the contradiction and determine which source is stale before changing behavior. Do not silently choose the implementation over business documentation.

## 1. Purpose and authority

StallTrack is evolving from a primarily stall and rental system into a broader platform for EEMO operations, assessments, collections, accountable documents and classified revenue reporting. Its mature stall, occupancy, contract, NPM settlement and facility domains remain valuable and are not being replaced by one generic transaction model.

The authority labels used below mean:

- **Confirmed business rule:** clarified directly by the EEMO Head. It supersedes older contradictory business assumptions.
- **Target architecture decision:** approved design for future implementation; it does not assert current runtime support.
- **Business decision gate:** an unresolved ruling that must not be guessed where it changes financial or accountability behavior.
- **Migration compromise:** a truthful representation of old data whose detail cannot be reconstructed.

Accepted architecture and business documents record intended meaning. Current implementation artifacts are evidence of behavior. Contradictions must be investigated rather than silently resolved by choosing whichever source appears first.

## 2. Core financial model

The target separates the operation and its obligation from the money event, the accountable document, the classified lines and any allocation:

    Operation / specialized obligation or activity
                        |
                  money received
                        v
                   Collection
                 /      |       \
         classified   allocation  accountable document history
           lines       if owed     OR or Cash Ticket
                 \      |       /
                 reporting and reconciliation

These concepts are not interchangeable:

- **Obligation or assessment:** what the payer owes under its owning domain.
- **Collection:** money received in a specific event, attributed to a business date and actor.
- **OR or Cash Ticket document:** the accountable instrument and physical number that records a compatible collection.
- **Revenue classification:** the official reporting meaning of a collected amount.
- **Allocation:** how collected money is applied to one or more obligations when applicable.

A collection may have multiple classified lines and may relate to multiple historical documents over its lifetime. The active document presents the collection's compatible classified lines; it must not independently change or duplicate their financial meaning. A single current issued document is allowed at a time. OR-compatible and Cash-Ticket-compatible lines cannot be mixed on one document.

## 3. Source-of-truth rules

| Question | Authoritative target source |
|---|---|
| What is owed? | The specialized obligation domain, such as PaymentRecord/monthly rental, UtilityBill, or the NPM month-settlement ledger. A genuinely new obligation type may make ReceivableObligation authoritative only by explicit design. |
| What money was received? | A posted Collection event. |
| Which record owns the physical OR/CT number? | AccountableDocument, linked to its permanently consumed physical form unit when managed as inventory. |
| Which official revenue category owns the amount? | CollectionLine classification, using stable classification identity rather than display text. |
| What remains unpaid? | The owning obligation calculation less valid credits, settlements and allocations. The generic allocation anchor must not recalculate a specialized obligation. |
| What drives RCD and Monthly Income? | Posted classified collection lines, using the applicable collection-date basis. |
| What drives Collection Efficiency? | Authoritative obligation and settlement calculations over a declared obligation period. |
| What drives Revenue Target Attainment? | Classified cash revenue compared with the approved target for the declared target period. |
| Who collected or held the form? | Attributed Collection actor and accountable-form assignment/custody history. |

## 4. Confirmed Cantilan business rules

### Delinquency and Arrears

- An account is **Delinquent** once it is at least one month behind.
- **Arrears** means old or lapsed debt, not simply one or two currently unpaid months.
- The exact age or status boundary for old/lapsed debt remains a Business Decision Gate.
- When qualifying old debt is collected, its revenue classification is **Arrears** while its source obligation remains identifiable as TCC, NCC, NPM or another original source.
- The system must preserve both the origin obligation and the revenue classification at recovery. It must not infer either from a name or report-time string matching.

### Monthly obligations and flexible installments

TCC, NCC, BBQ and similar monthly rental facilities remain monthly obligations. Their collection cadence may include multiple installments, each with its own Official Receipt. Flexible or daily installments do not make those obligations NPM-style daily billing.

### NPM remains specialized

NPM retains DailyCollection, RentGoal/PureDays, closures, absences, month-end adjustment and NpmMonthSettlementService as its authoritative operational and financial rules. Preserve its specialized calculation and daily collector workflow. Do not materialize NPM into generic receivables for uniformity or duplicate its arithmetic in presentation code.

### Cantilan instrument policy

This is the confirmed Cantilan tenant policy, not a universal rule for every municipality:

| Official Receipt | Cash Ticket |
|---|---|
| Permanent stall rent | Market Fees |
| ECF | Tabo |
| Fish/Meat Vendor Fee | Transportation/Parking |
| Weight & Measure | Vegetable/Fruit Space Rental |
| Penalties/Fines | WCF |
| Slaughterhouse | Landing/Berthing |

One OR may contain multiple compatible itemized lines. Cash-Ticket lines cannot be inserted into that OR; OR and Cash Ticket are separate accountable instruments. Other tenants' permitted instrument policy is tenant-owned and effective-dated.

### Other confirmed distinctions

- Vegetable/Fruit Space Rental is temporary/open-space revenue, not permanent NPM stall tenancy.
- Fish/Meat Vendor Fee and Weight & Measure are separate reportable classifications, even when collected from the same vendor.
- Transportation rates are based on configured vehicle class and effective-dated rates, not one universal per-trip amount.
- Penalties and slaughter add-ons are controlled by approved configuration, not arbitrary collector-entered prices.
- The configured standard slaughter package remains valid; approved add-ons are selected from controlled configuration. ECF and WCF are separate revenue classifications, may be collected separately from rent, and retain their respective Cantilan OR/CT policy.
- Payor identity may be optional for appropriate transactional Cash Ticket collections.
- Accountable-form inventory and annual revenue targets are part of the target system.
- Lot/event rentals must not be represented as permanent stall contracts merely because they occur at a market.
- RCD-style collection classification and full accountability are in scope; a Treasury approval workflow is not. Do not resurrect the retired partial-remittance workflow as a substitute.

## 5. Target domain components

These are conceptual responsibilities, not an EF schema prescription. Tenant-owned configuration and records must remain tenant-scoped, attributable, backed up and restorable. Financial events and issued documents retain history; mutable catalogs may be deactivated rather than erasing referenced meaning.

| Component | Responsibility and key rules |
|---|---|
| RevenueClassification | Stable semantic identity/code, independent of display wording. Prefer stable canonical codes for common meanings with controlled tenant-specific sources where needed. Tenant-visible name, report group/order and active state are configuration. No business logic compares display strings. |
| RevenueClassificationVersion / policy | Tenant-owned policy version for display/report configuration and allowed instruments. Allowed OR/CT policy is effective-dated and may differ by tenant and date; classification meaning remains stable. A posted line retains the applicable classification/policy reference needed to reproduce its meaning. |
| Collection | Immutable posted money-received event: tenant, business date, actor/collector, optional payer, operation context, total, client idempotency key and lifecycle/reversal references. Corrections append an attributable reversal/correction rather than rewriting posted history. |
| CollectionLine | Classified portion of a Collection: stable revenue identity, amount, source/origin reference and supported quantity/rate detail. Preserve origin obligation separately from recovery classification. Posted lines are immutable; corrections are explicit. |
| CollectionAllocation | Explicit amount applied from a collection line to one or more source obligations. Enforce tenant ownership, valid outstanding amounts and auditable allocation/reversal. Do not invent automatic allocation order before policy is approved. |
| ReceivableObligation | Allocation anchor and assessment snapshot for adapting an existing domain, with source type/id, period and detail quality. PaymentRecord, UtilityBill and NPM remain authoritative for their own amounts owed. A new obligation type may use this as its authority only through an explicit authority mode and design. |
| AccountableDocument | OR or CT identity, physical number, status dates, owning collection, current-state flag and replacement links. Number uniqueness is tenant-scoped within the applicable instrument/series. Immutable after issue; void/replacement appends a new document. At most one current/active document per collection. |
| AccountableFormBatch | Received series/booklet/range, form type, source/reference and receipt date. Defines a traceable inventory range, not a fictitious used transaction. |
| AccountableFormUnit | Individual serial/control number and permanent state history. A unit is consumed when issued, including if its document is later voided. It never returns to available inventory. |
| AccountableFormAssignment | Custody transfer to a collector/accountable officer with issuer, recipient, date and scope. History is attributable and append-only. |
| ChargeDefinition / ChargeRate | Approved charge identity, calculation basis and effective-dated tenant rate. Resolves through controlled configuration; it does not independently decide obligation balance. |
| VehicleClass | Tenant-configurable stable vehicle identity used by transport/parking charges. Effective-dated rate is separate from its display name; trip, driver, plate and route may be optional operational context. |
| PenaltyDefinition | Approved penalty type, applicability, rate/amount rule and effective dates. Collectors select an approved charge; they do not invent financial meaning or price. |
| SlaughterApprovedAddon | Approved configurable add-on and effective rate, selected in addition to the standard package. Whether each component is a separate official classification remains a decision gate. |
| RevenueTarget | Tenant target amount and period linked to classification/group. Revisions must be attributable and historically explainable. Target calendar, fiscal year and governance remain unresolved. |
| Temporary/Event Rental | Separate activity for event/lot/open-space rental with date/event, location/lot, renter, optional area/supporting details and configured charges. It is not a permanent Stall/Contract. Instrument policy must be resolved before Lot Rental collections are issued. |

Common lifecycle rules: every tenant-owned catalog, policy and transaction is tenant-scoped and included in backup/restore. Tenant-scoped uniqueness is enforced for identities and accountable numbers. Rates and allowed instruments are effective-dated where stated; posted money and issued-document meaning remain reproducible after later configuration changes. Posted collections, lines, allocations and issued documents are retained as audit evidence; corrections append reversal/replacement history. Mutable setup is audited and deactivated or versioned rather than deleted when referenced. Exact soft-delete mechanics remain an implementation choice, but soft deletion must not release an issued number or erase financial history.

## 6. Pass 2.5 hardened rules

### Accountable-document history

- One Collection may relate to **zero or more AccountableDocuments over its lifetime**.
- At most one related document may be current/active.
- An issued document is immutable.
- Replacement creates a new document and an explicit replacement link; the old number is never mutated into the replacement number.
- Voided and replaced documents remain permanently retained.
- The physical form unit is permanently consumed after issue, even when its document is voided.

### Receivable-obligation boundary

For existing specialized domains, ReceivableObligation is an allocation anchor and immutable assessed snapshot/projection. It cannot become a second billing engine. PaymentRecord, UtilityBill and NPM settlement remain authoritative for what is owed. NPM is not materialized merely to make the model look uniform. A genuinely new obligation type may use ReceivableObligation as its authority only when explicitly designed and tested as such.

### Instrument policy

Revenue classification establishes stable semantic identity. Allowed instruments are tenant-owned effective-dated policy. Cantilan's confirmed mapping above is preserved exactly for Cantilan; it is not silently imposed on other LGUs.

## 7. Current-to-target compatibility

Existing specialized module rows remain historical evidence. Additive adapters link future collections and classified lines to those domains; the common layer surrounds the specialized rules.

| Current component | Target participation and compatibility |
|---|---|
| PaymentRecord | Remains the monthly obligation/status projection during transition. It is not one receipt. Existing rows and OR values remain historical evidence. After cutover, new money-received history comes from Collection and allocations; do not destructively reconstruct installments that were never stored. |
| DailyCollection | Remains authoritative for NPM daily marks, amount, business date, absence and RentGoal month-end adjustment. A later adapter may link its captured money to Collection/classified lines without moving month calculation into the generic ledger. |
| UtilityBill | Remains authoritative for utility assessment/balance. New payment events can link to it. Existing utility totals or receipt fields do not justify invented installment detail. |
| TpmAttendance | Remains the Tabo-an market-day activity/source. Future collections classify the resulting revenue and use the tenant's configured Cash Ticket policy. |
| TrmTrip | Remains historical trip activity. Future transport/parking design moves toward vehicle-class, effective-rate and Cash Ticket semantics. Do not infer a historical vehicle class or turn an old OR value into a CT serial. |
| SlaughterTransaction | Remains the per-animal activity and preserves known package breakdown. Future collection/document links expose approved classifications/add-ons without replacing the activity model. |
| OnlinePaymentTransaction | Remains the payment-provider lifecycle and idempotency record. A settled provider payment can produce/link to Collection; official document issuance may follow asynchronously. The provider record is not itself an OR/CT. |
| OrNumberRegistry | Continues to protect tenant-scoped number reservation/uniqueness during transition. It is not the authoritative receipt parent. Reconcile it with AccountableDocument uniqueness before migrating writers. |
| OrSeriesConfig | Remains tenant OR-series configuration/operational context. It does not mean StallTrack generates the physical OR number. Preserve current sequence/state unless a separately approved accountable-form workflow changes its role. |

## 8. Reporting model and dates

Keep receivable performance and cash revenue in separate report families.

| Report family | Question and source |
|---|---|
| Receivables / obligations | What was due, settled, credited, collected against the obligation, and left outstanding? Use the owning domain's billing period and calculation. Collection Efficiency is collected against obligation, not revenue target attainment. |
| Cash revenue | What money was received by classification, instrument, collector and date? Use posted classified collection lines. RCD and Monthly Income are built from this classified cash source. |
| Management targets | How much classified revenue was received against an approved annual target? Show annual target, monthly actuals, YTD and target attainment separately from collection efficiency. |

Date meanings must remain explicit:

- **Activity date:** when the operational event occurred, such as a slaughter or transport activity.
- **Obligation period:** the month or other period in which the amount became due.
- **Collection business date:** when money was received; drives cash collection reporting and collector accountability.
- **Document issue date:** when an OR/CT was issued; drives accountable-document history and issue-date views.

Reports declare their basis visibly. A monthly obligation can legitimately be reported as a period amount under PureDays; that does not make it fixed monthly rent.

## 9. Legacy data and migration policy

Production migration is additive. Do not invent history or destructively replace module rows.

| Legacy source | Truthful treatment |
|---|---|
| PaymentRecord | Keep as historical obligation/state evidence. A cumulative amount and one OR cannot reconstruct multiple installment events. Mark imported aggregate detail as unknown. |
| DailyCollection | Preserve date, paid/absent and known adjustment fields. Create classified detail only where mapping is deterministic; do not fabricate OR lines or move NPM arithmetic. |
| UtilityBill | Preserve the bill and known totals. Repeated partial collections cannot be recreated unless separately recorded in trustworthy evidence. |
| TpmAttendance | Preserve market-day activity and any known legacy number. Do not invent a Cash Ticket serial where no such inventory existed. |
| TrmTrip | Preserve trip, amount and known fields. Vehicle class is unknown unless explicitly stored in reliable source data. |
| SlaughterTransaction | Preserve activity and recorded component breakdown. Do not assume historical breakdown equals the effective tenant-resolved amount or decide unapproved classification detail. |
| OnlinePaymentTransaction | Preserve provider identifiers, settlement state and existing links. Reconcile to collections only through deterministic relations; do not infer an absent OR. |
| OR fields and registry rows | Retain old module fields during the compatibility period. Cross-check duplicates and existing numbers before document cutover; never silently rewrite a physical number. |

Use an explicit LegacyAggregate / DetailUnknown representation only where a report or migration needs to state that the source is an aggregate whose line detail is unavailable. Do not manufacture old installment events, CT inventory, TRM vehicle classes or source allocations. New tables and relationships must be included in tenant backup, export and restore coverage before they become authoritative.

## 10. Target information architecture

The approved top-level workspaces are:

1. Dashboard
2. Operations
3. Collections
4. Payors & Accounts
5. Monitoring
6. Accountable Forms
7. Reports
8. Administration

Operations groups work by operation (Public Market, rental facilities, slaughterhouse, transportation/parking and other enterprises). Public Market keeps permanent stalls distinct from market collections, temporary space, Tabo, utilities and vendor charges.

Collections provides Today, History, a general Record Collection entry, Awaiting Document and Corrections/Reversals. Specialized collection should normally begin from its operation/account/activity and open a context-aware capture flow; the general entry progressively asks for context and is not a form containing every possible field.

Payors & Accounts owns the canonical browse/detail view for Outstanding Obligations. Monitoring owns attention and action queues: delinquency, old/lapsed debt, exceptions, expiring/expired occupancies and follow-up.

Reports separates Receivables, Cash Revenue, Management and Operational reporting. Administration groups setup, including Revenue Setup and **Revenue Target Setup**, apart from read-only Management target reports.

Unreleased capabilities remain hidden. Do not add empty workspace navigation. Preserve existing routes during rollout with aliases or redirects where needed.

## 11. Approved UI design rules

- Retain the restrained navy/gold identity on white and neutral work surfaces.
- Use a compact WorkspaceHeader instead of a large hero on administrative pages.
- Use tables for registers, histories, inventories and reports; cards summarize rather than replace them.
- Give each page one strong primary action.
- Use semantic status families with explicit wording; never rely on color alone.
- Do not assign facility/revenue categories a rainbow of colors.
- Use context-aware collection capture; the general entry point is progressive, not universal.
- Give canonical obligations, collections, accountable documents, form units and activities stable detail pages.
- Use an official ReportShell and make obligation-period, collection-date or document-issue-date basis explicit.
- Keep web responsive while preserving table identity and access to columns.
- Keep Collector Mobile a separate, shallow, task-focused interface with visible offline and sync state.
- Keep the Payor Portal simple: Home, Balances, Payment & Receipt History, Profile.
- Do not expose internal terms such as allocation adapter or projection as routine staff vocabulary.
- Use a full page for canonical financial history; reserve drawers for previews and modals for short, bounded tasks.

Vocabulary boundary:

- **Paid** describes an obligation state.
- **Collected** describes money received.
- **Issued** describes an accountable-document state.

Partial, queued offline, awaiting document, reversed and voided states remain distinct.

## 12. Approved implementation roadmap

Each phase is additive and independently reviewable. Specialized obligation calculations remain authoritative throughout; writers/readers migrate only after their compatibility behavior is proven.

| Phase | Purpose and dependency |
|---|---|
| Phase 0 — correctness baseline | Fix or explicitly disposition independent verified correctness defects, prepare test baselines and decision records. This is the next implementation phase; it does not start a Collection/Receipt/CT migration. |
| Phase 1 — Revenue Classification | Establish stable semantic codes, tenant catalog/presentation and effective-dated instrument policy. Seed only rulings that are confirmed; do not block architectural groundwork by guessing the final catalog. |
| Phase 2 — Collection ledger | Add immutable money-received events, attribution, idempotency and reversal history. Keep module writes compatible until controlled cutover is proven. |
| Phase 3 — AccountableDocument / OR pilot | Establish OR ownership, document history/replacement and compatible itemized lines for a bounded OR workflow. Reconcile existing OR registry and legacy fields before switching writers. |
| Phase 4 — shadow classified reporting | Compare classified collection projections with current reports without replacing their official source. Investigate every difference. |
| Phase 5 — monthly installments | Adapt monthly obligations and PaymentRecord state to multiple immutable collections and explicit allocations. Preserve historical PaymentRecords; never synthesize missing old installments. |
| Phase 5B — delinquency/Arrears transition | Apply the clarified account-status/recovery classifications only after the old/lapsed boundary and required historical treatment are approved. |
| Phase 6 — Cash Ticket / accountable forms | Add CT document issuance, batches, units, custody, used/spoiled/cancelled history and reconciliation after operating policy is decided. |
| Phase 7 — source adapters | Adapt utilities, TPM, transportation, fish/weight, penalties, slaughter and other sources as appropriate. Each specialized activity retains its owning domain. |
| Phase 8 — production classified reporting | Switch approved RCD, Monthly Income and collector revenue views to verified posted classified lines after shadow reconciliation. |
| Phase 9 — annual targets | Add target setup, revisions, YTD and attainment after target period/governance rules are decided. Keep attainment separate from Collection Efficiency. |
| Later — broader EEMO operations | Add event rentals, additional enterprise operations and other configured revenue sources without forcing every source into Facility or Stall/Contract. |

### UI migration dependency map

- **UI-0:** tokens and accessibility primitives; independent of revenue schema.
- **UI-1:** shell, workspace navigation and shared headers/tables/filters.
- **UI-2:** migrate existing facility, vendor, transaction, monitoring, report and administration screens incrementally.
- **UI-3:** expose Pass 3 workspace navigation only as capabilities ship.
- **UI-4:** collection, obligation and document detail/composition after ledger and OR capabilities exist.
- **UI-5:** Accountable Forms and CT capture after Phase 6.
- **UI-6:** classified cash reports and target reports after Phases 8 and 9.

## 13. Current phase

**Current architecture status:** Planning approved.

**Next implementation phase:** Phase 0 — correctness baseline and decision preparation.

No Collection/Receipt/CT production migration has started. Existing module records and report calculations remain the current runtime behavior until a later approved implementation changes them.

## 14. Business decision gates

| Decision gate | Blocks or constrains |
|---|---|
| Exact old/lapsed Arrears qualification boundary | Phase 5B status transition, arrears recovery classification and historical reports. |
| Final complete official revenue-classification catalog/codes | Completing production catalog coverage in Phase 1 and official classification coverage in Phase 8. Confirmed sources may be modeled without inventing the remainder. |
| Automatic allocation policy for partial or multi-obligation collections | Any automatic allocation behavior in Phase 5. The model may support explicit allocations; do not guess ordering or split rules. |
| Lot Rental OR/CT instrument | Issuing Lot Rental documents and production collection flow for that source in Phase 7/later operations. |
| Detailed Cash Ticket series and custody operating policy | Production batch issuance, collector assignment and reconciliation in Phase 6. |
| Final vehicle-class catalog and rates | Configured production transportation/parking catalog and rate rollout in Phases 6–7. |
| Whether slaughter package components are separate official classes or transparency-only detail | Final SLH classification adapter and report detail in Phases 7–8. |
| Annual-target period, governance and revision policy | Production Revenue Target Setup and attainment semantics in Phase 9. |

Configuration can safely hold tenant-varying names, rates and allowed instruments. It cannot substitute for a ruling where the choice changes instrument custody, allocation, classification or historical liability.

## 15. Phase 0 correctness preparation

The following defects were found independently of the target architecture and must be handled as separate correctness work, not hidden inside the migration:

- UtilityBill is financially significant but was absent from automatic financial audit coverage.
- TransactionFeed was audited and is already correct: DailyFee includes MonthEndAdjustment, and TransactionFeed does not add the traceability field again. The actual Phase 0D defect was double-counting the adjustment in compliance and collector-report projections.
- UpdateSlaughterCommandHandler may reconstruct charges without IFeeRateResolver.

Classify and test each before its owning migration touches that path. Do not combine them into a broad architecture rewrite.
