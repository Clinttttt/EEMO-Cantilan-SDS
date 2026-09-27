# Sol High UI Audit Baseline

**Recorded:** 2026-09-28
**Audit baseline inspected:** pre-`586bca2c` canonical state (Phase 5A era)
**Purpose:** Preserve the important findings from the completed read-only Sol High Web + Collector Mobile audit so implementation agents do not repeat the audit or lose the identified UI risks.

> This is an implementation handoff, not a new business authority. When this file conflicts with later direct EEMO Head rulings, ADRs, the Decision Registry, or the V2 Master Specification, the newer authority wins.

## 1. Audit scope and limitation

The completed Sol High audit reviewed the current StallTrack UI source broadly across:

- approximately **78 routed Blazor Web components** / **94 route declarations**;
- approximately **12 Collector Mobile routes**;
- shell/sidebar/navigation;
- Overview;
- Operations and facility/source workspaces;
- Current Collection and Collection Activity;
- Online Payments;
- Payors & Accounts / legacy Vendor surfaces;
- Monitoring;
- Reports and print surfaces;
- Settings, Collectors, Audit, and admin areas;
- Payor-facing surfaces;
- Collector Mobile menu, work selection, collection, records, reports, pending-sync/reconciliation, and accountable-document flows.

The audit was primarily source-level. It did **not** constitute final rendered-browser visual approval.

## 2. Core conclusion that remains valid

The current StallTrack visual language should be **preserved**, not replaced.

Implementation should continue using the accepted:

- dark navy sidebar and navigation language;
- navy/gold/neutral visual identity;
- existing page background, typography, cards, borders, spacing, tables, controls, chips/badges, modal patterns, and responsive behavior;
- professional government-office tone.

The problem identified by the audit is primarily **information architecture, workflow truthfulness, terminology, capability ownership, and stale data presentation** — not the need for a new theme.

Do not invent an independent UI system, redesign the whole product, or make new pages visually unrelated to current StallTrack.
## 3. Major structural findings

### 3.1 Operations is structurally stale

The old Operations page follows report/income-style grouping too closely and does not cleanly represent how staff work.

The target work-oriented directory remains:

1. **Configured Facilities**
2. **Market Services**
3. **Space Operations**
4. **Utility Operations**
5. **Other Source Operations**

Operational navigation and financial-report grouping are separate concepts.

### 3.2 Sidebar is mostly sound

The existing global navigation should largely remain:

- Overview
- Operations
- Collection Activity
- Online Payments
- Payors & Accounts
- Monitoring
- Reports
- Collectors
- Audit Trail
- Settings

Do not add every operation to the sidebar.

**Current Collection** should remain contextual/resumable rather than automatically becoming a permanent global navigation item.

**Accountable Forms** should not be globally advertised until its UI/release state is intentionally approved.

### 3.3 Route ownership needs cleanup

The audit identified route/ownership inconsistencies, including the need to review the Overview recent-activity destination and sidebar active-state behavior.

Unauthorized users should not be shown dead/inaccessible admin destinations.

## 4. Truthfulness findings

A major audit concern was that several routed surfaces could look authoritative while still using static, sample, local-only, or legacy data.

Areas explicitly flagged for verification included:

- ECF account/report surfaces;
- WCF account/report surfaces;
- Market Fees / Market Fees report surfaces;
- Landing/Berthing reporting;
- other old report/workspace pages with fixed sample totals, rows, periods, or SavePayment-style local behavior.

Rule carried forward:

> **No fake/demo financial data may look like live EEMO data.**

If a real backend read model exists, use it.

If not, show a truthful empty, unavailable, legacy-coverage, or Setup Required state rather than fabricated numbers.
## 5. Collection Activity finding

The audit identified that the current `/collections/activity` experience may rely on a legacy transaction feed and may compute/display totals from a capped result set.

Therefore it must not silently present itself as the complete canonical financial ledger.

Target behavior:

- one parent Collection/accountable document;
- expandable classified Collection Lines;
- explicit allocations/source detail;
- clear distinction between canonical posted Collections and legacy/compatibility activity where both still exist;
- truthful coverage disclosure and pagination/filtering where needed;
- no double counting of compatibility projections.

## 6. Payors & Accounts finding

The audit identified a mismatch between the global **Payors & Accounts** concept and older `/vendors`-style screens.

Target semantics remain:

- **Payor** = continuing real-world financial identity;
- **PayorUser** = authentication/access identity only;
- Vendor/Occupant/Contract Lessee/Customer remain contextual roles and should not be globally renamed without checking context.

Do not infer or merge Payors by display name, phone alone, spelling similarity, OR number, or CT number.

## 7. Mobile findings

Collector Mobile should remain:

- focused;
- fast;
- assignment-driven;
- offline-safe;
- narrower than the Web administration/composer experience.

The audit flagged stale or legacy utility/collection semantics in Mobile, including cumulative utility concepts and inconsistent terminology.

Mobile must never become a generic free-form financial form.

It should not expose:

