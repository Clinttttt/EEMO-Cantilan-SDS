# StallTrack Interface Decision Registry

**Status:** Active interface decision registry
**Baseline:** `22f45239adf998d22c33e402aa4c87e2465ac106`
**Architecture:** [INFORMATION_ARCHITECTURE.md](../interface/INFORMATION_ARCHITECTURE.md)
**Migration:** [MIGRATION_PLAN.md](../interface/MIGRATION_PLAN.md)

## 1. Purpose

This registry prevents current runtime facts, approved target architecture, UX proposals, unresolved business questions, technical constraints, and future capabilities from being treated as interchangeable.

Only these values are used:

**TYPE**

- `RUNTIME FACT`
- `APPROVED ARCHITECTURE`
- `UX DECISION`
- `BUSINESS DECISION GATE`
- `TECHNICAL CONSTRAINT`
- `FUTURE CAPABILITY`

**STATUS**

- `CONFIRMED`
- `PROPOSED`
- `NEEDS EEMO INPUT`
- `BLOCKED`
- `FUTURE`
- `SUPERSEDED`

`CONFIRMED` for interface architecture means approved interface direction. It does not claim that the target interface is implemented.

## 2. Known authority reconciliations

Two cross-document distinctions are explicit at this baseline:

1. [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md) names the target landing workspace `Dashboard`. The current interface architecture ruling names it `Overview`. This registry treats Overview as the target interface label while preserving the same portfolio-summary purpose. This is an interface wording refinement, not a financial/domain change.
2. Baseline code, tests, and the accepted EEMO ruling agree that `DomainRules.DelinquentThresholdMonths = 1`: every active account with at least one fully elapsed unpaid month is delinquent, while Arrears is reserved for old/lapsed-year stall debt that remains owed. Three months is only an urgency/severity boundary.

## 3. Decisions

### IA-001 — Current financial source authority

- **ID:** IA-001
- **SUBJECT:** Specialized production sources and the generic collection foundation
- **STATUS:** CONFIRMED
- **TYPE:** RUNTIME FACT
- **DECISION / QUESTION:** `PaymentRecord`, `DailyCollection`/NPM settlement, utilities, TPM attendance, TRM trips, slaughter transactions, and online-payment lifecycle records retain their established production authority. `Collection` and `CollectionLine` are not the universal production writer or report source.
- **RATIONALE:** Interface unification must not imply a financial cutover.
- **EVIDENCE / SOURCE:** [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), sections 3, 7, 12, and 13; baseline code and migrations.
- **IMPACT:** Workspace and component changes must continue to read and write through the current authoritative source for each workflow.
- **REVISIT CONDITION:** Revisit per source only after its approved writer/reader migration, reconciliation, and production cutover.

### IA-002 — Target Web workspace model

- **ID:** IA-002
- **SUBJECT:** Canonical Web Office workspaces
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Released Web workspaces are Overview, Operations, Collections, Payors & Accounts, Monitoring, Reports, and Administration. Accountable Forms has reserved future placement and remains hidden.
- **RATIONALE:** The model separates operational context, money received, business accounts, attention queues, read-only reporting, and administration.
- **EVIDENCE / SOURCE:** Current interface architecture ruling; [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), section 10, with the Dashboard-to-Overview label refinement recorded in IA-003.
- **IMPACT:** New global navigation and canonical routes must use these ownership boundaries.
- **REVISIT CONDITION:** Revisit only if a new stable user responsibility cannot fit without distorting an existing workspace.

### IA-003 — Overview replaces Dashboard as the target label

- **ID:** IA-003
- **SUBJECT:** Portfolio landing-workspace label
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Use `Overview` as the target label. It retains the earlier Dashboard workspace's portfolio summary, today/activity, attention, and facility-status purpose.
- **RATIONALE:** Overview names the information role without prescribing a card-heavy visual pattern.
- **EVIDENCE / SOURCE:** Current interface architecture ruling; earlier target label in [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), section 10.
- **IMPACT:** `/overview` becomes the target canonical landing route; `/menu` remains compatible during migration.
- **REVISIT CONDITION:** Revisit if EEMO user testing shows Dashboard is materially clearer to office users.

### IA-004 — Facilities hub and selected-facility context

- **ID:** IA-004
- **SUBJECT:** Global facility navigation
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Use one Facilities entry/hub, a selected-facility context, and a facility switcher. Custom facilities must not create permanent global-sidebar growth.
- **RATIONALE:** Facility is an operational scope, while the global sidebar represents stable responsibilities.
- **EVIDENCE / SOURCE:** Current route/sidebar audit; target facility architecture in [INFORMATION_ARCHITECTURE.md](../interface/INFORMATION_ARCHITECTURE.md).
- **IMPACT:** Existing facility routes remain; global navigation eventually removes per-facility rows after the hub is proven.
- **REVISIT CONDITION:** Revisit the switcher interaction after office usability validation, not the one-hub ownership principle.

### IA-005 — Specialized facility workflows remain domain-owned

- **ID:** IA-005
- **SUBJECT:** Shared facility shell versus shared business workflow
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Share context, headers, switching, structural sections, and report entry points. Keep NPM, monthly rental, TPM, TRM, SLH, utilities, and custom-facility business workflows in their owning domains.
- **RATIONALE:** Similar placement does not make billing, activity, or collection semantics interchangeable.
- **EVIDENCE / SOURCE:** [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), sections 3, 4, and 7; [ARCHITECTURE_RULES.md](../architecture/ARCHITECTURE_RULES.md), money rules.
- **IMPACT:** `FacilityShell` may compose specialized work but may not calculate obligations or issue generic commands.
- **REVISIT CONDITION:** Revisit a domain boundary only through a separately approved domain architecture change.

### IA-006 — Reports are read-only

- **ID:** IA-006
- **SUBJECT:** Reports workspace ownership
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Reports are read-only analytical or document surfaces. Mutable collection, correction, follow-up, closure, renewal, and account-lifecycle work belongs to operational workspaces.
- **RATIONALE:** Users must be able to predict whether a report can change production state.
- **EVIDENCE / SOURCE:** Current audit of Collection Manager, Follow-up, Whole-time History, and Closed Accounts; reporting model in [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), section 8.
- **IMPACT:** Existing mutable pages are rehomed before report families are consolidated.
- **REVISIT CONDITION:** None for the principle; a read-only report may link to an authorized operational action outside the report.

### IA-007 — Administrator Accounts namespace migration

- **ID:** IA-007
- **SUBJECT:** Current `/accounts` route and future business-account namespace
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Move administrator/collector login-account management to Administration > People & Access before using `/accounts` for payor/occupancy financial relationships.
- **RATIONALE:** Login accounts and business accounts are different concepts and require different role boundaries.
- **EVIDENCE / SOURCE:** Current `Menus/Accounts.razor`; target account architecture.
- **IMPACT:** `/admin/access/administrators` is introduced first; current `/accounts` remains an alias during transition.
- **REVISIT CONDITION:** Revisit route details if the framework requires a different collision-free alias sequence.

### IA-008 — Payors & Accounts workspace terminology

- **ID:** IA-008
- **SUBJECT:** Canonical business-account workspace label
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Use `Payors & Accounts` for the target workspace. Account means the interface view of one occupancy/term and its financial relationship; it is not a new backend aggregate requirement.
- **RATIONALE:** The label supports browse-by-person and browse-by-financial-relationship tasks while retaining occupancy context.
- **EVIDENCE / SOURCE:** [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), section 10; target account model.
- **IMPACT:** Page labels must continue to identify Space/Stall and Occupancy/Term rather than flattening them into Account.
- **REVISIT CONDITION:** Revisit the displayed label if EEMO usability validation shows `Accounts` is interpreted primarily as login credentials after namespace migration.

### IA-009 — Occupancy/Term terminology and liability ownership

- **ID:** IA-009
- **SUBJECT:** Space, occupancy, payor, and account relationship
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Space/Stall, Occupancy/Term, Payor, and Account remain distinct. Occupancy/Term identifies the period that owns liability; current and earlier terms remain separate.
- **RATIONALE:** A stall outlives its holders, and historical liability cannot be inferred from the current occupant or rate.
- **EVIDENCE / SOURCE:** [ARCHITECTURE_RULES.md](../architecture/ARCHITECTURE_RULES.md), money rules; `StallOccupancy.AnsweringForMonth`; current Stall Profile and closed-account behavior.
- **IMPACT:** Account detail and reporting must identify term/occupancy scope for balances and history.
- **REVISIT CONDITION:** Terminology may be localized, but the distinctions cannot be removed without a business/domain ruling.

### IA-010 — Person terminology by operation

