# StallTrack V2 Master Specification

**Status:** Canonical V2 orientation and change-control specification
**Historical clean-adoption baseline:** `396b7ee5f13c9887b896d8c6cb3380e9945d4679`
**Historical clean-adoption branch:** `interface-v2/clean-adoption`
**Current implementation status:** `../planning/CURRENT_RELEASE_STATE.md` plus current Git/code/tests. As of 2026-10-07, the accepted local integration checkpoint is `C:\dev\stalltrack\eemo` / `integration/report-governance-ui` at `6d3362f9`; this is newer than the deployed 2026-10-05 production release and is not itself a deployment declaration.
**Current business-direction overlay:** IA-067, IA-068, ADR-007, and `../planning/OFFICE_CLARIFICATION_REFACTOR_20261006.md` supersede older target assumptions about Terminal/Transportation, Fish/Meat identity, Business Payors, NPM Daily batch collection, and the completeness of Official Monthly Income.
**Applies to:** Web Office portal, Collector Mobile where stated, Payor portal where stated, and future V2 capability placement.

## 1. Purpose

This document is the first document to read when working on StallTrack V2. It gives one coherent map of what V2 means, what exists now, what is target architecture, what is future, and what must not be inferred.

It is derived from the canonical repository documentation and the current code/test baseline. It is not a replacement for the detailed business, architecture, security, testing, or decision documents.

When a detail here conflicts with a more authoritative current business ruling or accepted domain decision, follow the authority order in `docs/README.md`, surface the contradiction, and update the stale source rather than silently choosing one.

### Status language

- **CURRENT** — implemented and authoritative in the clean baseline.
- **TARGET V2** — approved direction, migrated incrementally.
- **FUTURE / HIDDEN** — placement is reserved but capability must not be exposed yet.
- **BLOCKED** — target cannot be completed until a stated prerequisite exists.
- **NEEDS MEEDO INPUT** — do not infer the answer in UI or code.
## 2. What StallTrack V2 is

StallTrack V2 is an incremental evolution of the existing production system. Its purpose is to make responsibilities, navigation, terminology, financial context, reporting, and future capability placement coherent without discarding working production behavior.

V2 primarily changes:

- information architecture and navigation;
- terminology and ownership of workflows;
- contextual placement of existing capabilities;
- consistency and accessibility;
- explicit financial/report scope;
- readiness for future collection, accountable-document, classification, and reporting capabilities.

V2 does **not** mean a full rewrite, a new generic design language, or consolidation of specialized facility domains into one financial engine.

## 3. Strict UI preservation rule

**The current/proven production UI is the default visual baseline.**

- Do not visually redesign an existing page, shell, card, table, or workflow merely because it is being migrated to V2.
- Adopt the existing production pattern first; add or adjust only what an approved V2 requirement actually needs.
- Route, navigation, vocabulary, or workspace migration is not permission to restyle the page body.
- Prefer additive, local, reversible changes over page-wide recomposition.
- Preserve StallTrack's established navy structure, restrained gold accents, neutral work surfaces, compact operational density, and specialized workflow patterns.
- Correct misleading semantics, accessibility defects, broken responsive behavior, inconsistency, or a UI that cannot express an approved capability with the smallest compatible change.

**V3 presentation authority:** [`docs/interface/STALLTRACK_UI_V3_DIRECTION.md`](../interface/STALLTRACK_UI_V3_DIRECTION.md), approved by Clint, now governs Web Office visual presentation. It supersedes the visual-preservation requirements in this section and in §8 and §17–18 (navy structure, old dark sidebar styling) for Web presentation only. It does not supersede business rules, MEEDO rulings, financial architecture, source authority, authorization, tenancy, runtime/application behavior, accessibility, reporting truth, or backend contracts, and it does not relax the behavioral preservation, small-slice delivery, or human localhost visual gate in this specification.
## 4. Historical clean-adoption production baseline

> This section describes the original V2 clean-adoption baseline. Several source-authority, V3 UI, remittance, WCF, governed-service and reporting changes have shipped since it was written. Use `../planning/CURRENT_RELEASE_STATE.md` for current implementation state.

The clean baseline is a multi-tenant LGU economic-enterprise revenue and operations system. Cantilan is the accuracy baseline; tenant-owned names, facilities, rates, users, branding, and data remain scoped per municipality.

