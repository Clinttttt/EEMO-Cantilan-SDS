# StallTrack Interface Architecture

**Status:** Target interface architecture
**Baseline:** `22f45239adf998d22c33e402aa4c87e2465ac106`
**Applies to:** Web Office portal, Collector Mobile, and the Payor portal where noted

## 1. Purpose and authority

This document is the source of truth for StallTrack's target information architecture, navigation, page structure, and cross-surface interface vocabulary. It defines how users find and understand existing and future capabilities. It does not redefine financial rules, source-domain authority, authorization policy, or API behavior.

Authority remains, in order:

1. Current explicit business rulings and task-specific decisions.
2. Accepted business and revenue architecture in [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md) and [EEMO_BUSINESS_RULES.md](../business/EEMO_BUSINESS_RULES.md).
3. Implementation boundaries in [ARCHITECTURE_RULES.md](../architecture/ARCHITECTURE_RULES.md), [APPLICATION_PATTERNS.md](../architecture/APPLICATION_PATTERNS.md), and [SYSTEM_ARCHITECTURE.md](../architecture/SYSTEM_ARCHITECTURE.md).
4. Current code, migrations, tests, and verified production behavior as evidence of what is implemented now.
5. This document for interface organization within those boundaries.

When this document conflicts with an authoritative financial or business rule, the financial or business rule governs. Record the conflict in [DECISION_REGISTRY.md](../decisions/DECISION_REGISTRY.md); do not resolve it through labels, navigation, or presentation arithmetic.

Implementation sequencing and compatibility are defined in [MIGRATION_PLAN.md](MIGRATION_PLAN.md).

### Status language

- **CURRENT PRODUCTION** describes behavior present at the baseline.
- **TARGET ARCHITECTURE** describes the approved future interface structure.
- **FUTURE CAPABILITY** reserves placement for work that is not yet functional.
- **UNRESOLVED DECISION** identifies a business or UX decision that must not be inferred.

## 2. Current-state problem summary

**CURRENT PRODUCTION:** Web navigation grew around individual features and facility pages. Generic concepts, individual facilities, operational queues, reports, and configuration appear at overlapping hierarchy levels.

The principal structural problems are:

- every configured facility can become a permanent global navigation entry;
- mutable collection and follow-up workflows are located under Reports;
- `/accounts` means administrator and collector credentials, while the product also needs a business account concept;
- Settings combines business configuration, access management, office setup, audit, backup, and system information;
- facility pages use several independently composed headers, toolbars, contextual actions, and report entry points;
- reports mix receivables, cash collection, management analysis, operational records, and action queues;
- Web and Mobile use overlapping words for different tasks, including Vendor, Payor, Transaction, Payment, and Collection;
- Mobile correctly preserves specialized facility workflows, but its shell calls the work selector Menu, gives routine space to unassigned facilities, and places detailed sync state inside Records.

These are interface-structure problems. They do not imply that the specialized source domains or their financial rules should be consolidated.

## 3. Product mental model

StallTrack is an LGU economic-enterprise operations and revenue-collection system. Its interface must express five related layers without collapsing them:

1. **Operation:** the real-world enterprise activity, such as public-market operation, monthly space rental, slaughter activity, transportation/parking, or a weekly market.
2. **Obligation:** what is owed, for which source and period, as calculated by the authoritative source domain.
3. **Collection:** money received on a business date and attributed to an actor and source context.
4. **Accountable document:** the OR or Cash Ticket identity and lifecycle associated with a collection when that architecture becomes authoritative.
5. **Reporting and accountability:** read-only views of obligations, collections, documents, classifications, and targets using an explicit basis.

The following distinctions are architectural invariants:

- Facility is not Revenue Classification.
- Billing Basis is not Payment Cadence.
- Obligation is not Collection.
- Collection is not Remittance.
- Collection Efficiency is not Revenue Target Attainment.
- Collector Balance is not Customer Outstanding Balance.
- Delinquency is not Arrears.
- Delinquency is not Follow-up Severity.
- Space/Stall, Occupancy/Term, Payor, and Account answer different questions.

### Source authority

**CURRENT PRODUCTION:** `PaymentRecord`, `DailyCollection`, NPM settlement, TPM attendance, TRM trips, utility bills, slaughter transactions, and online-payment lifecycle records retain their established authority.

**CURRENT PRODUCTION:** `Collection` and `CollectionLine` exist as a dormant foundation and shadow-reconciliation target. They are not the universal production writer or report source.

**FUTURE CAPABILITY:** `AccountableDocument`, accountable-form inventory/custody, production Cash Ticket workflows, classified cash reporting, and annual target attainment are not current production authority.

The interface must never present a future common layer as authoritative before its writer, readers, reconciliation, and cutover have been approved.

## 4. Role and task model