- **ID:** IA-010
- **SUBJECT:** Cross-facility person terminology
- **STATUS:** CONFIRMED
- **TYPE:** UX DECISION
- **DECISION / QUESTION:** Use domain-specific person terms rather than `Vendor` universally: temporary/TPM sellers use **Vendor**; permanent rental-space holders use **Occupant**; the financially responsible or paying party is **Payor**; TRM uses **Transporter**; Slaughterhouse uses **Client**. **Stallholder** may remain where it is established office/report terminology.
- **RATIONALE:** The same person label should not erase materially different operational relationships across facilities.
- **EVIDENCE / SOURCE:** Current Vendor Registry, TPM/TRM/SLH workflows and latest EEMO terminology clarification.
- **IMPACT:** Guides navigation labels, search/filter wording, account pages, facility headers, reports, and Mobile prompts without changing backend entity names.
- **REVISIT CONDITION:** Revisit only if EEMO later supplies a different official term for a specific operation.

### IA-011 — Collection Activity replaces generic workspace-level Transactions

- **ID:** IA-011
- **SUBJECT:** Cross-source activity-feed label
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Use `Collection Activity` for the global feed of recorded collections. Retain Transaction for provider, source-domain, or technical records where that distinction matters.
- **RATIONALE:** The current Transactions page describes recorded collections across facilities; the generic label obscures its purpose.
- **EVIDENCE / SOURCE:** Current `Menus/Transactions.razor` title/subtitle and feed columns.
- **IMPACT:** Target route is `/collections/activity`; `/transactions` remains compatible.
- **REVISIT CONDITION:** Revisit if the feed later intentionally includes non-collection operational transactions and its scope is explicitly redesigned.

### IA-012 — Administration hierarchy

- **ID:** IA-012
- **SUBJECT:** Settings and administration ownership
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Administration separates Business Configuration, People & Access, Office Setup, and System Administration. Personal security belongs to the account menu.
- **RATIONALE:** Configuration frequency, authority, and risk differ materially across these areas.
- **EVIDENCE / SOURCE:** Current Settings, Facility Configuration, Revenue Setup, Accounts, Collectors, Office Profile, Audit, and Backups audit.
- **IMPACT:** `/settings` becomes a compatibility entry; subject-specific canonical routes are introduced additively.
- **REVISIT CONDITION:** Revisit individual page placement if its actual responsibility changes; retain the four-part boundary.

### IA-013 — Head/Admin role visibility

- **ID:** IA-013
- **SUBJECT:** Navigation visibility versus authorization
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Hide unusable Head-only global destinations from Admin. Preserve current direct-route and endpoint authorization. Explain authority at a protected action boundary when an accessible page contains that action.
- **RATIONALE:** Locked global rows add noise and do not replace security enforcement.
- **EVIDENCE / SOURCE:** Current Sidebar locked Collectors/Audit rows; role rules in [EEMO_BUSINESS_RULES.md](../business/EEMO_BUSINESS_RULES.md), section 4.
- **IMPACT:** Navigation component tests and direct-route authorization tests are both required.
- **REVISIT CONDITION:** Revisit visibility only if EEMO identifies a training need that cannot be met through help/contextual messaging.

### IA-014 — Mobile primary navigation

- **ID:** IA-014
- **SUBJECT:** Collector Mobile information architecture
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Mobile primary navigation is Collect, Activity, Summary, and Me. Web workspaces are not copied to Mobile.
- **RATIONALE:** Collector work is shallow, assigned, field-oriented, and interruption/offline tolerant.
- **EVIDENCE / SOURCE:** Current Mobile route and archetype audit; approved task-focused Mobile rule in [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), section 11.
- **IMPACT:** Current Menu/Records/Reports/Profile are migrated compositionally; specialized capture routes remain.
- **REVISIT CONDITION:** Revisit after collector usability validation or a materially new field responsibility.

### IA-015 — Global sync state and Sync Center

- **ID:** IA-015
- **SUBJECT:** Offline/pending operation discoverability
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Show persistent sync/connectivity state and provide Sync Center under Activity. Distinguish queued, syncing, failed/retryable, rejected/action-required, and storage-fault states.
- **RATIONALE:** Sync state affects whether money capture is safely recorded and must not be discoverable only through Records.
- **EVIDENCE / SOURCE:** Current `MobileSyncService`, pending-operation store, and `Record.razor` Pending Sync sheet.
- **IMPACT:** UI composition changes; idempotency and queue semantics remain unchanged.
- **REVISIT CONDITION:** Revisit presentation after field validation; state distinctions remain required.

### IA-016 — Additive route aliases

- **ID:** IA-016
- **SUBJECT:** Route migration compatibility
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Introduce target routes additively and preserve current routes as aliases or compatibility redirects. Do not casually rename authentication, token, activation, payment callback, or other external routes.
- **RATIONALE:** Current links, bookmarks, office material, and callbacks are production contracts.
- **EVIDENCE / SOURCE:** Current Web route inventory; [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), section 10.
- **IMPACT:** Every route phase requires old/new resolution and authorization tests.
- **REVISIT CONDITION:** A legacy route may retire only after every condition in the migration plan is met.

### IA-017 — Accountable Forms placement

- **ID:** IA-017
- **SUBJECT:** Future accountable-document workspace
- **STATUS:** FUTURE
- **TYPE:** FUTURE CAPABILITY
- **DECISION / QUESTION:** Reserve Accountable Forms for Documents, Official Receipts, Cash Tickets, Inventory & Custody, and Void/Replacement/Reconciliation. Keep the workspace hidden until functional.
- **RATIONALE:** The lifecycle spans collection, document identity, form inventory, custody, and immutable history and therefore needs a stable home.
- **EVIDENCE / SOURCE:** [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), target components and phases 3/6.
- **IMPACT:** Current OR fields remain in existing workflows; no empty navigation or implied inventory is allowed.
- **REVISIT CONDITION:** Expose only when the bounded AccountableDocument capability or later form inventory is authoritative and usable.

### IA-018 — Remittance future placement

- **ID:** IA-018
- **SUBJECT:** Remittance and reconciliation workspace
- **STATUS:** FUTURE
- **TYPE:** FUTURE CAPABILITY
- **DECISION / QUESTION:** Reserve Collections > Remittance & Reconciliation as a possible future location. This is placement only and does not authorize implementation.
- **RATIONALE:** Remittance follows collection and concerns money handoff/reconciliation, but it is not itself a collection.
- **EVIDENCE / SOURCE:** Current interface architecture ruling; retired remittance history in [IMPLEMENTATION_HISTORY.md](../planning/IMPLEMENTATION_HISTORY.md); prohibition against resurrecting the partial workflow in [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), section 4.
- **IMPACT:** No current navigation item or workflow is added.
- **REVISIT CONDITION:** Revisit only after renewed EEMO approval resolves IA-023 and defines a complete useful workflow.

### IA-019 — Delinquency threshold and follow-up severity

- **ID:** IA-019
- **SUBJECT:** Current delinquency and urgency behavior
- **STATUS:** CONFIRMED
- **TYPE:** RUNTIME FACT
- **DECISION / QUESTION:** Delinquent means at least one fully elapsed unpaid month. For active accounts, one to two elapsed unpaid months are lower-age delinquent and Normal/this-period follow-up; three or more are higher-age delinquent and Critical/immediate follow-up. Three months is not the delinquency threshold.
- **RATIONALE:** Financial state and operational urgency are separate dimensions.
- **EVIDENCE / SOURCE:** `DomainRules.DelinquentThresholdMonths = 1`; `FollowUpComposer.ImmediateSeverityAgeMonths = 3`; financial/follow-up tests; current explicit business ruling.
- **IMPACT:** Labels, filters, summaries, and reports must preserve the distinction.
- **REVISIT CONDITION:** Only an explicit later business ruling plus separately tested financial behavior change may alter it.

### IA-020 — Arrears qualification boundary

- **ID:** IA-020
- **SUBJECT:** Qualification of old/lapsed debt as Arrears
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED BUSINESS RULE
- **DECISION / QUESTION:** Active current accounts become Delinquent after one fully elapsed unpaid month. **Arrears** is reserved for old/lapsed-year stall debt that remains owed after the relevant occupancy/term has lapsed; it is not a month-count synonym for ordinary active-account delinquency.
- **RATIONALE:** The Head explicitly distinguished current unpaid-month delinquency from old/lapsed owed debt. This preserves Delinquent, follow-up severity, and Arrears as separate concepts.
- **EVIDENCE / SOURCE:** Direct EEMO Head clarification consolidated in [EEMO_OPERATIONAL_RULEBOOK.md](../business/EEMO_OPERATIONAL_RULEBOOK.md), sections 1 and 15; [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md).
- **IMPACT:** Active 1+ elapsed unpaid months remain Delinquent; do not relabel them Arrears. Old/lapsed owed stall debt may be presented as Arrears. When qualifying Arrears are paid, the cash reports under the dedicated Arrears revenue line while the originating facility/obligation remains linked for traceability.
- **REVISIT CONDITION:** Revisit only if EEMO changes the old/lapsed-debt rule or supplies a more specific formal qualification test.