- unrestricted financial configuration;
- arbitrary rates;
- arbitrary OR/CT choice;
- arbitrary revenue lines;
- the full Web Composer.

Phase 4 document-custody and reconciliation safety must remain intact.
## 8. Findings superseded or refined by later EEMO Head rulings

The audit was completed before the latest 2026-09-27 Head clarifications. The following older audit conclusions must **not** be treated as current business policy.

### 8.1 Tabo

Old UI/audit concern: stale CT presentation.

**Current confirmed rule:** Tabo = **OR**.

### 8.2 Vegetable / Fruit Space Rental

Old audit state: OR/CT supported, exact resolver still pending.

**Current confirmed resolver:**

- full/whole payment -> **OR**;
- daily transaction -> **CT**.

The choice is no longer an unresolved collector discretion question.

### 8.3 ECF / WCF

Old interim audit guidance considered metered/shared/fixed presentation possibilities.

**Current Head direction:**

- **ECF = OR**, using **direct approved amount** for the current Cantilan workflow;
- **WCF = CT**, with the currently stated amount **PHP 10**.

Do not turn "direct approved amount" into arbitrary collector input.

Do not scatter PHP 10 as a hard-coded UI constant when effective configuration/policy should own the value.

### 8.4 Utility ownership

Old implementation/code strongly associates `UtilityBill` with NPM stalls.

**Current target architecture:** ECF/WCF are broader **EEMO Utility Operations**, not globally NPM-owned.

An NPM stall may be one utility service context, but NPM is not the architectural parent of every ECF/WCF assessment.

Existing NPM-bound `UtilityBill` data remains valid legacy/current source evidence and must not be destructively rewritten merely to generalize the future model.

### 8.5 Transfer Large Cattle

Old audit state: significant Cantilan policy uncertainty.

**Current Head direction:**

- it is a transfer transaction;
- it has a corresponding fee/amount;
- it is only occasionally used;
- direct approved amount entry is appropriate.

Still configurable/pending: exact Cantilan fee schedule, accountable-form/reference detail, and mandatory local regulatory fields/attestations.
## 9. Specialized-operation findings that remain important

- **Fish / Meat Vendor Fees** and **Weight & Measure / Registration** are separate operations/classifications and should not be collapsed into one generic Vendor Fee.
- **Transportation / Parking** should emphasize vehicle class + approved rate + CT collection rather than unnecessary driver/trip complexity as the target workflow.
- **Slaughterhouse** remains specialized; package/calculation components do not automatically become separate revenue classifications.
- **NPM** remains a specialized permanent-stall/rental workspace.
- **Arrears** is a financial/report condition/classification, not an Operations facility tile.
- **Ice Plant** remains a configured facility/rental-type source in the current StallTrack business model, not a new ice-sales product model.
- **Landing / Berthing** remains a specialized CT operation and should not be reduced to an arbitrary custom fee.
- **Market Fees** is its own official report/classification row; ECF, WCF, Tabo, Fish/Meat, Landing/Berthing, Transportation, Weight & Measure, Transfer Large Cattle, and Ice Plant are sibling report lines, not Market Fees children.

## 10. Implementation translation

The audit should be used as the baseline for the dedicated UI completion track:

- **U1:** shell/navigation truthfulness;
- **U2:** Operations V2 directory;
- **U3:** remove/gate misleading demo/static financial states;
- **U4:** align ECF/WCF and the NPM utility boundary;
- **U5:** complete missing/specialized source workspaces;
- **U6:** Collection Activity + Payors & Accounts;
- **U7:** Collector Mobile semantic alignment;
- **U8:** reports, responsive/accessibility, and final presentation QA.

The audit should **not** be rerun from scratch unless the UI changes materially enough to invalidate the source inventory.

## 11. Non-negotiable UI implementation rules

1. Preserve the current StallTrack design system and visual taste.
2. Reuse existing accepted page/component patterns before creating new ones.
3. Do not invent a new theme, independent component language, or unrelated visual pattern.
4. Do not make UI state imply a financial source is Canonical when it remains Legacy.
5. Do not activate Phase 5B through UI work.
6. Do not fabricate data to make a presentation look complete.
7. Do not rewrite backend financial architecture for cosmetic reasons.
8. Keep unsupported operations honestly gated.
9. Treat direct Head rulings and `586bca2c` as newer than the original audit.
10. Require rendered visual QA before calling broad UI completion visually approved.

## 12. Related canonical documents

Read this audit handoff together with:

- `docs/planning/STALLTRACK_V2_PHASE_STATUS.md`
- `docs/v2/STALLTRACK_V2_MASTER_SPECIFICATION.md`
- `docs/interface/INFORMATION_ARCHITECTURE.md`
- `docs/interface/DESIGN_SYSTEM.md`
- `docs/business/EEMO_OPERATIONAL_RULEBOOK.md`
- `docs/business/REVENUE_ARCHITECTURE.md`
- `docs/decisions/DECISION_REGISTRY.md`
- `docs/evidence/2026-09-27_eemo_head_final_clarifications.md`

This file preserves the Sol High audit findings; the authoritative business/decision documents above resolve later changes.
