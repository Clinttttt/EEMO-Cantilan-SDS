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
2. Baseline code, tests, and the revenue architecture agree that `DomainRules.DelinquentThresholdMonths = 1`: every account with at least one fully elapsed unpaid month is delinquent, Arrears remains unavailable until its old/lapsed qualification rule is resolved, and three months is only an urgency/severity boundary.

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
- **STATUS:** NEEDS EEMO INPUT
- **TYPE:** BUSINESS DECISION GATE
- **DECISION / QUESTION:** What exact age, occupancy status, or other criteria make old/lapsed debt qualify as Arrears?
- **RATIONALE:** Current age bands establish delinquency and urgency but do not establish Arrears classification.
- **EVIDENCE / SOURCE:** Current explicit ruling; [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), sections 4 and 14; current reports intentionally leave Arrears unset.
- **IMPACT:** Blocks Arrears status/classification UI and related report cutover. Use Outstanding Balance, Delinquent, and Ended-Occupancy Balance meanwhile.
- **REVISIT CONDITION:** Close only with an explicit EEMO qualification rule and separate decision on Monthly Income presentation of recovered qualifying Arrears.

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
- **STATUS:** NEEDS EEMO INPUT
- **TYPE:** BUSINESS DECISION GATE
- **DECISION / QUESTION:** Keep remittance out of the current product until EEMO approves a useful end-to-end workflow. A simple `Remitted = Yes/No` flag is explicitly insufficient. Any future design must define covered amount, date, accountable officer, recipient/acknowledgement, deposit or cashier context, reconciliation states, correction/void behavior, and whether treasury participation is in scope.
- **RATIONALE:** The prior partial workflow was built and retired because the office found no usable value in it. Office accountable-form evidence also shows that remittance is an amount-and-accountability process, not merely a boolean collection status.
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
- **STATUS:** NEEDS EEMO INPUT
- **TYPE:** BUSINESS DECISION GATE
- **DECISION / QUESTION:** Use Ordinance No. 12-2021 as documented office evidence for the terminal-fee model, but confirm that it remains the current, unamended schedule before production configuration. The visible schedule records Public Utility Buses ₱30, Public Utility Baby Buses ₱30, Jeepneys ₱20, Vans ₱20, Multicabs ₱10, and Tricycles ₱5, with route/service context on the ordinance. Future rates remain effective-dated and prospective.
- **RATIONALE:** The ordinance provides a concrete class/rate basis and explicitly describes Cash Ticket issuance, but historical TRM trips do not reliably encode vehicle class and must not be retroactively reclassified or repriced.
- **EVIDENCE / SOURCE:** Ordinance No. 12-2021 office reference; [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md), sections 4, 5, 7, and 14; TRM shadow reconciliation status.
- **IMPACT:** Future setup belongs under Administration and TRM work remains under Facilities. Historical `TrmTrip.Fee` remains financial truth; new effective rates apply only to future applicable trips. Cash Ticket remains the target instrument for Transportation/Parking under the confirmed Cantilan policy.
- **REVISIT CONDITION:** Close when EEMO confirms Ordinance No. 12-2021 is still current or supplies the superseding schedule and any required route/class changes.

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
- **STATUS:** PROPOSED
- **TYPE:** UX DECISION
- **DECISION / QUESTION:** Begin with Web navigation vocabulary, role visibility, a small additive alias set, and one Facilities landing entry while preserving current page bodies.
- **RATIONALE:** It validates the target mental model with the smallest runtime surface and no financial change.
- **EVIDENCE / SOURCE:** Audit recommendation re-evaluated against UI-1/UI-2 sequencing in [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md) and formalized in the migration plan.
- **IMPACT:** Explicitly excludes reports, facility workflow rewrites, account redesign, Mobile, visual redesign, and all future financial capabilities.
- **REVISIT CONDITION:** Confirm after the presentation and before opening the implementation task; reduce scope further if route or shell risk cannot be isolated.

## 4. Decision-gate summary

The following items require EEMO input, a UX decision, or a stated technical prerequisite before their affected capability can be finalized:

| ID | Gate | Blocks or constrains |
|---|---|---|
| IA-020 | Exact Arrears qualification boundary | Arrears status/classification and report cutover |
| IA-022 | Online-payment operational ownership | Queue ownership, escalation, and messaging |
| IA-024 | Whether and how remittance should exist | Any visible remittance capability |
| IA-025 | Official report/document set | Report consolidation and print authority |
| IA-026 | Facility name/code display | Headers, switchers, Mobile, and official documents |
| IA-027 | Target governance/period/revision | Revenue Target Setup and Attainment |
| IA-028 | Final classification catalog | Complete classified reporting and collection choices |
| IA-030 | Ordinance currentness / superseding transportation schedule | Production transportation setup and classified flow |
| IA-034 | Stable route identities | Canonical account and SLH activity detail routes |

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