| Role | Primary job | Frequent work | Occasional work | Sensitive work | First information needed |
|---|---|---|---|---|---|
| Head / SuperAdmin | Govern the office and oversee enterprise revenue | Review portfolio, collections, exceptions, outstanding obligations, and reports | Configure facilities/rates, manage people, inspect history | Revenue policy, provider configuration, access, audit, backup/restore, destructive lifecycle actions | Current collection position, operational exceptions, delinquency, ended-occupancy balances, pending document work |
| Admin / office staff | Run daily office operations | Record or correct allowed collection state, encode OR evidence, maintain accounts, follow up, produce reports | Import rosters/history and handle operational exceptions | Only actions explicitly allowed by existing authorization and business policy | Today's queues, current period, unresolved records, selected facility context |
| Collector | Capture assigned field collections | Resume assigned facility work, find the payor/activity, record money and permitted receipt evidence, sync | Review activity, retry sync, inspect summary, maintain profile | Accountable-form handling and corrections only when explicitly implemented and authorized | Server business date, assigned work, resume point, connectivity, sync state |
| Payor | Understand and settle personal obligations | View balances, pay eligible obligations, review history | Activate access and maintain profile | Online payment and personal account data | Total outstanding, affected facility/space/period, available payment action |

The Platform Operator remains outside this interface architecture in the separate operator console. Head and Platform Operator are not synonyms.

## 5. Design principles

1. **Organize by user responsibility.** Global workspaces represent stable jobs, not database tables or every route.
2. **Keep operational context explicit.** Facility, period, as-of date, and selected account remain visible while work is performed.
3. **Preserve specialized domain workflows.** Shared shells and components provide structure; they do not absorb source-domain calculations or commands.
4. **Make Reports read-only.** A page that changes collection, occupancy, follow-up, correction, or closure state belongs to an operational workspace.
5. **State financial basis.** Money views identify tenant, facility, occupancy scope, period, as-of date, and the applicable date/money basis.
6. **Separate current state from history.** Current occupancy and prior terms remain distinct, as do active delinquency and ended-occupancy balances.
7. **Hide unreleased capability.** Future placement is designed now, but empty navigation is not exposed.
8. **Reduce role noise.** A role sees usable destinations. Authorization remains enforced by the existing guards and endpoints.
9. **Keep contextual actions contextual.** Imports, record detail, app binding, provider setup, and lifecycle actions do not require permanent global navigation.
10. **Migrate additively.** Existing routes and external flows continue during transition.
11. **Keep Mobile task-focused.** Web workspace navigation must not be copied into Collector Mobile.
12. **Treat offline state as operational state.** A collector must know whether captured work is accepted, queued, retryable, or rejected.

## 6. Target Web workspace architecture

**TARGET ARCHITECTURE:** the Web Office workspaces are:

1. Overview
2. Operations
3. Collections
4. Spaces & Occupants
5. Monitoring
6. Accountable Forms
7. Reports
8. Administration

The former target **Payors & Accounts / Business Payors** workspace is retired by ADR-007 / IA-068. Source-specific registries remain under the operation that owns them, while unified New Collection searches across those sources.

Accountable Forms is a bounded active workspace; its exact available functions follow the current release state rather than this historical IA outline.

These workspaces describe **capability ownership and user responsibility**. They do not require the global sidebar to reproduce every workspace subdivision as permanent group labels. The approved global sidebar is a curated set of real, high-frequency destinations; contextual and secondary destinations remain inside their owning workspace. See `../v2/STALLTRACK_V2_MASTER_SPECIFICATION.md`, section 8.

`Overview` is the target interface label for the portfolio landing workspace called `Dashboard` in the earlier revenue-architecture outline. The purpose is unchanged; the target label emphasizes orientation and attention rather than a card-based visual treatment.

### Workspace responsibilities

| Workspace | Owns | Does not own |
|---|---|---|
| Overview | Portfolio status, today's activity, attention summaries, recent/relevant facilities | Full reports, configuration, record-changing queues |
| Operations | Facility selection and source-domain work | Cross-facility financial reporting or system configuration |
| Collections | Recorded collection activity, allowed recording/correction work, online-payment operations, and unified source-native New Collection | Obligation calculation, identity inference, provider credentials |
| Spaces & Occupants | Permanent-space and occupancy registry/detail for facilities that actually use spaces/stalls | Generic cross-operation Payor master data or Fish/Meat registration |
| Monitoring | Delinquency, exceptions, expiring/expired occupancies, ended-occupancy balances, and follow-up | Financial classification or report generation as the primary purpose |
| Reports | Read-only Receivables, Cash Revenue, Management, and Operational reporting | Mutable workflows |
| Administration | Business configuration, people/access, office setup, system administration | Daily collection work |

## 7. Target Web capability/navigation ownership tree

This tree defines where capabilities belong and how users reach deeper work. It is **not a requirement that every node or workspace heading appear as a permanent global-sidebar item**. The curated global sidebar is defined in the V2 master specification and may link directly into a workspace's primary current destination.