Current code evidence includes these Web routes and aliases:

| Current capability | Current route evidence |
|---|---|
| Dashboard / Overview | `/menu`, `/overview` |
| Operations / Facilities landing | `/operations` |
| Collection Activity | `/transactions`, `/collections/activity` |
| Online Payments | `/online-payments` |
| Permanent spaces / occupants registry | `/vendors` |
| Follow-up Queue | `/reports/follow-up`, `/monitoring/follow-up` |
| Reports | `/reports` |
| Export Data | `/export` |
| Collectors | `/collectors` — SuperAdmin |
| Audit Trail | `/audit-trail` — SuperAdmin |
| Settings / Administration alias | `/settings`, `/admin` |

Current route aliases are compatibility evidence, not permission to delete legacy routes.
### Current source authority

Specialized production sources remain authoritative. Existing payment records, daily collections, NPM settlement, TPM attendance, TRM trips, utility bills, slaughter transactions, and online-payment lifecycle records keep their established write/read authority unless a separately approved cutover says otherwise.

`Collection` and `CollectionLine` are a foundation/shadow-reconciliation direction, not the universal production writer or report source.

Current OR fields and OR-number protection are transition evidence. They are **not** an authoritative `AccountableDocument` lifecycle.

## 5. Product mental model

V2 must keep these layers separate:

1. **Operation** — the real-world enterprise activity.
2. **Obligation** — what is owed, for which source and period.
3. **Collection** — money received on a business date and attributed to actor/source context.
4. **Accountable document** — OR/Cash Ticket identity and lifecycle when authoritative.
5. **Reporting and accountability** — read-only views of obligations, collections, documents, classifications, and targets with explicit basis.

The interface may connect these layers; it must not collapse their meanings.
## 6. Non-negotiable distinctions

- Facility is not Revenue Classification.
- Billing Basis is not Payment Cadence.
- Obligation is not Collection.
- Collection is not Remittance.
- Collection Efficiency is not Revenue Target Attainment.
- Collector Balance is not Customer Outstanding Balance.
- Delinquency is not Arrears.
- Delinquency is not Follow-up Severity.
- Space/Stall, Occupancy/Term, Payor, and login Account are different concepts.
- Current production authority is not the same as target architecture.

Money views compare like for like: tenant, facility, occupancy scope, period, as-of date, money basis, and relevant date basis.

Presentation code must not recreate domain arithmetic merely to make the UI look uniform.
## 7. Roles and authority

- **Platform Operator:** separate platform authority; cross-tenant reach requires the dedicated operator flag.
- **Head / SuperAdmin:** full permitted authority within the LGU, subject to peer-Head and platform-only restrictions.
- **Admin:** office operations and reports allowed by current authorization; no Head-only account/audit authority.
- **Collector:** Mobile-focused field collection within assigned/available facility rules.
- **Payor:** personal obligation/payment/history scope in the Payor portal.

Navigation visibility must match usable authority, but hiding a link never replaces endpoint authorization.

## 8. Global Web sidebar — current V2 decision

The sidebar is a curated list of real, high-frequency destinations. It is **not** required to mirror every domain layer or every subdivision of the target architecture.

Use the established old/production sidebar visual treatment and replace/adopt content only as needed.
### Target global destinations

The 2026-10-06 office clarification retires the planned global **Payors & Accounts / Business Payors** workspace as a collection prerequisite. Source-owned registries stay with their owning operations; unified New Collection searches across them.

1. **Overview** → `/overview`
2. **Operations** → `/operations`
3. **Collection Activity** → `/collections/activity`
4. **Online Payments** → `/online-payments`
5. **Spaces & Occupants** → current permanent-space registry route `/vendors`
6. **Monitoring** → current implementation may land on `/monitoring/follow-up` until a true landing workspace exists
7. **Accountable Forms** → `/accountable-forms`, Head/Admin
8. **Reports** → `/reports`
9. **Collectors** → `/collectors`, Head-only
10. **Audit Trail** → `/audit-trail`, Head-only
11. **Settings** → `/settings`

Fish/Meat vendors and other source-specific identities are **not** replacement global payor pages. They belong under the operation that owns the record and are discoverable through source-native collection search.