### IA-021 — Current correction authority

- **ID:** IA-021
- **SUBJECT:** Authority for current operational corrections
- **STATUS:** CONFIRMED
- **TYPE:** RUNTIME FACT
- **DECISION / QUESTION:** Head and Admin may perform the existing operational corrections that the current application already permits, including allowed collection/status corrections, current OR-evidence encoding or replacement, occupancy lifecycle actions, inactive-record actions, and facility-wide closure management. A separate free-text correction reason is not currently required; audit history remains required.
- **RATIONALE:** These are existing office responsibilities and current operational capabilities. Navigation redesign must not narrow or expand them accidentally.
- **EVIDENCE / SOURCE:** Current Collection Manager, Follow-up, Closed Accounts and facility workflows; latest EEMO office clarification.
- **IMPACT:** Preserve current authorization and audit behavior while pages are reorganized. This ruling does **not** approve future AccountableDocument or Cash Ticket void/replacement semantics.
- **REVISIT CONDITION:** Revisit when a future accountable-form/document lifecycle defines stricter void, replacement, evidence, or approval rules.

### IA-022 — Online-payment operational ownership

- **ID:** IA-022
- **SUBJECT:** Daily ownership of Awaiting OR and payment exceptions
- **STATUS:** NEEDS EEMO INPUT
- **TYPE:** BUSINESS DECISION GATE
- **DECISION / QUESTION:** Confirm whether Head, Admin, or a specific office role owns Awaiting OR handling, payment exceptions, and operational reconciliation with provider status.
- **RATIONALE:** Current page authorization permits Head/Admin while provider setup is Head-oriented; navigation placement alone cannot define office responsibility.
- **EVIDENCE / SOURCE:** Current `OnlinePayments.razor`; role rules in [EEMO_BUSINESS_RULES.md](../business/EEMO_BUSINESS_RULES.md).
- **IMPACT:** Affects queue ownership messaging, notifications, filters, and escalation, not current authorization until approved.
- **REVISIT CONDITION:** Close with an EEMO operating procedure and role decision.

### IA-023 — Provider configuration ownership

- **ID:** IA-023
- **SUBJECT:** Online-payment provider credentials and webhook setup
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Place provider configuration under Administration > Business Configuration > Collection Channels and keep operational payments under Collections. Preserve the current Head-controlled authorization boundary.
- **RATIONALE:** Credentials/configuration and daily payment handling have different sensitivity and frequency.
- **EVIDENCE / SOURCE:** Current `OnlinePayments.razor` Head-of-Office setup language and existing authorization behavior; target Administration architecture.
- **IMPACT:** Later composition split must preserve secret handling and existing guards.
- **REVISIT CONDITION:** Revisit only if provider configuration authorization changes through an explicit security/business decision.

### IA-024 — Remittance ownership and semantics

- **ID:** IA-024
- **SUBJECT:** Any future remittance workflow
- **STATUS:** SUPERSEDED IN PART by IA-052 (the CT-exhaustion trigger and "no partial remittance" wording are withdrawn; remittance is a separate ledger)
- **TYPE:** BUSINESS DECISION GATE
- **DECISION / QUESTION:** Keep remittance out of the current product until a complete accountable workflow is implemented. One key operating rule is now confirmed: a Cash Ticket assignment is remitted/accounted for when its assigned batch/range has been fully consumed; normal partial remittance is not allowed. A simple `Remitted = Yes/No` flag is still insufficient. The future design must still define covered amount, date, accountable officer, recipient/acknowledgement, deposit/cashier context, reconciliation states, correction/void behavior, and any Treasury handoff that is actually in scope.
- **RATIONALE:** The prior partial workflow was built and retired because the office found no usable value in it. Current EEMO clarification establishes the CT-exhaustion trigger, while office accountable-form evidence shows that remittance remains an amount-and-accountability process, not merely a boolean collection status.
- **EVIDENCE / SOURCE:** [IMPLEMENTATION_HISTORY.md](../planning/IMPLEMENTATION_HISTORY.md), retired work record; [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), section 4; latest office accountable-form reference showing remittance/deposit and collection-versus-remittance fields.
- **IMPACT:** Blocks any visible Remittance workspace, collector-balance claim, or simplistic remitted checkbox. Collection reporting remains collection reporting.
- **REVISIT CONDITION:** Reopen only through a new EEMO business case that defines the complete process; previous removed endpoints/UI do not constitute approval.

### IA-025 — Official report and document set

- **ID:** IA-025
- **SUBJECT:** Which current outputs are official office documents
- **STATUS:** NEEDS EEMO INPUT
- **TYPE:** BUSINESS DECISION GATE
- **DECISION / QUESTION:** Preserve office-evidenced report structures, but confirm which outputs are formally official, their authoritative scope, and required signatories. Current candidates include Financial Summary, Monthly Collection Report, List of Stallholders, Slaughterhouse List, Collector Report of Collections, monthly stall-rental/occupant monitoring, Monthly Income/Market Operations, and accountable-form reports.
- **RATIONALE:** Operational analytics, working registers, and official documents require different stability, signatory, print, and retention expectations. StallTrack may modernize layout without discarding required business fields.
- **EVIDENCE / SOURCE:** Current report inventory and [EEMO_BUSINESS_RULES.md](../business/EEMO_BUSINESS_RULES.md), reporting section; latest office reference sheets for Monthly Rental of Stall Occupants, lessee/stall monitoring, Monthly Income/Market Operations, and accountable-form reporting.
- **IMPACT:** Constrains report consolidation, naming, print layouts, and retirement of duplicate entries. Report templates should support report date and configurable Prepared by / Verified by / signatory information where applicable.
- **REVISIT CONDITION:** Close with an EEMO-approved report register identifying official status, required signatories, and scope for each output.

### IA-026 — Facility names and codes

- **ID:** IA-026
- **SUBJECT:** Display of tenant-resolved names and standard codes
- **STATUS:** NEEDS EEMO INPUT
- **TYPE:** UX DECISION
- **DECISION / QUESTION:** Confirm when NPM, TPM, TRM, SLH, and other codes accompany tenant-resolved facility names on Web, Mobile, reports, and official documents.
- **RATIONALE:** Codes aid recognition but must not replace tenant-owned display names or leak Cantilan wording to another tenant.
- **EVIDENCE / SOURCE:** Tenant-resolution rule in [ARCHITECTURE_RULES.md](../architecture/ARCHITECTURE_RULES.md); current Web/Mobile headers.
- **IMPACT:** Affects facility switcher, breadcrumbs, mobile headers, report titles, and print documents.
- **REVISIT CONDITION:** Close after EEMO preference and cross-tenant naming behavior are validated.

### IA-027 — Revenue targets

- **ID:** IA-027
- **SUBJECT:** Annual target setup and attainment
- **STATUS:** FUTURE
- **TYPE:** FUTURE CAPABILITY
- **DECISION / QUESTION:** Reserve target setup under Administration > Business Configuration and read-only attainment under Reports > Management. Office Monthly Income evidence already uses **Annual Target**, monthly actuals, total/YTD, and percentage, so target reporting has real office precedent. The target's authoritative source, approval/governance, revision policy, period, and classification/facility scope remain unresolved.
- **RATIONALE:** Setup and analysis are separate; attainment is not Collection Efficiency. Existing report columns do not by themselves authorize unrestricted editing in StallTrack.
- **EVIDENCE / SOURCE:** [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), report model, phase 9, and decision gates; latest EEMO Monthly Income/Market Operations reference showing Annual Target, monthly actual columns, Total, and Percentage.
- **IMPACT:** Future target design should preserve revision history and support explicit classification with optional facility scope where approved. No target navigation, edit authority, or calculated attainment becomes current merely from the reference sheet.
- **REVISIT CONDITION:** Begin only after EEMO confirms where approved targets originate, who may set/revise them, the applicable period, and classification/facility scope.

### IA-028 — Final revenue-classification catalog

- **ID:** IA-028
- **SUBJECT:** Complete official semantic catalog and report coverage
- **STATUS:** NEEDS EEMO INPUT
- **TYPE:** BUSINESS DECISION GATE
- **DECISION / QUESTION:** Which remaining official classifications/codes and groupings complete the EEMO catalog?
- **RATIONALE:** Phase 1 contains confirmed classifications only; interface labels cannot invent missing semantic identities.
- **EVIDENCE / SOURCE:** [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), sections 4, 12, 13, and 14.
- **IMPACT:** Blocks complete classified cash reporting and some future collection choices. Revenue Setup remains configuration, not universal production authority.
- **REVISIT CONDITION:** Close incrementally as EEMO approves stable classifications and policies.