Items marked `[future]` remain hidden until functional. Items marked `[contextual]` are not permanent global entries.

```text
StallTrack
├── Overview
│   ├── Portfolio summary
│   ├── Today's activity
│   ├── Attention summary
│   └── Facility status
│
├── Operations
│   ├── Facilities
│   │   ├── Facilities landing
│   │   └── {Selected facility}
│   │       ├── Overview
│   │       ├── Work
│   │       ├── Spaces / Participants [where applicable]
│   │       ├── Activity
│   │       └── Reports
│   └── Source operations
│       ├── Market services
│       ├── Utilities
│       ├── Space rental
│       ├── Fish / Meat vendor registry
│       ├── Income From Terminal
│       ├── Transportation / Parking
│       └── Other approved specialized/governed services
│
├── Collections
│   ├── Collection Activity
│   ├── Collection Status & Corrections
│   ├── Online Payments
│   │   ├── Awaiting OR
│   │   └── Payment History
│   └── Remittance & Reconciliation [future; requires renewed approval]
│
├── Spaces & Occupants
│   ├── Current spaces / stalls
│   ├── Occupancies / terms
│   ├── Ended occupancies
│   └── Occupancy detail [contextual]
│       ├── Summary
│       ├── Obligations
│       ├── Collections
│       ├── Occupancy / Contract
│       ├── Follow-up
│       └── Activity
│
├── Monitoring
│   ├── Follow-up Queue
│   ├── Ended-Occupancy Balances
│   ├── Exceptions
│   ├── Expiring / Expired Occupancies
│   └── Follow-up History
│
├── Accountable Forms
│   ├── Documents
│   ├── Official Receipts
│   ├── Cash Tickets
│   ├── Inventory & Custody
│   └── Void / Replacement / Reconciliation
│
├── Reports
│   ├── Receivables
│   │   ├── Financial Position
│   │   ├── Outstanding & Aging
│   │   └── Collection Efficiency
│   ├── Cash Revenue
│   │   ├── Monthly Collections
│   │   ├── Collector Collections
│   │   ├── Facility / Date / Instrument
│   │   └── Revenue Classification [future]
│   ├── Management
│   │   ├── Facility Performance
│   │   ├── Revenue Trends
│   │   └── Revenue Target Attainment
│   └── Operational
│       ├── Facility Reports
│       ├── Stallholder Registers
│       └── Specialized Activity Lists
│
└── Administration [Head]
    ├── Business Configuration
    │   ├── Facilities & Billing Policies
    │   ├── Revenue Classifications
    │   ├── Collection Channels
    │   └── Revenue Targets
    ├── People & Access
    │   ├── Administrators
    │   ├── Collectors
    │   └── Facility Assignments
    ├── Office Setup
    │   └── Office Profile
    └── System Administration
        ├── Audit Trail
        ├── Backup & Restore
        └── System Information
```

Personal password, MFA, profile, and sign-out actions belong to the signed-in user menu rather than Administration navigation.

## 8. Facility architecture

### Facilities hub

**TARGET ARCHITECTURE:** one Facilities entry opens a tenant-scoped hub. Tenant configuration must not produce unbounded global-sidebar growth.

Each facility entry states:

- tenant-resolved facility name and code;
- operational archetype;
- active/available state;
- applicable current business date or period;
- a small status summary using that facility's correct basis;
- attention count where supported;
- an Open Facility action.

The hub must not compare unlike amounts as though they were one measure. A per-service facility's amount for today and a monthly facility's current obligation have different bases.

### Selected-facility context

A shared facility shell provides:

- facility name and short code;
- facility switcher limited to authorized, configured facilities;
- operational type;
- business date or period;
- facility state;
- one primary action;
- contextual Reports entry;
- Head-only configuration link into Administration.

The shell standardizes context and placement. It does not own billing or collection rules.

### Specialized work

| Operation | Shared context | Domain-owned work |
|---|---|---|
| NPM / daily stall | Overview, Spaces & Occupancies, Activity, Reports | Daily round, sections, catch-up days, closures/absence, contextual links to applicable utilities, RentGoal/PureDays settlement, and approved NPM Daily Collect All for today's daily charge only. Fish/Meat Vendor Fee and Weight & Measure are separate source operations, not NPM-owned. |
| TCC/NCC/BBQ/ICE/custom monthly rental | Overview, Spaces & Occupancies, Activity, Reports | Monthly status, installment/full/partial collection under current production rules, OR evidence, history |
| TPM / weekly market | Overview, Participants, Activity, Reports | Market-day calendar, vendor attendance, goods |
| Income From Terminal | Overview, Activity, Reports | CT direct aggregate entry for Comfort Room, Pull Pul Vans/Cargo Vans, and Tricycad; optional Cash Ticket count; vehicle-class-assisted entry where useful |
| Transportation / Parking | Overview, Activity, Reports | CT direct amount received; no required vehicle-class/rate basis and no TRM synonym |
| Fish / Meat Vendor | Registry, Activity, Reports | Independent annual/tax-year Fish-or-Meat registration, direct Vendor Fee collection, and registered-vendor eligibility for Weight & Measure |
| SLH / per head | Overview, Activity, Reports | Client, animal lines, head count, package/add-on context, receipt grouping |