A subtle visual divider may separate the last three administrative/system destinations; they remain actual navigations, not category labels.

### Not permanent global entries

- individual facilities — enter through Operations;
- Collection Manager — contextual under collection/monitoring work;
- Follow-up History — contextual under Monitoring;
- Export Data — contextual under Operations/Reports once ownership is clear.

Keep legacy routes available while navigation evolves.
## 9. Workspace responsibilities

### Overview

Purpose: concise portfolio orientation and attention. Preserve the proven production dashboard visual structure by default.

Owns high-level collection position, facility summaries, recent activity, and attention summaries. It does not become the full Reports library, configuration directory, or a dumping ground for future V2 capability.

### Operations

Purpose: facility discovery and source-domain work.

The Facilities landing uses the tenant-scoped configured facility catalog. Selecting a facility enters the specialized facility workflow; V2 structure must not erase NPM, monthly rental, SLH, TPM, utility, or custom-facility behavior. Legacy TRM data/routes remain compatibility evidence, but the target model now separates **Income From Terminal** from **Transportation/Parking**.

Operations may also list non-facility source operations and governed configurable services. A known but incomplete operation may appear as **Setup Required** for authorized Web users; that state is informational/configurational and cannot create money until all required approved policy is complete.

Transfer Large Cattle is now understood as an occasional transfer transaction with a corresponding fee/amount. The Head prefers direct approved amount input. Keep the amount policy configurable and preserve the governed-service boundary; Philippine regulatory references may guide optional transfer/ownership/certificate details and OR-oriented presentation until Cantilan supplies a different local instrument/form rule.

Latest confirmed office direction:

- **Tabo uses OR**.
- **Vegetable/Fruit Space Rental** uses OR for full/whole payment and CT for daily transactions.
- **ECF** uses OR with direct approved amount entry; **WCF** uses CT and follows its current direct/prepared workflow.
- ECF/WCF are broader MEEDO Utility Operations rather than NPM-owned revenue types.
- **Transportation/Parking** is a separate CT direct-amount source with no required vehicle-class/rate basis.
- **Income From Terminal** is a separate official operation/report family: Comfort Room; Pull Pul Vans, Cargo Vans; Tricycad. All are CT and allow direct aggregate amount entry; vehicle-class/rate configuration belongs prospectively to Terminal.
- **Fish/Meat Vendor Fee** is independent from NPM and from a mandatory Business Payor. It uses an independent Fish/Meat vendor registry; Weight & Measure requires a registered vendor from that registry.

The office Monthly Income sheet remains the report-grouping authority. The 2026-10-06 continuation confirms A. Income From Market, B. Income From Terminal, C. Income from Slaughterhouse, and the overall total/signatories.

See [2026-10-06 MEEDO office clarification](../evidence/2026-10-06_meedo_office_terminal_fish_vendor_monthly_income_clarification.md), IA-067, IA-068, and ADR-007.
Do not fabricate Cash Ticket, OR, Accountable Forms, classification, or export workflows on the landing page merely because future architecture reserves them.
### Collections

Purpose: recorded collection activity and currently approved collection operations.

`/collections/activity` is the canonical activity entry while `/transactions` remains a compatibility route.

Online Payments remains a real operational destination. Provider configuration belongs with administrative configuration, not the daily payment queue.

Mutable correction/status work must not be mislabeled as read-only reporting merely because its current component lives under a reports folder/route.

### Source records and registries

There is no longer a target global **Payors & Accounts / Business Payors** workspace.

**CURRENT:** `/vendors` remains the permanent-space / occupant registry surface.

**TARGET:** each business source owns the identity/relationship record it actually needs:

- permanent spaces → stall/occupancy records;
- monthly/space rental → renter/occupancy/account records;
- Fish/Meat → independent vendor registration;
- utilities → utility/source account;
- other operations → their own approved source records.

Unified New Collection performs **cross-source search** over these records and returns typed source identity/context. Equal names are never auto-merged. A free-text payer snapshot is allowed only for operations whose server policy explicitly permits a one-off/direct payer context.

See [ADR-007](../decisions/ADR_007_SOURCE_NATIVE_COLLECTION_IDENTITY.md).
### Monitoring