### IA-029 — WCF entry surfaces

- **ID:** IA-029
- **SUBJECT:** Future WCF recording on Web and Mobile
- **STATUS:** FUTURE
- **TYPE:** FUTURE CAPABILITY
- **DECISION / QUESTION:** WCF must eventually be recordable from both Web/Admin and Collector Mobile through one canonical backend collection flow and one financial source. Reports derive from that source.
- **RATIONALE:** Dual entry surfaces must not create duplicate financial records or manual report entries.
- **EVIDENCE / SOURCE:** [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), confirmed WCF target entry requirement.
- **IMPACT:** Future Mobile work must preserve offline/idempotent behavior; current interfaces must not imply WCF dual entry exists.
- **REVISIT CONDITION:** Implement only after the canonical backend flow, instrument policy, authorization, and offline behavior are approved.

### IA-030 — Transportation classifications and rates

- **ID:** IA-030
- **SUBJECT:** Future transport/parking configuration
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED BUSINESS RULE
- **DECISION / QUESTION:** Use the documented Ordinance No. 12-2021 vehicle-class schedule as the current Cantilan basis for V2 transport/parking planning: Public Utility Buses ₱30, Public Utility Baby Buses ₱30, Jeepneys ₱20, Vans ₱20, Multicabs ₱10, and Tricycles ₱5, with route/service context where applicable. Transportation/Parking uses Cash Ticket. Future configuration remains effective-dated and prospective.
- **RATIONALE:** The office reference and latest project ruling confirm that the schedule is the working basis; the remaining design problem is implementation, not rate discovery. Historical TRM trips do not reliably encode vehicle class and must not be retroactively reclassified or repriced.
- **EVIDENCE / SOURCE:** Ordinance No. 12-2021 office reference; direct current project/business ruling consolidated in [EEMO_OPERATIONAL_RULEBOOK.md](../business/EEMO_OPERATIONAL_RULEBOOK.md), section 10; [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md).
- **IMPACT:** Future setup belongs under Administration. Historical `TrmTrip.Fee` remains financial truth; new effective rates apply only to future applicable collections. The old driver/trip-heavy UI must not obscure that the office's primary concern is CT-based transport/parking collection.
- **REVISIT CONDITION:** Revisit only if EEMO supplies a superseding ordinance/schedule or explicitly changes the vehicle-class basis.

### IA-031 — AccountableDocument production authority

- **ID:** IA-031
- **SUBJECT:** Current versus future receipt/document authority
- **STATUS:** FUTURE
- **TYPE:** FUTURE CAPABILITY
- **DECISION / QUESTION:** `AccountableDocument` is target architecture and is not current production authority. Current module OR fields and `OrNumberRegistry` remain transition evidence/protection.
- **RATIONALE:** Navigation and wording must not imply immutable document lifecycle, inventory, or replacement history before the bounded pilot and cutover.
- **EVIDENCE / SOURCE:** [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), phases 3 and 13 current status.
- **IMPACT:** Account/collection detail may reserve future placement but cannot label current records as managed accountable documents.
- **REVISIT CONDITION:** Revisit after the pilot reconciles registry/legacy fields and becomes authoritative for an approved workflow.

### IA-032 — Cash Ticket inventory and custody

- **ID:** IA-032
- **SUBJECT:** Cash Ticket books, units, assignment, and reconciliation
- **STATUS:** FUTURE
- **TYPE:** FUTURE CAPABILITY
- **DECISION / QUESTION:** Cash Ticket inventory/custody is not implemented. Its future interface belongs under Accountable Forms after operating policy is approved.
- **RATIONALE:** A label or serial field is not inventory, custody, consumption, spoilage, or reconciliation.
- **EVIDENCE / SOURCE:** [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), phases 6 and 14 decision gates.
- **IMPACT:** Keep Accountable Forms hidden; do not fabricate historical CT units or assignment.
- **REVISIT CONDITION:** Revisit after detailed series/range, custody, spoilage/cancellation, and reconciliation policy is approved.

### IA-033 — Report scope and basis

- **ID:** IA-033
- **SUBJECT:** Required context for financial reports
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Every financial report states tenant, facility, occupancy scope where applicable, period, as-of date, money basis, and relevant activity/obligation/collection/document date basis.
- **RATIONALE:** Period, lifetime, assessment, cash, and document views may legitimately differ; users need enough context to compare like for like.
- **EVIDENCE / SOURCE:** [ARCHITECTURE_RULES.md](../architecture/ARCHITECTURE_RULES.md), money/reporting rules; [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), section 8.
- **IMPACT:** ReportShell and migration acceptance criteria must expose basis without changing calculations.
- **REVISIT CONDITION:** Add source-specific basis detail when needed; do not weaken the minimum context.

### IA-034 — Canonical account/detail route identity

- **ID:** IA-034
- **SUBJECT:** Replacement of stall-key and owner-name routes
- **STATUS:** BLOCKED
- **TYPE:** TECHNICAL CONSTRAINT
- **DECISION / QUESTION:** Do not replace `/profile/{FacilityId}/{StallKey}` or `/facility/slh/transaction/{OwnerName}` with canonical account/activity routes until stable IDs and compatible lookup behavior exist.
- **RATIONALE:** Stall keys and names are not durable account/record identities, but inventing a new route ID without backend support would create broken or ambiguous links.
- **EVIDENCE / SOURCE:** Current route definitions and account/occupancy model.
- **IMPACT:** Keep current contextual routes and add only safe aliases; later canonical route work may require API/query support under separate scope.
- **REVISIT CONDITION:** Revisit when stable account/occupancy and SLH activity identifiers are exposed to the client.

### IA-035 — Billing basis and payment cadence language

- **ID:** IA-035
- **SUBJECT:** Monthly obligations paid in installments
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Interface labels must distinguish billing basis from payment cadence. Multiple or daily collection installments do not turn a monthly obligation into daily billing.
- **RATIONALE:** The distinction affects obligations, outstanding balances, collection history, and reports.
- **EVIDENCE / SOURCE:** [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), monthly-obligation ruling; NPM specialization.
- **IMPACT:** Filters and reports must not classify a source from collection frequency or stored monthly amount.
- **REVISIT CONDITION:** Only a source-domain billing ruling may change the basis.

### IA-036 — First post-presentation slice

- **ID:** IA-036
- **SUBJECT:** Initial implementation boundary
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED UX DECISION
- **DECISION / QUESTION:** Begin with Web navigation vocabulary, role visibility, a small additive alias set, and one Facilities landing entry while preserving current page bodies.
- **RATIONALE:** It validates the target mental model with the smallest runtime surface and no financial change.
- **EVIDENCE / SOURCE:** Audit recommendation re-evaluated against UI-1/UI-2 sequencing in [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md) and formalized in the migration plan.
- **IMPACT:** Explicitly excludes reports, facility workflow rewrites, account redesign, Mobile, visual redesign, and all future financial capabilities.
- **REVISIT CONDITION:** Revisit only if a proposed slice expands beyond additive navigation/vocabulary/facility-entry work or requires page-body redesign, financial behavior change, or a future capability.

### IA-037 — Curated global sidebar and production visual preservation

- **ID:** IA-037
- **SUBJECT:** Global Web sidebar composition and V2 visual baseline
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED UX DECISION
- **DECISION / QUESTION:** The global sidebar is a curated set of real, high-frequency destinations rather than a literal rendering of every V2 capability domain. Preserve the proven production sidebar visual treatment by default. The current approved destinations are Overview, Operations, Collection Activity, Online Payments, Payors & Accounts, Monitoring, Reports, Collectors, Audit Trail, and Settings; Collectors/Audit retain current Head-only authority.
- **RATIONALE:** Capability ownership and global navigation answer different questions. A compact real-destination sidebar keeps daily office work fast while deeper V2 subdivisions remain contextual inside their owning workspace. V2 is not a visual-reset project.
- **EVIDENCE / SOURCE:** Current approved V2 task ruling; current production sidebar/code; [STALLTRACK_V2_MASTER_SPECIFICATION.md](../v2/STALLTRACK_V2_MASTER_SPECIFICATION.md), sections 3 and 8; [DESIGN_SYSTEM.md](../interface/DESIGN_SYSTEM.md), production UI preservation rule.
- **IMPACT:** Individual facilities, Collection Manager, Follow-up History, and Export Data are not required permanent global entries. Existing routes remain compatible. A navigation-content change does not authorize page-body redesign.
- **REVISIT CONDITION:** Revisit only when a new functional capability proves it needs permanent global access or office workflow evidence shows the curated list is insufficient.