Facility-specific navigation may omit inapplicable sections. Uniform empty tabs are not required.

### Terminal vehicle-rate evidence

The previously captured vehicle schedule remains useful **Terminal** configuration evidence, not Transportation/Parking policy.

Prospective mapping:

- Public Utility Bus, Public Utility Baby Bus, Jeepney, Van and Multicab → **PULL PUL VANS, CARGO VANS**;
- Tricycle → **TRICYCAD**.

The current office clarification also permits direct aggregate Terminal totals, so vehicle class/rate detail is optional for that aggregate path.

This evidence does not authorize retroactive reclassification. Historical `TrmTrip.Fee` and canonical Transportation records remain historical truth in their existing source/classification. Any cutover from legacy TRM/Transportation assumptions to the separated Terminal and Transportation/Parking model must be explicit, prospective and reconciliation-safe.

### Facility reports

A facility-context Reports entry and a global Reports entry may open the same report definition with different preselected scope. They must use the same source and state the selected facility, period, as-of date, and basis.

## 9. Source identity, occupancy, and space model

The interface must not force one entity to answer every question, and it must not reintroduce a universal Business Payor master after ADR-007.

| Question | Interface object |
|---|---|
| What physical unit is this? | Space; use Stall where that is the official facility term |
| Who holds it now or held it for a period? | Occupancy / Term |
| Which registered Fish/Meat business is this? | Fish/Meat Vendor Registration |
| Which source record establishes collection eligibility? | Typed source identity owned by that operation |
| What payer name/reference was recorded for a permitted one-off collection? | Frozen payer snapshot on the Collection |
| What is owed, collected, credited, and outstanding for a relationship-backed source? | The owning source account/obligation view |
| What happened under an earlier holder or term? | Earlier occupancy/account |

### Person terminology by operation

Use relationship-specific language rather than treating `Vendor` as universal:

- **Vendor** — temporary/TPM seller or participant where that term matches the operation;
- **Fish/Meat Vendor** — independently registered Fish or Meat business/vendor; this is a source-owned registration identity, not an NPM occupant or Business Payor;
- **Occupant** — permanent rental-space holder;
- **Payer / Payor snapshot** — name/reference frozen on a posted collection when the operation allows direct/one-off collection; it does not create a cross-source identity;
- **Client** — Slaughterhouse actor;
- **Stallholder** — retained where it is established office/report terminology.

These are interface terms. They do not require renaming backend entities merely for consistency.

**TARGET ARCHITECTURE:** Account is a user-facing composition for one occupancy/term and its financial relationship. It need not become a new backend aggregate merely to support the interface.

An account detail page contains:

1. summary and status;
2. current occupancy/term;
3. obligations using the authoritative source domain;
4. collections and current receipt evidence;
5. specialized charges where applicable;
6. follow-up state;
7. earlier occupancies/terms as separate records;
8. activity/audit evidence;
9. accountable documents only after that capability is authoritative.

Historical liability rules remain unchanged:

- current and earlier-term balances are displayed separately;
- renewing a term does not write off earlier debt;
- a new holder does not inherit the prior holder's balance;
- collection against a prior term remains attributed to that term;
- an ended-occupancy balance is not presented as the sitting occupant's delinquency.

## 10. Collections architecture

### Current production boundary

Collections is an interface workspace, not a declaration that `Collection`/`CollectionLine` is already the universal source. Current specialized writers and readers remain authoritative until their approved migrations complete.

### Target responsibilities

- **Collection Activity:** the cross-source feed currently described generically as Transactions.
- **Collection Status & Corrections:** existing allowed status recording, exception, closure, OR-evidence, correction, or reversal work, subject to current authorization and source-domain rules.
- **Online Payments:** operational lifecycle, including Awaiting OR and payment history.
- **Context-aware capture:** collection normally begins from the selected operation, account, obligation, or activity.
- **General Record Collection:** may be exposed only when the required canonical backend capability exists; it must progressively request context and must not become a form containing every domain field.

Provider credentials and webhook configuration belong to Administration > Business Configuration > Collection Channels.

### Current correction authority

Head and Admin retain the existing operational correction capabilities that the current application authorizes, including allowed collection/status corrections, current OR-evidence updates, occupancy lifecycle actions, inactive-record actions, and facility closure management. Current office practice does not require a separate free-text correction reason; audit history remains the accountability record.

This does not define the future AccountableDocument/Cash Ticket void-and-replacement policy. Accountable-form corrections may require stricter evidence, reason, or approval rules when that lifecycle is designed.