Purpose: operational attention, not financial classification.

Current Follow-up Queue is the practical entry. Target Monitoring also owns ended-occupancy balances, exceptions, expiring/expired occupancies, and follow-up history as those surfaces are coherently migrated.

Rules:

- delinquent begins after one fully elapsed unpaid month;
- 1–2 months can be lower/Normal urgency;
- 3+ months can be Critical urgency;
- Critical does not mean Arrears;
- current-period unpaid does not automatically mean delinquent;
- ended-occupancy balance is separate from current-occupant delinquency.

Arrears is reserved for old/lapsed-year stall debt that remains owed after the relevant occupancy/term has lapsed. Do not use ordinary active-account month age alone to relabel delinquency as Arrears. When qualifying Arrears are paid, the collected cash reports under the dedicated Arrears revenue line while retaining the originating facility/obligation reference.
### Reports

Reports is the central **read-only and printable output area**, not merely analytics.

It may contain:

- official/office-ready printable reports;
- financial position and receivables views;
- monthly collection reports;
- collection registers;
- collector/facility reports;
- stallholder/occupancy lists where appropriate;
- management summaries and trends;
- future classified cash and target reports only when authoritative.

Every financial report must state enough scope/basis to compare like for like.

Existing report sources remain authoritative until separately approved cutover. Printable office outputs must remain recognizable and complete; a visual modernization must not remove required fields, signatory context, or business evidence.
### Settings

The global destination is **Settings**.

Inside it, V2 may progressively organize existing administration under:

- Business Configuration;
- People & Access;
- Office Setup;
- System Administration.

This internal organization does not require the sidebar label "Administration".

Collectors and Audit Trail remain separate real destinations where useful; both retain current Head/SuperAdmin authorization.

Settings placement never changes MFA, authentication, backup/restore, platform-operator, facility-rate, tenant, or security authority.
## 10. Facilities and specialized business models

Canonical facility codes plus configured custom facilities remain tenant-aware.

| Code | Reference operation | Current model |
|---|---|---|
| NPM | New Public Market | specialized stall collection; explicit NPM month basis governs obligation |
| TCC | Tampak Commercial Center | monthly rental |
| NCC | New Commercial Center | monthly rental |
| BBQ | Barbecue Stand | monthly space rental |
| ICE | Iceplant | monthly space rental |
| SLH | Slaughterhouse | per-head/service transaction |
| TRM | Legacy Transport Terminal compatibility code | historical per-trip/transport evidence; target Terminal is a separate official operation family, while Transportation/Parking is an independent direct-amount CT source |
| TPM | Tabo-an Public Market | per-vendor market-day/weekly operation |

Do not infer one shared billing model from similar UI. Facility-specific writers, rates, dates, participants, and reporting semantics remain source-owned.

Configured custom facilities must use tenant-resolved names/rates; do not hardcode a Cantilan-only catalog when the tenant source is unavailable.
## 11. NPM billing rule

NPM follows the explicit `NpmMonthBasis`.

- **RentGoal:** daily collections settle against the tenant's fixed monthly obligation and may require month-end top-up.
- **PureDays:** no fixed monthly rent; obligation is resolved daily fee × chargeable days, with no top-up.

Both use the shared month rule/ledger. Do not infer basis from a stored monthly amount and do not reproduce settlement arithmetic in presentation code.

A daily collection cadence does not itself mean daily billing basis.

## 12. Accountable documents, OR, and Cash Ticket

### CURRENT

- module/source OR fields and OR uniqueness protection exist;
- current collection flows may carry receipt evidence according to existing source behavior.

### FUTURE / HIDDEN

- authoritative `AccountableDocument` production lifecycle;
- Official Receipt document lifecycle;
- Cash Ticket document lifecycle;
- Cash Ticket inventory, custody, consumption, spoilage/cancellation, replacement, and reconciliation.
Accountable Forms has a reserved target position but remains hidden until a functional, authorized capability exists.

Do not equate an OR field with an accountable-document lifecycle. Do not create Cash Ticket navigation, inventory cards, serial workflows, or custody claims before the operating policy and authoritative implementation exist.

Office evidence may establish that a classification commonly uses OR or Cash Ticket; that evidence does not by itself implement document inventory/custody.

## 13. Revenue classification and targets