### IA-038 — Business Payor identity independent of authentication

- **ID:** IA-038
- **SUBJECT:** Tenant-owned business identity for approved operational relationships
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Adopt a small, stable business Payor independent of authentication, governed by the twelve constraints in [ADR-001](ADR_001_BUSINESS_PAYOR_IDENTITY.md). `PayorUser` remains optional authentication/access identity; authoritative source relationships determine eligible obligations; posted payer evidence is frozen.
- **RATIONALE:** Office obligations must not depend on portal registration, and textual similarity cannot establish financial identity. Payor identifies who, without introducing assessment, balance, receivable, or settlement authority.
- **EVIDENCE / SOURCE:** Clint's explicit acceptance of MASTER Grill Me Q41 on 2026-09-26, including all twelve constraints, recorded in [ADR-001](ADR_001_BUSINESS_PAYOR_IDENTITY.md). This is a StallTrack engineering/domain decision, not an additional EEMO accounting policy.
- **IMPACT:** Approved target semantics only. Preserve permitted anonymous/one-off payer contexts, tenant isolation, historical evidence, and unresolved legacy identity. MASTER owns sequential implementation; N/O/P/Q remain paused candidate workstreams. No partial candidate work or financial cutover is approved by this decision.
- **REVISIT CONDITION:** Concrete source relationship or access requirements expose a conflict with these constraints; bring it to MASTER rather than infer identity or introduce another ledger.

### IA-039 — Per-source canonical settlement cutover

- **ID:** IA-039
- **SUBJECT:** Opening legacy settlement and one settlement authority after conversion
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Adopt [ADR-002](ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md): frozen evidenced opening settlement, explicit per-source Legacy/Canonical authority marker, canonical post-cutover collections/lines/allocations, and legacy paid/status fields maintained only as atomic compatibility projections.
- **RATIONALE:** Incremental migration must preserve historical evidence while preventing independent legacy and canonical settlement paths from changing the same balance.
- **EVIDENCE / SOURCE:** Clint's explicit MASTER Grill Me Q42 approval on 2026-09-26, including eight clarifications and the payment/reversal example, recorded in ADR-002.
- **IMPACT:** Every writer for a converted source, including Mobile, online and corrections, follows canonical posting. Retain original assessment, opening settlement, allocations and correction evidence. Opening settlement is not a new collection and must not inflate canonical cash reports/RCD/Activity. No source conversion or lane restart is authorized by this decision alone.
- **REVISIT CONDITION:** A concrete source cannot satisfy the single-authority or atomic-projection rule; escalate before conversion. The separately approved operational readiness gate is IA-040 / Q43.

### IA-040 — Controlled reconciliation gate before cutover