### Future remittance

**REMITTANCE (IA-052):** Accountable Forms > Remittance & Liquidation is the money-accountability workspace, separate from form custody. The earlier partial collector-remittance workflow was retired and is not resurrected; the new one covers whole authoritative collections exactly once and adds no Treasury workflow.

A simple `Remitted = Yes/No` field is not an acceptable substitute. Office accountable-form evidence treats remittance as an amount-and-accountability process with date, accountable officer, recipient/acknowledgement, deposit/cashier context, and collection-versus-remittance reconciliation. The reserved placement does not authorize implementation, restoration of the retired workflow, a Treasury approval flow, or any assumption about ownership.

## 11. Monitoring architecture

Monitoring owns attention and action queues:

- delinquent active accounts;
- current-period unpaid or partial obligations that need attention;
- missing OR evidence and other collection exceptions;
- expiring and expired occupancies;
- ended-occupancy balances;
- follow-up history.

### Delinquency and severity

**CURRENT PRODUCTION:** Delinquent means at least one fully elapsed unpaid month.

For active accounts:

- one or two elapsed unpaid months are delinquent, lower-age, and Normal / this-period follow-up;
- three or more elapsed unpaid months are delinquent, higher-age, and Critical / immediate follow-up.

Three months is a follow-up severity boundary, not the delinquency threshold.

**UNRESOLVED DECISION:** Arrears means qualifying old/lapsed debt, but the exact qualification boundary is unresolved. The interface must not derive Arrears from the one-to-two or three-plus age bands. Ended/past occupancy balances remain a separate operational condition.

Monitoring may link into an account or operation to complete work. Its queues must not become independent sources of obligation arithmetic.

## 12. Reports architecture

### Read-only principle

**TARGET ARCHITECTURE:** Reports are read-only analytical or document surfaces. Mutable workflows move to Operations, Collections, Spaces & Occupants, the owning source operation, Monitoring, or Administration.

### Report families

| Family | Question | Basis/source |
|---|---|---|
| Receivables | What was due, settled, credited, collected against obligation, and left outstanding? | Authoritative obligation domain and obligation period |
| Cash Revenue | What money was received by date, facility, collector, instrument, and future classification? | Current specialized collection sources until an approved classified-cash cutover |
| Management | What changed, how are facilities performing, and eventually how does classified revenue compare with approved targets? | Declared management scope; target attainment remains separate from collection efficiency |
| Operational | What roster, activity list, or facility-specific register supports office work? | Owning source domain and declared facility/period scope |

### Required report scope

Every financial report states:

- tenant/municipality;
- facility scope;
- occupancy scope where applicable;
- period;
- as-of date;
- money basis;
- date basis: activity date, obligation period, collection business date, or document issue date;
- generated timestamp;
- source limitations where the target common layer is not authoritative.

Print, PDF, and CSV are actions on a scoped report. They are not independent top-level information architectures.

### Current operational pages that do not belong under Reports

- Collection Manager;
- Follow-up Queue and its actionable history;
- Closed/ended account lifecycle actions;
- online payments awaiting OR;
- market closure and collection exception work.

The complete Official Monthly Income structure/signatories are now confirmed by IA-067. Other report/document families may still have separate official-status, retention or signatory decision gates.

### Office-reference report structures

Recent EEMO reference material provides business-structure evidence for future report design without requiring StallTrack to copy legacy spreadsheets visually:

- **Monthly Rental of Stall Occupants:** occupant, monthly rental, January–December collections, total payment, total yearly rental, balance, totals/subtotals, Prepared by, and Verified by.
- **Lessee / Stall Monitoring:** actual occupant/lessee, stall or space number, actual monthly rental, whole-year rental, yearly collection, and balance.
- **Monthly Income / Market Operations:** Annual Target, monthly actual columns, Total/YTD and Percentage.
  - **A. Income From Market** keeps the confirmed Market sibling lines and the documented Rent Income / Space Rental subsections.
  - **B. Income From Terminal** contains **COMFORT ROOM**, **PULL PUL VANS, CARGO VANS**, **TRICYCAD**, then Total Income from Terminal.
  - **C. Income from Slaughterhouse** is a separate top-level section.
  - The statement closes with **OVERALL TOTAL MARKET COLLECTION**, Prepared by, and Certified Correct.
  - Tabo uses OR. Vegetable/Fruit Space Rental uses OR for full/whole payment and CT for daily transactions. ECF uses OR; WCF uses CT. Transportation/Parking is separate from Terminal and uses direct-amount CT.
  - Fish/Meat Vendor Fee is independent from NPM; Weight & Measure requires the independent registered Fish/Meat vendor source.
  - See [2026-10-06 MEEDO office clarification](../evidence/2026-10-06_meedo_office_terminal_fish_vendor_monthly_income_clarification.md), IA-067 and IA-068.