Revenue Classification is separate from Facility.

Future classified cash reporting must map collection lines to approved classification identities rather than guessing from facility names.

Office Monthly Income uses Annual Target, monthly actuals, Total/YTD, and Percentage.

IA-066 now establishes bounded target governance:

- tenant + calendar year + official row;
- externally approved amount;
- Head-only configuration;
- source/reference evidence;
- immutable audited revisions;
- no inferred growth formula.

Head-only official report adjustments may revise the reported cell through an audited delta/revision without editing Collections, source obligations, remittance or collector position.

Revenue Target Attainment remains separate from Collection Efficiency.
## 14. Reports and official-office evidence

Current documentation identifies candidate office outputs including Financial Summary, Monthly Collection Report, List of Stallholders, Slaughterhouse List, Collector Report of Collections, rental/occupant monitoring, Monthly Income/Market Operations, and accountable-form reporting.

The complete **Official Monthly Income** structure is now confirmed by office evidence: A. Income From Market, B. Income From Terminal, C. Income from Slaughterhouse, OVERALL TOTAL MARKET COLLECTION, Prepared by, and Certified Correct.

For Monthly Income:

- Prepared by / Certified Correct name + position are configurable Office Settings;
- report adjustments are Head-only and audited;
- the complete report may span multiple print pages rather than being forced into one unreadable page.

Other office outputs may still require separate confirmation of statutory/official status, retention and final signatory rules.

Until separately confirmed for those other outputs:

- preserve office-evidenced fields and recognizable print structures;
- do not retire a report merely because another page looks similar;
- distinguish operational registers from official documents and management analysis.

Export actions should become contextual to their owning operation/report rather than a permanent global sidebar destination once ownership is clear.
## 15. Collector Mobile

Mobile remains task-focused and specialized.

Target direction: Collect, Activity, Summary, and Me, with clear sync/connectivity state and resume behavior.

The 2026-10-06 Mobile target is **source-native and report-family aware**:

- organize collection work around Income From Market, Rent Income, Space Rental, Income From Terminal, and Income From Slaughterhouse;
- New Collection searches source-owned identities instead of a Business Payor master;
- after selecting a source, show only server-confirmed eligible operations;
- Fish/Meat vendors may expose Vendor Fee + Weight & Measure when registered;
- Transportation/Parking is direct amount; Terminal owns the vehicle-class-assisted path;
- NPM Daily may offer Collect All for today's daily charge only, with unchecked exceptions remaining unpaid and one Collection/SRC per selected stall.

Do not copy the Web sidebar into Mobile.

Preserve:

- specialized facility capture flows;
- assignment/archetype routing;
- `ClientOperationId` idempotency;
- offline queue persistence/retry;
- server-issued business date as primary;
- signed release/version coordination.

For governed configurable services, Mobile remains transaction-only. It may render an operation only when the service is Active, explicitly Mobile-enabled, authorized/assigned to the collector, and compatible with accountable-document custody. The collector supplies transaction facts; classification, instrument, rate/calculation policy, and document requirements come from the specialized source or approved tenant configuration.

Do not expose Setup Required services as collectible actions to ordinary collectors. Head/Admin Web may still see their setup state. Mobile must never provide a generic form for inventing a charge name, arbitrary amount/rate, classification, or OR/CT choice.

No AccountableDocument, Cash Ticket custody, remittance, WCF, vehicle-class, or configurable-service collection workflow is implied merely by a navigation change. Activation still requires its approved backend authority and rollout gate. See [ADR-006](../decisions/ADR_006_GOVERNED_CONFIGURABLE_SERVICE_OPERATIONS.md).

### 2026-10-06 current-direction overlay

The historical matrix below is intentionally preserved, but these old target rows are now superseded:

- **Broader Payors & Accounts landing/detail** → retired as a target; use source-owned registries and source-native search (ADR-007 / IA-068).
- **Transportation class/rate redesign** → vehicle classes/rates move to Terminal; Transportation/Parking is direct amount (IA-067).
- **Revenue target setup/attainment** → bounded Head-only annual target governance is confirmed under IA-066.
- **Official Monthly Income structure** → A. Market, B. Terminal, C. Slaughterhouse, overall total and configurable signatories are confirmed (IA-067).
- **NPM Daily batch collection** → Collect All is approved for today's daily charge only, with one Collection/SRC per selected stall (IA-068).