- **ID:** IA-040
- **SUBJECT:** Scoped reconciliation before freezing opening settlement and activating Canonical authority
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Apply the eight requirements in [ADR-002, Q43](ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md#controlled-reconciliation-gate--q43): mark an explicit scope Pending Cutover, quiesce its new legacy activity, drain/reconcile all in-flight channels and physical documents, freeze evidenced opening positions, then activate Canonical authority. Unready sources remain Legacy.
- **RATIONALE:** Server cumulative state can omit physically collected money still in device queues or in-flight processing. Snapshotting server state alone cannot establish a reliable opening boundary.
- **EVIDENCE / SOURCE:** Clint's explicit MASTER Grill Me Q43 approval on 2026-09-26, including eight clarifications and the WCF PHP 800 assessment / PHP 300 reconciled opening settled / PHP 500 outstanding example.
- **IMPACT:** Late old submissions become preserved reconciliation exceptions, retaining ClientOperationId/document identity; no automatic legacy write, opening-snapshot change, or conversion of cumulative values into receipts. Issued OR/CT units stay consumed. Unrelated sources continue normally. MASTER coordinates enforcement and evidence sequentially; no actual cutover is authorized by this decision alone.
- **REVISIT CONDITION:** A proposed cutover cannot account for affected offline, physical-document, Web, online or retry/idempotency activity. Defer that scope rather than weaken the gate.

### IA-041 — Durable posting operation identity

- **ID:** IA-041
- **SUBJECT:** Tenant-scoped immutable posting intent and durable outcome
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Adopt the eleven requirements in [ADR-003](ADR_003_DURABLE_POSTING_OPERATIONS.md). TenantId + ClientOperationId binds one normalized semantic posting intent to its durable outcome. Equivalent retries return the existing authorized outcome; changed-intent reuse is an explicit IDEMPOTENCY CONFLICT; concurrent identical requests have one financial effect.
- **RATIONALE:** Mutable source-row keys and duplicate flags cannot preserve posting intent or durable retry history through installments, corrections and cutover.
- **EVIDENCE / SOURCE:** Clint's explicit MASTER Grill Me Q44 approval on 2026-09-26, including scope, normalization, atomicity, authorization, correction, document and failure semantics.
- **IMPACT:** MASTER owns the durable registry and atomic success transaction. Preserve original bindings after reversal, expose current disposition, distinguish infrastructure failure from durable business rejection, and never let a new key bypass document/source/reconciliation rules. Existing source keys are compatibility only once active. N/O/P/Q remain paused candidate workstreams.
- **REVISIT CONDITION:** A proposed workflow cannot preserve immutable intent, tenant-safe authorized replay or atomic financial success; resolve it before that workflow converts.

### IA-042 — Server-persisted versioned Web collection drafts

- **ID:** IA-042
- **SUBJECT:** Durable user-owned draft authority, revision-bound review and one successful posting
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Apply all ten requirements in [ADR-004](ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md). Stable tenant/user-owned DraftId, server persistence, expected-revision mutation checks, normalized financial review binding, final revalidation and atomic Posted/CollectionId linkage. One DraftId produces at most one successful Collection regardless of operation keys.
- **RATIONALE:** Circuit-local state and a review boolean cannot provide durable recovery or protect review integrity across tabs. Posting retry identity and draft identity solve separate problems.
- **EVIDENCE / SOURCE:** Clint's explicit MASTER Grill Me Q45 approval on 2026-09-26, including ten requirements and multi-tab/posting examples.
- **IMPACT:** Drafts create no revenue, allocation or document consumption and are excluded from financial Activity/RCD. Material changes require renewed review. MASTER owns draft persistence, contracts and consumers sequentially. Shared editing/handoff requires separate approval. Abandoned-draft retention is deferred. N/O/P/Q remain paused candidates.
- **REVISIT CONDITION:** A concrete workflow requires shared ownership or cannot satisfy atomic single posting/review integrity; return to MASTER before expanding the model.

### IA-043 — As-of and latest-corrected reporting

- **ID:** IA-043
- **SUBJECT:** Immutable events/corrections, recorded-knowledge cutoff and explicit reporting basis
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Adopt all eleven requirements in [ADR-005](ADR_005_CORRECTION_REPORTING_BASES.md). AsOf uses only events/corrections durably recorded by its cutoff; LatestCorrected follows all currently applicable corrections to period events. Preserve original events, distinct dates, correction relationships and explicit financial effects. Official cross-period RCD treatment remains pending Office confirmation.
- **RATIONALE:** Later or backdated corrections must not rewrite what the system knew earlier. A replacement physical document does not by itself represent another receipt of money.
- **EVIDENCE / SOURCE:** Clint's explicit MASTER Grill Me Q46 approval on 2026-09-26, including eleven requirements and the September 26 collection / September 27 reversal example.
- **IMPACT:** MASTER owns event/correction evidence and explicit period/cutoff/basis queries and drill-down, and labels legacy reconstruction limits. Reports remain derived. No automatic earlier-RCD restatement or later-period adjustment policy is approved; N/O/P/Q remain paused candidates.
- **REVISIT CONDITION:** Office confirmation determines official cross-period RCD presentation/accounting. Preserve both query capabilities and the evidence needed for the eventual policy.

### IA-044 — Governed configurable service operations

- **ID:** IA-044
- **SUBJECT:** Flexible but governed handling of locally defined EEMO services and Collector Mobile exposure
- **STATUS:** CONFIRMED
- **TYPE:** APPROVED ARCHITECTURE
- **DECISION / QUESTION:** Adopt [ADR-006](ADR_006_GOVERNED_CONFIGURABLE_SERVICE_OPERATIONS.md). A structurally simple local EEMO service may be represented by governed tenant configuration for classification, effective-dated instrument/rate/calculation policy, Payor requirement, operational fields, setup state and allowed collection channels. Incomplete setup cannot produce a financial Collection. Collectors record transaction facts only and never invent charge identity, rate, classification, or OR/CT policy.
- **RATIONALE:** StallTrack needs to accommodate local services such as Transfer Large Cattle and future EEMO revenue lines without hard-coding guessed rules or stopping the entire V2 design while office policy is being confirmed.
- **EVIDENCE / SOURCE:** Clint's explicit V2 architecture discussion and approval on 2026-09-27; existing Malinawa/configurable-service direction in [EEMO_OPERATIONAL_RULEBOOK.md](../business/EEMO_OPERATIONAL_RULEBOOK.md); canonical no-arbitrary-line, instrument-policy, Mobile-authority and collection rules.
- **IMPACT:** Operations may show **Setup Required** for incomplete services. Active services may feed the same canonical Collection infrastructure through Web and, only when explicitly enabled/authorized, focused Collector Mobile. IA-048 later confirms Transfer Large Cattle as an occasional transfer transaction with a corresponding direct approved amount; its exact Cantilan fee schedule/accountable-form and mandatory local regulatory detail remain configurable. A configurable service may later be promoted to a specialized source domain without rewriting posted history.
- **REVISIT CONDITION:** A service requires materially specialized approval, regulatory, assessment, lifecycle or reconciliation behavior beyond the governed configuration model; promote that source rather than stretching the generic model.
### IA-045 — EEMO Head correction: Tabo, Vegetable/Fruit, and Market grouping

- **ID:** IA-045
- **SUBJECT:** Latest Cantilan instrument and report-grouping clarification
- **STATUS:** CONFIRMED
- **TYPE:** BUSINESS DECISION
- **DECISION / QUESTION:** The EEMO Head confirmed **Tabo = Official Receipt (OR)**, superseding the earlier CT assumption. **Vegetable/Fruit Space Rental may use OR or CT, with CT the predominant practice.** For Market Fees/report grouping, use the office Monthly Income 2026 sheet: Market Fees is one sibling income row, while ECF, WCF, Tabo, Fish/Meat Vendor Fees, Landing/Berthing, Transportation Fees, Weight & Measure/Registration, Transfer Large Cattle, and Ice Plant are separate sibling rows.
- **RATIONALE:** The Head explicitly directed StallTrack to follow the office itemized Monthly Income sheet instead of inventing nested Market Fees subcategories.
- **EVIDENCE / SOURCE:** Direct EEMO Head clarification on 2026-09-27 plus the photographed Municipal Economic Enterprises Development Office Monthly Income 2026 sheet; recorded in [2026-09-27 EEMO Head clarification](../evidence/2026-09-27_eemo_head_monthly_income_clarification.md).
- **IMPACT:** Operations/UI and tenant instrument policy must stop presenting Tabo as CT. Vegetable/Fruit cannot remain CT-only in target design; IA-046 subsequently resolves full/whole payment = OR and daily transaction = CT. Tabo shadow/configuration must be re-verified before cutover. Market Fees remains one official report line rather than a container for the other listed market income rows.
- **REVISIT CONDITION:** Revisit only if EEMO supplies a newer official policy/report or clarifies a superseding instrument rule.

### IA-046 - Vegetable/Fruit OR-versus-CT selection condition

- **ID:** IA-046
- **SUBJECT:** Deterministic instrument resolution for dual-instrument Vegetable/Fruit Space Rental
- **STATUS:** CONFIRMED
- **TYPE:** BUSINESS DECISION
- **DECISION / QUESTION:** The EEMO Head confirmed the resolver on 2026-09-27: **full/whole ("buo") payment uses Official Receipt (OR); daily transactions/collections use Cash Tickets (CT).**
- **RATIONALE:** Vegetable/Fruit legitimately supports both instruments, but the choice is not arbitrary collector discretion. The collection cadence/context resolves the accountable document.
- **EVIDENCE / SOURCE:** Direct EEMO Head chat clarification recorded in [2026-09-27 EEMO Head final clarifications](../evidence/2026-09-27_eemo_head_final_clarifications.md).
- **IMPACT:** Target policy must support contextual instrument resolution. Remove CT-only presentation and remove the former unresolved decision gate. IA-047's regular/fixed-versus-transient/temporary inference is superseded for Cantilan.
- **REVISIT CONDITION:** Only if EEMO supplies a newer local rule that supersedes the full-payment/daily-transaction distinction.

### IA-047 - Interim Philippine-reference basis for presentation and UI audit

- **ID:** IA-047
- **SUBJECT:** Temporary evidence-based handling of unresolved Vegetable/Fruit instrument selection, utility assessment basis, and Transfer Large Cattle workflow
- **STATUS:** PARTIALLY SUPERSEDED / REFERENCE ONLY
- **TYPE:** PRESENTATION / UI-AUDIT REFERENCE - NOT PRODUCTION CUTOVER AUTHORITY
- **DECISION / QUESTION:** External Philippine references remain useful only where direct Cantilan policy is still absent. They no longer govern Vegetable/Fruit OR-versus-CT selection or the current ECF/WCF presentation because the EEMO Head has now directly clarified those items.
- **RATIONALE:** Direct Cantilan office clarification outranks comparative external-LGU precedent.
- **EVIDENCE / SOURCE:** [2026-09-27 EEMO Head final clarifications](../evidence/2026-09-27_eemo_head_final_clarifications.md) supersedes the applicable portions of [2026-09-27 Interim Philippine Reference Basis](../evidence/2026-09-27_interim_philippine_reference_basis.md).
- **IMPACT:** Keep IA-047 only as supporting reference for unresolved regulatory structure, especially optional Transfer Large Cattle ownership/animal/certificate details and other future comparative research. Do not use it to override current Head instructions.
- **REVISIT CONDITION:** Retire additional portions whenever Cantilan supplies direct local policy.

### IA-048 - EEMO utility operations are not NPM-owned; latest utility and transfer working rules

- **ID:** IA-048
- **SUBJECT:** Utility operation scope, current ECF/WCF basis, and latest Transfer Large Cattle office direction
- **STATUS:** CONFIRMED
- **TYPE:** BUSINESS + TARGET ARCHITECTURE CLARIFICATION
- **DECISION / QUESTION:** ECF and WCF are broader **EEMO Utility Operations**, not globally owned by the NPM facility. An NPM stall may be one service subject/context, but NPM must not be the architectural parent of every utility assessment. Current Head direction is **ECF = OR with direct approved amount entry** and **WCF = CT at the currently stated PHP 10 rate**. Transfer Large Cattle is an occasional transfer transaction with a corresponding **direct approved amount**.
- **RATIONALE:** The office Monthly Income/board material presents ECF and WCF as separate revenue lines, and earlier Head clarification already distinguished the broader EEMO/Public Market operation from NPM. The current code's UtilityBill is explicitly stall/NPM-bound and should therefore be treated as a legacy/current specialized source, not as proof that all future utilities are NPM-owned.
- **EVIDENCE / SOURCE:** Direct EEMO Head clarification on 2026-09-27, office Monthly Income/board evidence, and earlier confirmed Public Market-versus-NPM scope. See [2026-09-27 EEMO Head final clarifications](../evidence/2026-09-27_eemo_head_final_clarifications.md).
- **IMPACT:** Operations keeps a separate Utility Operations section. NPM may expose contextual utility links for its occupants but must not own global ECF/WCF navigation or reporting. Existing UtilityBill rows remain valid and must not be destructively rewritten; future non-NPM utility subjects should be handled additively through an appropriate generalized source/context model or adapter. Do not hard-code PHP 10 or direct ECF amounts into UI markup; resolve approved effective configuration/policy.
- **REVISIT CONDITION:** Revisit only if EEMO later restricts utilities to a specific facility or supplies a superseding utility-assessment/rate policy.

### IA-049 - Operational functionalization rulings (Clint / Core Brain, 2026-09-30)

- **ID:** IA-049
- **SUBJECT:** Instrument, basis, channel and receipt rules for the remaining Monthly Income operations, and the itemized OR model
- **STATUS:** CONFIRMED (Clint / Core Brain direction for the operational functionalization program; recorded from the task brief, not a new EEMO staff interview)
- **TYPE:** BUSINESS DECISION + IMPLEMENTATION DIRECTION
- **DECISION / QUESTION:**
  - **Itemized OR:** one physical OR may carry several OR-compatible classified lines for one payer context (for example NPM rent + ECF + Fish/Meat Vendor Fee + Weight & Measure + Penalty). Each line keeps its own revenue classification. **OR and CT are never mixed on one physical document**; a visit that also needs CT-based lines uses separate Cash Tickets.
  - **Billing basis is not payment cadence.** Monthly-obligation sources (NPM, TCC, NCC, BBQ, Ice Plant where configured, Fish/Meat Vendor Fee, Kanmanggay) keep a monthly obligation; ₱30/day style payments are installments/allocations against it and never a daily billing model.
  - **Fish/Meat Vendor Fee != NPM stall rent != Weight & Measure.** Vendor Fee is its own OR classification with a monthly-style goal (about PHP 900, commonly collected as PHP 30 installments) resolved from effective configuration. NPM remains the source authority for Fish/Meat vendor context; no second vendor registry.
  - **Weight & Measure** = OR, quantity x approved effective rate, frozen (quantity, rate, effective date, amount) at collection time for new rows. Historical Fish rows without frozen evidence stay unresolved.
  - **ECF** = OR, current basis direct approved amount. **WCF** = CT, current approved rate PHP 10 through effective configuration (not hard-coded).
  - **Vegetable / Fruit** (temporary open-space rental, not permanent NPM tenancy): the transaction mode resolves the instrument - whole payment = OR, daily transaction = CT; the collector chooses the mode, never the instrument.
  - **Market Fees** = CT, **Landing / Berthing** = CT, both direct field transactions on Collector Mobile using an approved fixed amount or, only where the approved operation definition enables it, a direct approved amount. **Transfer Large Cattle** = **OR** (supersedes the unresolved instrument in IA-044/IA-048 wording), Collector Mobile, occasional, direct approved amount, deliberately simple (no livestock registry).
  - **Transportation / TRM** target = CT at approved vehicle-class effective-dated rates; historical `TrmTrip` money and the existing legacy OR workflow stay readable and are not relabelled.
  - **Kanmanggay** = Space Rental, OR, monthly per space. **Fiesta / Araw lot rental** = OR, temporary/event lot rental; Aug 15 and Oct 16 are event dates, not billing dates.
  - **Fines / Penalties** = OR under their own classification, created only from approved penalty definitions; never a free-text line with a collector-typed amount.
  - **Ice Plant** behaves like a monthly-obligation source but keeps its own classification/report line; it is not BBQ or Stall Rent.
  - **Slaughterhouse** keeps its specialized workflow, OR, controlled rates and transparent itemization (ante mortem, post mortem, slaughter fee and approved add-ons on the OR detail); reports still roll up to Slaughterhouse. No arbitrary collector-entered rates.
  - **OR custody** must exist before any new Mobile OR writer is activated; a collector may use only documents assigned to that collector.
- **RATIONALE:** Removes the remaining "unresolved instrument" and "not recorded yet" wording once a writer is genuinely functional, while keeping every amount, instrument and classification server-resolved from approved policy.
- **EVIDENCE / SOURCE:** Clint / Core Brain task brief of 2026-09-30 (highest precedence for this program); IA-044, IA-045, IA-046, IA-048; [EEMO_OPERATIONAL_RULEBOOK.md](../business/EEMO_OPERATIONAL_RULEBOOK.md) sections 9-13.
- **IMPACT:** Supersedes the "collection disabled until the instrument is confirmed" statements for Transfer Large Cattle. Does **not** activate any source, cut over any report, backfill history or publish an APK. Where a rule above would create double billing or invent an amount (for example the Fish/Meat Vendor Fee versus the existing NPM Fish/Meat daily fee, or a collector-typed Slaughterhouse custom rate) the affected slice stays blocked pending an explicit answer, recorded in [OPERATIONAL_FUNCTIONALIZATION_V3_20260930.md](../planning/OPERATIONAL_FUNCTIONALIZATION_V3_20260930.md).
- **REVISIT CONDITION:** Any newer EEMO staff ruling on the operations above.

### IA-050 - Grill-me rulings for the operational functionalization pass (Clint / Core Brain, 2026-09-30)

- **ID:** IA-050
- **SUBJECT:** Resolution of the ten open policy questions left by IA-049
- **STATUS:** CONFIRMED (Clint / Core Brain direction; supersedes the "blocked pending an answer" wording in IA-049 IMPACT)
- **TYPE:** BUSINESS DECISION + IMPLEMENTATION DIRECTION
- **DECISION / QUESTION:**
  - **Fish/Meat Vendor Fee** is an additional, separate obligation from NPM Stall Rental and from Weight & Measure; one vendor may owe all three. Classification Fish/Meat Vendor Fees, OR, monthly goal, flexible cadence (PHP 30/day installments supported), effective-configured amount (not eternal constants), not part of BaseRentalAmount. **The existing NPM `DailyFee`/`DailyCollection` history is never reclassified as Vendor Fee**, and rows are not rewritten because amounts resemble PHP 30; a distinct Vendor Fee obligation/source is introduced prospectively.
  - **Ice Plant** = OR, own classification (ICE_PLANT), monthly obligation with effective-configured amount (about PHP 1,000 working figure), partial/daily installments and remaining balance allowed. Not Stall Rental. No ice inventory, bag/kg sales or manufacturing workflow.
  - **WCF** is not meter x rate. It is Cash Ticket + direct approved amount on Collector Mobile, the approved office amount (PHP 10 working figure) coming from effective policy, never hard-coded in Razor/Mobile. If the policy allows DirectApproved the collector may state the amount; classification and instrument stay server-controlled. No cubic-meter assumption. Historical UtilityBill/WCF evidence is preserved.
  - **ECF** = OR + direct approved amount; no meter or kWh x rate. It reuses the existing authoritative ECF/utility source (payor/source identity, period, approved amount, collected, balance, status, calculation basis, optional reference). It may share an itemized OR. Web and Mobile capture must converge on one source and never create duplicate assessments; collectors do not enter arbitrary ECF rates.
  - **Transportation / TRM**: target policy CT at approved vehicle-class effective-dated rates is confirmed, but **no cutover effective date is authorized**. Nothing may invent or backdate one; legacy TRM evidence stays intact; new CT architecture may exist inactive/shadow-ready. Status: TARGET CONFIRMED, CUTOVER DATE NOT AUTHORIZED.
  - **Kanmanggay** = Space Rental, monthly per space, OR, Business Payor identity, lightweight Space Rental account (payor, space identifier, active period, effective monthly approved rate, monthly obligations, settlement history). Not BBQ, not a permanent NPM stall/contract. Partial and installment settlement supported.
  - **Slaughterhouse**: collector-entered arbitrary CustomRate is not the target. Head/Admin manage approved definitions, packages and add-ons (effective-dated); the collector selects approved definitions only; receipt/detail stays itemized; Monthly Income still aggregates under Slaughterhouse. Historical CustomRate evidence is preserved.
  - **Fiesta / Araw** = Space Rental group, operation Lot Rental - Fiesta/Araw, OR, temporary event lot rental (event dates Aug 15 / Oct 16 are event dates, not billing dates). Head/Admin approve event, lot, amount, payor, period and availability; the collector never invents the lot amount; never a permanent stall tenancy.
  - **Collector totals** cover all real collections attributable to the collector from legacy or canonical sources, **counted exactly once** through explicit source coverage / cutover authority: legacy is authoritative before a source cutover, canonical after it, shadow/reconciled representations are comparison only. Applies to Collector Records, Collector Report of Collections, Collection Activity and Monthly Income.
  - **CB-06 Monthly Income cutover is NOT authorized.** The canonical reader remains shadow/readiness only until every line has coverage mapping, reconciliation is understood, no double counting remains, corrections are right, opening settlement is not current cash, unresolved history is safely handled, totals reconcile with the office's Monthly Income, and Core Brain/Clint explicitly authorize it.
  - Locked reminders: Market Fees/Landing/Transfer Large Cattle/Vegetable-Fruit/Fines as in IA-049; monthly sources (NPM, TCC, NCC, BBQ, Ice Plant, Fish/Meat Vendor Fee, Kanmanggay) keep a monthly billing basis regardless of installment cadence.
- **RATIONALE:** Removes the remaining policy gates so the sources can be built on server-resolved configuration without inventing amounts, instruments or classifications.
- **EVIDENCE / SOURCE:** Clint / Core Brain GRILL-ME ANSWERS of 2026-09-30.
- **IMPACT:** Only two gates remain: TRM cutover date and CB-06. A NEW contradiction that could change money, document identity, classification or historical meaning must be grilled again.
- **REVISIT CONDITION:** Any newer EEMO staff ruling; the TRM cutover date or CB-06 authorization.

### IA-051 - Final Transportation and Monthly Income go-live rulings (Clint / Core Brain, 2026-09-30)

- **ID:** IA-051
- **SUBJECT:** Transportation / TRM cutover and the Monthly Income (CB-06) cutover authority
- **STATUS:** CONFIRMED (Clint / Core Brain direction; supersedes the "cutover date not authorized" wording in IA-050 for TRM and CB-06)
- **TYPE:** BUSINESS DECISION + IMPLEMENTATION DIRECTION
- **DECISION / QUESTION:**
  - **Transportation / TRM** is a day-to-day **Cash Ticket** collection on Collector Mobile at an approved vehicle-class, effective-dated rate. There is no unresolved question about a historical OR-to-CT date. The technical cutover takes effect when the approved production release containing the canonical Transportation workflow goes live: before it, legacy TRM history is preserved unchanged; after it, all new Transportation collections use the canonical CT workflow. Old TRM rows are never rewritten, old OR fields never converted to CT, no old CT numbers invented, history never re-priced, and missing vehicle classes never inferred.
  - **Monthly Income cutover (CB-06)** is **authorized prospectively at the approved production go-live, source by source, with exactly-once coverage.** When the release ships and a source is activated, new financial events for that source use canonical Collection / CollectionLine / Allocation / AccountableDocument / PostingOperation and the Monthly Income and reporting paths use those rows. No arbitrary historical date is chosen and no old money is backfilled. Pre-go-live legacy stays the historical authority; post-go-live canonical is the authority for activated sources; a mixed period combines legacy pre-cutover and canonical post-cutover collections exactly once, and shadow or compatibility rows are comparison only and never add money.
  - The release itself establishes the real go-live boundary; this session does not deploy.
- **RATIONALE:** Removes the last two gates. Activation is per source (a row's settlement authority, or the Head enabling a new service), which is what makes a mixed period countable exactly once.
- **EVIDENCE / SOURCE:** Clint / Core Brain FINAL FUNCTIONALIZATION PASS direction of 2026-09-30.
- **IMPACT:** `CollectionSourceAuthorityMap` is the single decision of which representation is authoritative per source kind. The Head enabling the Transportation service is the Transportation boundary and closes the legacy trip writer from that date.
- **REVISIT CONDITION:** Any newer EEMO staff ruling.

### IA-052 - Accountable-form custody and cash remittance are separate ledgers (Clint / Core Brain, 2026-09-30)

- **ID:** IA-052
- **SUBJECT:** Cash Ticket remittance timing, the four ledgers, and remittance coverage
- **STATUS:** CONFIRMED (Clint / Core Brain direction; supersedes the CT-exhaustion trigger in IA-024, the Rulebook section 5 and the Revenue Architecture wording)
- **TYPE:** BUSINESS DECISION + IMPLEMENTATION DIRECTION
- **DECISION / QUESTION:**
  - Accountable-form inventory and cash remittance are related but distinct. A collector may keep unused forms while remitting the money collected with forms already issued (for example 10,000 assigned, 725 used, 9,275 on hand, PHP 20,000 collected and PHP 20,000 remitted). Remittance is **not** blocked by remaining stock. The eventual complete accountability of a batch (issued/consumed + spoiled/cancelled + returned + remaining + reconciliation exceptions) is tracked separately and never treated as a peso figure.
  - Four ledgers, never collapsed: (1) accountable-form ledger (physical stock and custody); (2) collection ledger (Collection/CollectionLine/Allocation/PostingOperation); (3) remittance ledger (money already collected and turned over); (4) reporting projections (RCD, Monthly Income, collector totals, targets, accountability).
  - **A remittance never creates revenue.** It covers whole, already-posted authoritative Collections attributable to the collector. A Collection is actively covered by at most one remittance (exactly once, database-enforced). The expected amount is derived from the covered collections and frozen at recording; the remitted amount is typed; a shortfall is a visible difference and needs review; an amount above the expected is refused (the office's 2026-08-25 answer). Source collections are never adjusted and no other collection is manufactured.
  - Head and Administrators record a remittance; a mistake is voided with a reason, which frees its collections. History is append-only. A retried request with the same operation id does not create a second remittance.
  - StallTrack records operational accountability only. It invents no Treasurer role, approval chain, bank-deposit workflow or general-ledger integration; a reference or acknowledgement number is optional evidence.
  - Legacy-authoritative collections (pre-cutover sources with no canonical Collection) cannot be covered exactly and are reported apart as outside structured remittance until their source goes canonical; nothing is manufactured for them.
  - Physical form states: In Office, Assigned, Issued/Consumed, Spoiled/Cancelled (a blank form, recorded with reason, actor and time, never revenue, never returned to stock), Returned (an unused assigned form back to office custody through an auditable event, never revenue), Needs Review. An issued/consumed serial never becomes available again.
- **RATIONALE:** Tickets are consumed per payer while cash is turned over on its own schedule; tying them made the process unusable and hid the real accountability.
- **EVIDENCE / SOURCE:** Clint / Core Brain ACCOUNTABILITY, REMITTANCE and FINANCIAL REPORTING program brief of 2026-09-30; IMPLEMENTATION_HISTORY.md (retired 2026-08-25 remittance, whose office answers still hold).
- **IMPACT:** Supersedes IA-024's exhaustion trigger. Remittance is buildable; no Treasury workflow is.
- **REVISIT CONDITION:** Any newer EEMO staff ruling on remittance or Treasury handoff.

### IA-053 - ECF and WCF are utility operations settled against a direct approved amount (Clint / Core Brain, 2026-10-01)

- **ID:** IA-053
- **SUBJECT:** NPM / utility ownership and the utility financial basis in the active V3 UI
- **STATUS:** CONFIRMED (Clint / Core Brain direction; applies the 2026-09-27 Head direction recorded in Rulebook Q2)
- **TYPE:** BUSINESS DECISION + IMPLEMENTATION DIRECTION
- **DECISION / QUESTION:**
  - ECF (Official Receipt) and WCF (Cash Ticket) are broader utility operations with their own revenue classifications. They are not components of NPM stall rent, vendor registration or the base rental; NPM is only the subject/context of a utility obligation.
  - The current Cantilan financial basis is a **direct approved amount** set by an authorized office workflow. Meter readings, consumption and per-unit rates are not required financial evidence for new workflows, and a collector never chooses the amount.
  - A stall's Electricity / Water service flags are non-financial context (they list the space in the utility register); they never enter the base rental or whole-year figure.
  - Historical UtilityBill readings and reading-based amounts are preserved unchanged as legacy evidence; they are never used to reprice a recorded charge. No backfill, conversion or source activation follows from this.
  - One authoritative financial path per source: NPM screens show related utilities read-only and link to the ECF / WCF workspaces; they are not a second utility writer.
- **RATIONALE:** The office settles utilities at approved amounts; showing meter billing as the active workflow contradicted that and made utilities look like part of rent.
- **EVIDENCE / SOURCE:** Clint / Core Brain NPM UTILITY DECOUPLING brief of 2026-10-01; Rulebook Q2 (2026-09-27 Head direction).
- **IMPACT:** Add Vendor, NPM Reports, ECF / WCF pages and the utility bill dialog present the approved-amount model; a new utility bill defaults to the direct approved basis.
- **REVISIT CONDITION:** A tenant that genuinely bills by meter (the basis remains configurable per bill).

## 4. Decision-gate summary

The following items require EEMO input, a UX decision, or a stated technical prerequisite before their affected capability can be finalized:

| ID | Gate | Blocks or constrains |
|---|---|---|
| IA-022 | Online-payment operational ownership | Queue ownership, escalation, and messaging |
| IA-024 | Exact remittance/RCD operating sequence | Any visible remittance capability |
| IA-025 | Official report/document set | Report consolidation and print authority |
| IA-026 | Facility name/code display | Headers, switchers, Mobile, and official documents |
| IA-027 | Target governance/period/revision | Revenue Target Setup and Attainment |
| IA-028 | Final classification catalog | Complete classified reporting and collection choices |
| IA-034 | Stable route identities | Canonical account and SLH activity detail routes |
| IA-043 | Official cross-period RCD correction treatment | Official revised-earlier-report versus later-period-adjustment behavior; technical AsOf/LatestCorrected queries are approved |

## 5. Superseded interpretations

The following interpretations must not be reintroduced:

- `1–2 months = Arrears` — **SUPERSEDED**. These active accounts are delinquent and lower-age/Normal follow-up.
- `3+ months = the delinquency threshold` — **SUPERSEDED**. Three months is a higher-age/Critical severity boundary.
- `ended occupancy balance = current occupant delinquency` — **SUPERSEDED**. It is a separate operational condition belonging to the past occupancy.
- `daily collection cadence = daily billing basis` — **SUPERSEDED** for monthly rental obligations.
- `facility = revenue classification` — **SUPERSEDED** as an information model.
- `current Revenue Setup = authoritative classified cash reporting` — **SUPERSEDED**. Configuration exists; production report cutover has not occurred.
- `OR field = AccountableDocument lifecycle` — **SUPERSEDED** as an architectural assumption.
- `retired collector remittance = approved future remittance design` — **SUPERSEDED**. Any future workflow requires renewed approval.