- **Accountable-form reporting:** includes collection/accountability and remittance/deposit sections; its existence does not make digital remittance a current StallTrack capability.

**TARGET ARCHITECTURE:** preserve required business information and configurable report date/signatories while allowing a cleaner StallTrack-owned layout. Office evidence does not by itself prove that every referenced sheet is an official statutory output or that every revenue line already has a final production `RevenueClassification`.

### Revenue-target and report-adjustment governance

The office Monthly Income reference uses **Annual Target → monthly actuals → Total/YTD → Percentage**.

IA-066 now confirms a bounded governance model:

- tenant/calendar-year/official-row target;
- externally approved source amount;
- Head-only entry/revision;
- source/reference/actor/timestamp evidence;
- immutable revision history;
- no inferred growth formula.

Head-only official report adjustments may revise a reported Monthly Income cell through an audited delta/revision while preserving the system basis. They never edit Collections, source obligations, remittance or collector position.

Prepared by / Certified Correct name + position are configurable Office Settings under IA-067.

## 13. Administration and configuration architecture

```text
Administration
├── Business Configuration
│   ├── Facilities & Billing Policies
│   ├── Revenue Classifications
│   ├── Collection Channels
│   └── Revenue Targets
├── People & Access
│   ├── Administrators
│   ├── Collectors
│   └── Facility Assignments
├── Office Setup
│   └── Office Profile
└── System Administration
    ├── Audit Trail
    ├── Backup & Restore
    └── System Information
```

### Boundaries

- Facility catalog, billing basis, effective-dated rates, NPM basis/sections, TPM market day, and SLH labels belong to Business Configuration.
- Revenue Classification configuration may exist before it becomes authoritative for production cash reports; the interface must say so.
- Administrator and collector identity/access belong to People & Access.
- Collector performance reports belong to Reports, not access management.
- Collector-app binding/download is a contextual collector administration action.
- PayMongo or other provider configuration belongs to Collection Channels; the payment work queue remains under Collections.
- Facility collection history belongs to facility Activity, not configuration.
- Personal password and MFA controls belong to the signed-in account menu.
- Audit, backup/restore, and system information are distinct from business setup.

## 14. Mobile Collector architecture

Collector Mobile is a separate task architecture. It does not mirror the Web workspaces.

### Primary model

1. Collect
2. Activity
3. Summary
4. Me

### Exact target Mobile navigation tree

```text
Collector Mobile
├── Collect
│   ├── Resume current work
│   ├── New Collection
│   │   ├── Search source records
│   │   ├── Relevant eligible items
│   │   └── Direct/one-off fallback when server policy allows
│   ├── Income From Market
│   ├── Rent Income
│   ├── Space Rental
│   ├── Income From Terminal
│   │   ├── Comfort Room
│   │   ├── Pull Pul Vans, Cargo Vans
│   │   └── Tricycad
│   └── Income From Slaughterhouse
├── Activity
│   ├── Today
│   ├── History
│   └── Sync Center
│       ├── Pending
│       ├── Failed / retryable
│       └── Rejected / action required
├── Summary
│   ├── By period
│   ├── By source / participant
│   └── Assigned operations
└── Me
    ├── Profile
    ├── Assigned operations
    ├── App settings
    ├── App update
    └── Sign out
```

### Collect

- Display the server-issued business date when available.
- Put Resume Current Work first when a safe resume context exists.
- Organize operations using the confirmed Monthly Income families.
- New Collection searches **source-native records** rather than Business Payors; the result must state its source context.
- Show only server-confirmed eligible operations for the chosen source record.
- Registered Fish/Meat vendors may expose Vendor Fee and Weight & Measure; Weight & Measure never accepts an unregistered free-text vendor.
- If no source relationship exists, offer only direct/one-off operations whose policy permits payer snapshot text.
- Transportation/Parking is direct amount CT; Terminal is separate and owns the vehicle-class-assisted collection path.
- NPM Daily may provide **Collect All** for today's daily charge only. Unchecked stalls remain unpaid and each selected stall posts its own Collection/SRC.
- Remove unassigned/unavailable operations from routine field navigation or place them in a clearly secondary state.
- Keep Collect selected in the bottom navigation while inside collection work.

### Activity and Summary

Activity owns recorded work, history, filters, and Sync Center. Summary owns collector-scoped read-only reporting. Summary may change its period vocabulary by archetype, such as day, market day, or month.

### Me

Me owns profile, assignment information, device/app preferences, app update state, tenant switching where supported, and sign-out.

## 15. Offline and sync UX architecture

**CURRENT PRODUCTION:** Mobile writes retain the offline queue and `ClientOperationId` idempotency. The server-issued session business date is authoritative when available; device time is fallback only.

**TARGET ARCHITECTURE:** sync state is persistent operational context, not a hidden utility.

The global sync indicator distinguishes:

| State | Meaning | Required affordance |
|---|---|---|
| Online and synced | No local work needs attention | Quiet confirmation |
| Offline, queued | Captures are stored locally | Count and explanation that automatic sync will resume |
| Syncing | Retry/replay is active | Progress without blocking continued safe work |
| Failed, retryable | A transient attempt failed | Retry and diagnostic message |
| Rejected, action required | The server refused the operation | Persistent attention state and item-specific reason |
| Storage fault | Queue persistence is not reliable | Prominent instruction to sync with signal and report the device |

Sync Center lives under Activity and is reachable from the global indicator. A queued capture must remain visible in its facility workflow so a collector does not record it twice.

## 16. Cross-surface terminology

| Term | Canonical interface meaning | Usage boundary |
|---|---|---|
| Facility | Tenant-configured economic enterprise or operational area | Never a synonym for Revenue Classification |
| Revenue Classification | Stable semantic classification of revenue | Does not determine facility, billing basis, or instrument by itself |
| Space | Generic physical assignable unit | Use Stall where that is the official operational term |
| Stall | A market stall/space | Do not use for transporter, slaughter owner, or every temporary participant |
| Payer snapshot | Name/reference recorded on a specific Collection when the source permits it | Not a cross-source master identity and never enough to infer eligibility |
| Occupant | Person actually occupying a space | May differ from contract name or payor |
| Occupancy / Term | Period in which a party holds a space and answers for that period's liability | Current and earlier terms remain separate |
| Account | Interface view of one occupancy/term and its financial relationship | Not an administrator login account after namespace migration |
| Obligation | Amount owed for an authoritative source and period | Not a Collection |
| Collection | Money received and recorded on a business date | Not a Remittance |
| Payment | Payor-facing act of settling an obligation | Paid is an obligation state; Collected describes money received |
| Collection Activity | Cross-source feed of recorded collections | Preferred workspace wording over generic Transactions |
| Transaction | Provider/source/technical transaction when that distinction matters | Avoid as an unqualified global workspace label |
| Official Receipt / OR | Official receipt identity/number | Current OR fields do not imply AccountableDocument authority |
| Cash Ticket | Accountable cash-ticket instrument | Inventory/custody remains future |
| Outstanding Balance | Obligation less valid collections and credits for the stated scope | Not Collector Balance |
| Delinquent | At least one fully elapsed unpaid month | Not Arrears and not a severity label |
| Normal follow-up | Lower-age active delinquency or routine current-period attention | Does not mean not delinquent |
| Critical follow-up | Higher-age active delinquency or other immediate attention | Severity, not a new debt type |
| Ended-Occupancy Balance | Balance belonging to a past/ended occupancy | Separate operational condition |
| Arrears | Qualifying old/lapsed debt | Exact boundary unresolved; never inferred from age bands |
| Follow-up | Operational attention/action | Not a financial state |
| Collector | Assigned field collection user | Distinct from payor and office administrator |
| Remittance | Handoff/reconciliation of collected money | No current complete workflow; not a Collection |
| Collection Efficiency | Collected against eligible obligation for matching scope | Not Revenue Target Attainment |
| Revenue Target Attainment | Classified official actual revenue against an approved annual target | Head-governed target revisions under IA-066; not Collection Efficiency |
| Report | Read-only result with explicit scope, period, basis, and source | Mutable work does not belong under Reports |

Facility display names, office identity, section labels, rates, series, and market day must be tenant-resolved. Codes may accompany names according to the unresolved facility-name UX decision.

## 17. Page anatomy and structural patterns

### Universal page anatomy

1. **Workspace header:** breadcrumb, title, concise purpose, one primary action.
2. **Context/scope:** facility, account, period/as-of, report basis, role-relevant filters.
3. **Summary:** a small set of reconciled metrics.
4. **Main work/data:** table, queue, calendar, capture flow, or report.
5. **Secondary information:** definitions, notes, history, related links.
6. **Feedback:** loading, empty, error, conflict, queued, success, and permission states.

### List pages

- show title, scope, count, search/filter controls, and a dense table;
- use row actions and only valid bulk actions;
- state the active facility, period, and filters in empty states.

### Detail pages

- establish stable entity identity and status;
- show effective dates and related entities;
- separate current state from earlier terms/history;
- keep financial bases explicit;
- place destructive actions outside the primary reading flow.

### Operational collection screens

- keep facility and business date visible;
- establish payor/activity identity before amount entry;
- show amount and permitted instrument evidence;
- provide one primary save action;
- distinguish accepted, queued, retryable, and rejected outcomes.

### Configuration pages

- group by business subject;
- distinguish current from scheduled effective state;
- expose history without crowding the edit form;
- state Head-only authority where applicable.

### Confirmation flows

Confirmations state the entity, effective date/period, financial effect if any, resulting lifecycle state, reversibility, and required authority.

## 18. Frontend component architecture principles

### Shared components

Use shared components where semantics remain stable across domains:

- `WorkspaceHeader`
- `Breadcrumbs`
- `ScopeBar`
- `FacilitySwitcher`
- `PeriodSelector` and `AsOfSelector`
- `FilterBar`
- `ActionToolbar`
- `TableShell`
- `SummaryStrip`
- `MoneyDisplay`
- `StatusBadge`
- `ReportShell`
- standard loading, empty, error, and confirmation states
- Mobile `SyncIndicator`

### Domain components

Use domain components where reuse preserves one business meaning:

- `FacilityShell`
- space/occupancy summary and term history
- obligation ledger
- collection-activity table
- follow-up queue table
- online-payment awaiting-document queue
- NPM day calendar and utility panel
- TPM market-day selector
- TRM trip entry
- SLH animal entry
- future accountable-document status

### Page-specific components

Keep backup/restore, provider credentials, import preview/mapping, statutory print layouts, NPM settlement explanations, and source-specific report sections local unless a second use shares their semantics.

Visual similarity or line-count reduction alone does not justify abstraction. Presentation components must not reproduce financial arithmetic owned by a domain/query.

## 19. Route architecture principles

Target route families are:

```text
/overview
/operations
/facilities/{facility}/...
/collections/...
/accounts/...
/monitoring/...
/reports/{family}/...
/admin/{area}/...
```

`/operations` is the target Operations landing route and initially serves as the Facilities landing. Selected facilities use `/facilities/{facility}/...`.

Principles:

- introduce canonical routes additively;
- preserve current routes as aliases or compatibility redirects during transition;
- preserve route parameters and query strings;
- keep current authorization unchanged;
- do not rename token, activation, authentication, payment callback, or other external routes casually;
- do not move `/accounts` to the business namespace until administrator accounts have a canonical Administration route;
- do not replace stall-key profile routes with account/occupancy IDs until a stable canonical identity exists;
- allow facility-context and global report routes to resolve to the same report definition;
- retire legacy aliases only under the conditions in the migration plan.

## 20. Future-capability placement

| Capability | Reserved placement | Current status and constraint |
|---|---|---|
| AccountableDocument | Accountable Forms > Documents; linked from Collection/Account detail | Future; not production authority |
| Official Receipt management | Accountable Forms > Official Receipts | Future lifecycle; current OR evidence remains in source workflows |
| Cash Ticket books/series | Accountable Forms > Cash Tickets > Inventory & Custody | Future; detailed custody policy unresolved |
| Remittance reconciliation | Accountable Forms > Remittance & Liquidation (IA-052) | Whole-collection coverage, exactly once; no Treasury approval workflow |
| Revenue-classification reporting | Reports > Cash Revenue > Revenue Classification | Future production report; current classifications/setup do not make it authoritative |
| Annual revenue targets | Reports > Management > Target Attainment; setup under Administration | IA-066 confirms Head-only annual target revisions with source/reference audit evidence |
| WCF collection | Relevant utility operation on Web/Mobile according to the current release contract | One canonical financial source; direct/prepared rules stay server-owned |
| Terminal vehicle classes/rates | Administration > Business Configuration; used by Income From Terminal assisted entry | Prospectively mapped to Pull Pul Vans/Cargo Vans or Tricycad; aggregate Terminal entry does not require class/rate detail |
| Transportation / Parking | Operations > Market source; Mobile direct collection | CT direct amount; no Terminal/TRM dependency |
| Additional facility types | Operations > Facilities | Add an archetype-specific Work surface without new permanent global navigation |

## 21. Explicit non-goals

This architecture does not:

- alter financial or delinquency behavior;
- define the unresolved Arrears boundary;
- replace specialized source domains;
- declare `Collection`/`CollectionLine` the universal production writer;
- declare `AccountableDocument` authoritative;
- implement Cash Ticket inventory/custody;
- restore the retired remittance workflow;
- implement classified revenue reports or target attainment;
- define unresolved allocation, instrument, classification, vehicle-rate, or target policy;
- change API contracts, database schema, migrations, or authorization;
- require a big-bang route or page rewrite;
- copy Web navigation into Mobile;
- prescribe CSS or visual implementation details beyond structural rules;
- redesign the separate Platform Operator console.

## 22. Related documents

- [MIGRATION_PLAN.md](MIGRATION_PLAN.md) — additive implementation sequence, compatibility, validation, rollback, and completion criteria.
- [DECISION_REGISTRY.md](../decisions/DECISION_REGISTRY.md) — confirmed facts, approved architecture, future placement, technical constraints, and unresolved EEMO/UX decisions.
- [REVENUE_ARCHITECTURE.md](../business/REVENUE_ARCHITECTURE.md) — authoritative target revenue model and financial migration direction.
- [EEMO_BUSINESS_RULES.md](../business/EEMO_BUSINESS_RULES.md) — current specialized behavior and accepted product semantics, subject to later explicit rulings.