## 16. Historical capability status matrix

> This matrix is retained as a clean-adoption-era status snapshot. It is **not** the current implementation-status authority. Use [`docs/planning/CURRENT_RELEASE_STATE.md`](../planning/CURRENT_RELEASE_STATE.md), current Git/code/tests, and later Decision Registry entries before acting on any row below. `STALLTRACK_V2_PHASE_STATUS.md` remains phase history.

| Capability | Status |
|---|---|
| Existing specialized facility collection flows | CURRENT |
| Dashboard / Overview | CURRENT; visual baseline preserved |
| Facilities landing `/operations` | CURRENT; refine additively |
| Collection Activity alias | CURRENT |
| Online Payments | CURRENT |
| Spaces & Occupants registry | CURRENT |
| Monitoring Follow-up entry | CURRENT |
| Reports and printable/export outputs | CURRENT; organization evolves |
| Settings / admin alias | CURRENT |
| Collectors / Audit Trail Head-only | CURRENT |
| Broader Payors & Accounts landing/detail | TARGET V2 |
| Monitoring full landing/families | TARGET V2 |
| Context-owned export placement | TARGET V2 |
| Accountable Forms global workspace | Cash Ticket inventory/custody slice RELEASED (2026-09-30); OR custody, remittance, void/replacement and RCD remain FUTURE / HIDDEN |
| AccountableDocument authority | FUTURE / HIDDEN |
| Cash Ticket inventory/custody | TARGET V2 backend foundation implemented in Phase 4; UI/release remains gated |
| Revenue classification cutover | FUTURE / gated |
| Revenue target setup/attainment | FUTURE / gated |
| Remittance & Reconciliation | FUTURE; renewed approval required |
| WCF entry surface | Collector Mobile only (Web WCF posting is retired server-side; Web is monitoring/reconciliation). Supersedes the dual-entry target; production source remains Legacy / gated on the scoped Water cutover (IA-029 wording is stale, IA-049). |
| Governed configurable service operations | TARGET V2; IA-044. Setup Required is non-financial; Active services use approved policy and explicitly enabled channels. |
| Transfer Large Cattle financial setup | TARGET V2; **OR**, Collector Mobile, occasional, direct approved amount (IA-049). Exact Cantilan fee/ceiling and any local attestations stay configurable; non-collectible until an amount rule is recorded. |
| Transportation class/rate redesign | TARGET V2; Cantilan vehicle-class schedule confirmed for current planning |
| Canonical stable account/activity routes | BLOCKED on stable IDs |
| Arrears qualification and recovered-cash mapping | CONFIRMED; old/lapsed debt and paid Arrears use the dedicated Arrears classification with source traceability |
| Official report/document register | NEEDS MEEDO INPUT |

## 17. Historical clean-adoption migration strategy

This section preserves the original V2 adoption strategy. The repository has since moved through the V3 presentation and backend closure work; do not interpret the old branch/slice sequence below as the current work queue. Use `../planning/CURRENT_RELEASE_STATE.md` for the current queue.

The clean V2 branch starts from the known production-compatible baseline. Do **not** wholesale merge today's experimental presentation branches.

Adopt V2 in small slices:

1. documentation and guardrails;
2. sidebar destinations/content while preserving old sidebar visual design;
3. Operations/Facilities landing refinement using existing production language;
4. route aliases, vocabulary, and contextual relocation;
5. page-level changes only where a V2 requirement actually demands them;
6. future financial/document capabilities only after business authority and backend source are approved.

Every slice must be independently reversible and should avoid combining structural UI migration with financial behavior changes.
### Required human visual gate

For UI slices:

1. implement the smallest slice;
2. build and run focused tests;
3. run the affected Client locally against a trusted local API configuration;
4. Clint performs the localhost visual/workflow check;
5. fix only demonstrated issues;
6. integrate/publish only after approval.

Do not substitute a production deployment for this local visual gate.

## 18. Current sidebar adoption slice

The next clean-client sidebar implementation should:

- keep the old production sidebar styling and interaction model;
- remove permanent facility rows from global navigation once Operations reliably reaches all configured facilities;
- expose the current global destinations defined in section 8;
- keep Collectors/Audit authority unchanged;
- keep compatibility routes;
- avoid new category-heavy visual hierarchy unless explicitly requested.

This is a content/ownership migration, not a visual redesign.
## 19. Decision gates that must not be guessed

At minimum, consult `docs/decisions/DECISION_REGISTRY.md` before work involving:

- IA-022 — online-payment operational ownership;
- IA-024 — exact remittance/RCD operating sequence;
- IA-025 — official report/document set beyond the now-confirmed Official Monthly Income structure;
- IA-026 — facility name/code display policy;
- IA-027 — only target models beyond the bounded Head-only annual-target governance already resolved by IA-066;
- IA-028 — final revenue-classification catalog and any mappings still unresolved after IA-067/IA-068;
- IA-034 — canonical account/detail route identity.

A UI label cannot close a business decision gate.

## 20. Validation and release rules

- Preserve tenant isolation and current authorization.
- Do not move financial calculations into presentation code.
- Run unit and component suites separately where repository guidance requires it.
- Financial/reporting/tenancy/auth changes require focused failing-before-fix evidence.
- Scoped CSS must remain brace-balanced.
- Mobile changes require signed release/version coordination.
- A push to `master` triggers the established production release path; verify production afterward.
## 21. Canonical source map

Read this master specification first, then follow the detailed source for the task:

- `docs/README.md` — authority, documentation map, conflict handling.
- `docs/planning/CURRENT_RELEASE_STATE.md` — current implementation/release checkpoint; use before dated audits or phase snapshots.
- `docs/planning/OFFICE_CLARIFICATION_REFACTOR_20261006.md` — current implementation plan for the Terminal/Transportation split, independent Fish/Meat registry, source-native New Collection, NPM Daily Collect All, Business Payor retirement and complete Monthly Income.
- `docs/evidence/2026-10-06_meedo_office_terminal_fish_vendor_monthly_income_clarification.md` — direct office authority for those changes.
- `docs/business/EEMO_OPERATIONAL_RULEBOOK.md` — direct MEEDO Head/staff rulings, office-evidence reconciliation, confirmed workflow rules, and the current high-value open questions.
- `docs/business/EEMO_BUSINESS_RULES.md` — current accepted business semantics.
- `docs/business/REVENUE_ARCHITECTURE.md` — approved target revenue/document architecture and migration phases.
- `docs/interface/INFORMATION_ARCHITECTURE.md` — detailed target interface/workspace model.
- `docs/interface/MIGRATION_PLAN.md` — phased interface migration and rollback.
- `docs/interface/DESIGN_SYSTEM.md` — presentation/accessibility guidance.
- `docs/decisions/DECISION_REGISTRY.md` — confirmed, superseded, blocked, future, and MEEDO-input decisions.
- `docs/decisions/ADR_007_SOURCE_NATIVE_COLLECTION_IDENTITY.md` — source-native collection identity and Business Payor retirement.
- `docs/architecture/ARCHITECTURE_RULES.md` — non-negotiable implementation boundaries.
- `docs/architecture/APPLICATION_PATTERNS.md` — established implementation patterns.
- `docs/security/SECURITY_ARCHITECTURE.md` and `TENANT_ISOLATION.md` — security/tenant invariants.
- `docs/testing/TESTING_STRATEGY.md` — proof and validation responsibilities.
- current code, migrations, tests, CI/workflows, and verified production behavior — evidence of what is implemented now.

## 22. Agent execution rule
Before changing V2 code, an agent must be able to answer:

1. Is this behavior CURRENT, TARGET V2, FUTURE/HIDDEN, BLOCKED, or NEEDS MEEDO INPUT?
2. Which source/domain currently owns the data or mutation?
3. Does the task actually require a visual change?
4. Which current V3/shared UI pattern should be reused without changing the source's business semantics?
5. Which route/role/tenant/financial invariants must stay unchanged?
6. Which decision gate could this accidentally assume?
7. What focused tests and localhost visual check will prove the slice?

If those answers are unclear, stop and resolve the ambiguity before implementation.

---

This file is the orientation layer for StallTrack V2. Detailed domain rules continue to live in their canonical documents; do not duplicate or silently fork them here.
