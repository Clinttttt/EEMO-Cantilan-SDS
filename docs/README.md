# StallTrack Documentation

This directory is the permanent, tool-neutral source of project knowledge for StallTrack. It is intentionally independent of Kiro, Codex, ChatGPT, IDE choice, or any other agent/runtime.

`AGENTS.md` is the concise entry point for coding agents. This directory contains the durable product, architecture, security, interface, business, testing, operational, and decision material those agents and human maintainers must follow.

> **Office rename.** The office is now the Municipal Economic Enterprises Development Office (MEEDO). Historical records in this directory (decision registry, evidence, meeting notes) may retain the former EEMO terminology and are intentionally not rewritten. Technical identifiers such as the `EEMOCantilanSDS.*` projects/namespaces are unchanged.

## Authority and conflict handling

Use this precedence when sources disagree:

1. Explicit current MEEDO/business ruling or explicitly approved task-specific decision.
2. Accepted decision record in `docs/decisions/`.
3. Accepted business/revenue architecture and business-rule documentation.
4. Security and architectural invariants.
5. Current code, migrations, tests, CI/workflows, and verified production behavior as evidence of what is implemented now.
6. Interface/design documentation for presentation and information architecture.
7. Skills/runbooks as execution guidance.
8. Historical planning notes and evidence.

Do not silently choose whichever source is easiest to implement. Surface the contradiction, identify which source is stale, and resolve it before changing financial, tenancy, authorization, or accountability behavior.

## Core reading order

Before changing StallTrack code, read:

- `planning/CURRENT_RELEASE_STATE.md` — the first implementation-status checkpoint. It separates deployed production from the newer accepted local integration state.
- `planning/ACTIVE_WORKSTREAMS.md` — current checkout/branch coordination. The default primary checkout is `C:\dev\stalltrack\eemo`; extra worktrees are only for genuine concurrent branches.
- `v2/STALLTRACK_V2_MASTER_SPECIFICATION.md` — durable product/change-control map. Historical phase snapshots inside it are not newer than `CURRENT_RELEASE_STATE.md`.
- this `README.md` — authority order and canonical documentation map.
- for Terminal/Transportation, Fish/Meat/Weight & Measure, source-native collection identity, NPM Daily Collect All, or the complete Monthly Income report, read `planning/OFFICE_CLARIFICATION_REFACTOR_20261006.md` and the 2026-10-06 evidence before using older assumptions.
- for the exact October 7 implementation contracts and verification, read the four current handoffs: `COLLECTOR_PRODUCTIVITY_FOLLOWUP_HANDOFF_20261007.md`, `FISH_MEAT_REGISTRY_MANAGEMENT_HANDOFF_20261007.md`, `MOBILE_EDIT_ATOMICITY_FOLLOWUP_HANDOFF_20261007.md`, and `NPM_ARREARS_READINESS_HANDOFF_20261007.md`.
- the relevant authoritative business/architecture source for the task, especially `architecture/ARCHITECTURE_RULES.md`, `architecture/APPLICATION_PATTERNS.md`, `business/EEMO_BUSINESS_RULES.md`, and `business/REVENUE_ARCHITECTURE.md`.
- `decisions/DECISION_REGISTRY.md` before assuming a business, accountability, report, route-identity, target, remittance, or classification decision.
- current code, migrations, tests, CI/workflows, and verified production behavior as implementation evidence.
- the relevant specialized interface, security, testing, operations, or evidence documents below.

## Documentation map

### V2 orientation

- `v2/STALLTRACK_V2_MASTER_SPECIFICATION.md` — canonical V2 orientation and change-control specification. Start here; follow its links to the detailed authoritative domain documents.
- `v2/ITEMIZED_COLLECTIONS_CANONICAL_IMPLEMENTATION_BASELINE.md` — Clint-approved itemized collection architecture and sequential implementation baseline, including the remaining source-specific rollout gates.

### Architecture

- `architecture/ARCHITECTURE_RULES.md` — layering, CQRS, tenancy, money, EF Core, auth, Blazor, testing, deployment rules.
- `architecture/APPLICATION_PATTERNS.md` — command/query/repository/controller/API-client/component/test patterns.
- `architecture/SYSTEM_ARCHITECTURE.md` — system design rationale.
- `architecture/REPOSITORY_STRUCTURE.md` — solution and folder ownership.
- `architecture/TECH_STACK.md` — frameworks, runtimes, database, deployment and local configuration.
- `architecture/ONBOARDING_FLOW.md` — municipality onboarding flow.

### Business

- `business/EEMO_OPERATIONAL_RULEBOOK.md` — direct MEEDO Head/staff rulings, office-evidence reconciliations, confirmed operational rules, and the short list of genuinely unresolved business questions. The legacy filename is retained for link stability. Read this before asking MEEDO to re-confirm workflow details.
- `business/EEMO_BUSINESS_RULES.md` — current specialized facility behavior and accepted office semantics. The legacy filename is retained for link stability.
- `business/REVENUE_ARCHITECTURE.md` — target collections, classifications, accountable documents, reporting, and migration phases.

### Interface

- `interface/INFORMATION_ARCHITECTURE.md` — target Web/Mobile information architecture and vocabulary.
- `interface/MIGRATION_PLAN.md` — incremental interface migration.
- `interface/DESIGN_SYSTEM.md` — reusable UI hierarchy, tokens, states, accessibility, and consistency rules.
- `interface/STALLTRACK_UI_V3_DIRECTION.md` — Clint-approved V3 Web visual direction. Presentation authority only; supersedes older visual-preservation rules for Web presentation, never business rules.
### Security

- `security/SECURITY_ARCHITECTURE.md` — authentication, authorization, token, MFA, secrets, audit, and security invariants.
- `security/TENANT_ISOLATION.md` — municipality scoping and cross-tenant safety.

### Decisions

- `decisions/DECISION_REGISTRY.md` — confirmed facts, architecture decisions, blocked items, future capabilities, and unresolved questions.
- `decisions/ADR_007_SOURCE_NATIVE_COLLECTION_IDENTITY.md` — 2026-10-06 target identity architecture: source-owned records replace Business Payor as the normal collection-discovery model; name matching remains prohibited.
- New long-lived architecture decisions should use ADR files in this directory when one decision deserves an independent lifecycle.

### Testing

- `testing/TESTING_STRATEGY.md` — which test layer proves which kind of behavior and required validation gates.

### Operations

- `operations/PRODUCTION_VERIFICATION.md` — production release verification checklist.
- Existing detailed operational procedures may also live as reusable skills under `.agents/skills/`.

### Planning and evidence

- `planning/CURRENT_RELEASE_STATE.md` — single current implementation-status checkpoint. Prefer this over old dated audits/handoffs when asking what is live, canonical, legacy, pending or blocked.
- `planning/MOBILE_ITEMIZED_COLLECTION_BACKEND.md` — bounded backend feature-branch contract, actual supported/deferred sources, atomic checkout boundaries and Mobile.Core UI handoff; not a production activation checkpoint.
- `planning/REPORT_GOVERNANCE_FAST_COLLECTION_HANDOFF.md` — IA-066 local backend contracts for approved targets/report adjustments, space assignments/readiness, typed follow-up, multi-collector remittance and fast collection. Not a production activation checkpoint.
- `planning/OFFICE_CLARIFICATION_REFACTOR_20261006.md` — authoritative implementation plan for the office clarification; most of its target behavior is now implemented in the accepted local integration checkpoint, while production rollout remains separate.
- `planning/COLLECTOR_PRODUCTIVITY_FOLLOWUP_HANDOFF_20261007.md` — implemented NPM Daily/Whole, recent collections, correction capabilities, Terminal payer snapshot and Fish/Meat import contracts.
- `planning/FISH_MEAT_REGISTRY_MANAGEMENT_HANDOFF_20261007.md` — implemented Fish/Meat lifecycle, renewal/close, management totals and source eligibility.
- `planning/MOBILE_EDIT_ATOMICITY_FOLLOWUP_HANDOFF_20261007.md` — real API/DI atomic Edit correction fix and NPM Daily verification.
- `planning/NPM_ARREARS_READINESS_HANDOFF_20261007.md` — current NPM arrears optional-receipt behavior and Collect All readiness/SourceStillLegacy evidence.
- `planning/ACTIVE_WORKSTREAMS.md` — current single-primary-checkout/sequential-branch model plus an archived copy of the former many-worktree map.
- `planning/STALLTRACK_V2_PHASE_STATUS.md` — historical phase implementation record and release-gate evidence. Use it for phase history, not as a substitute for the current release-state checkpoint.
- `planning/SOL_HIGH_UI_AUDIT_BASELINE.md` — preserved read-only Sol High Web/Mobile audit findings plus the later Head-rule deltas that supersede stale audit assumptions. Use this as the UI completion handoff, not as business authority.
- `planning/ITEMIZED_COLLECTIONS_PHASE3_WRITER_INVENTORY.md` — current monthly PaymentRecord settlement writers and their Phase 5 cutover readiness prerequisites; inventory only, not activation authority.
- `planning/PHASE4_WCF_WRITER_READINESS.md` — current UtilityBill Water/WCF writers, compatibility readers, Cash Ticket custody, and scoped cutover prerequisites; inventory only, not activation authority.
- `planning/PHASE5A_SETTLEMENT_CUTOVER_CONTROL_PLANE.md` — Phase 5A source-scoped readiness evidence, writer/device/payment/document/report gates, and the test-only freeze/activation boundary; not authority to convert a real source.
- `planning/IMPLEMENTATION_HISTORY.md` — historical implementation/backlog record. It is evidence, not automatic current authority.
- `evidence/2026-10-06_meedo_office_terminal_fish_vendor_monthly_income_clarification.md` — direct office clarification that governs Terminal, Transportation/Parking, Fish/Meat, Weight & Measure, source-native collection, NPM Daily Collect All, report adjustments, and signatories.
- `evidence/2026-10-06_terminal_income_monthly_report.png` and `evidence/2026-10-06_fish_retailing_business_table.png` — photographed office references supporting that clarification.
- `evidence/` — remaining office/reference evidence retained with the repository.
- `../tools/diagnostics/` — diagnostic scripts; diagnostics are evidence tools, not application behavior.

## Stable distinctions

The documentation must preserve these distinctions:

- Facility is not Revenue Classification.
- Billing Basis is not Payment Cadence.
- Obligation is not Collection.
- Collection is not Remittance.
- Collection Efficiency is not Revenue Target Attainment.
- Collector Balance is not Customer Outstanding Balance.
- Delinquency is not Arrears.
- Delinquency is not Follow-up Severity.
- Space/Stall, Occupancy/Term, source-owned identity, payer snapshot, and login account are different concepts.
- A matching name is never enough to merge source identities or establish collection eligibility.
- Current production authority is not the same as target architecture.

## Documentation change rules

- Preserve business meaning when reorganizing files.
- A move/rename is not permission to rewrite a business rule.
- Update internal links, tests, comments, workflow path filters, and skills when a canonical document moves.
- Avoid duplicating the same rule in several documents. Prefer one canonical rule and link to it.
- Historical evidence should be clearly labeled as historical.
- A dated planning/handoff document is a snapshot, not current implementation authority. Compare it with `planning/CURRENT_RELEASE_STATE.md`, current Git/code/tests and the Decision Registry before acting on an old TODO or phase label.
- Proposed/future capability must never be described as current production behavior.
- Security, financial, tenancy, and accountability changes require explicit evidence and focused tests.
- UI documentation must not introduce presentation-side financial calculations.

## Skills versus documentation

`.agents/skills/` contains procedural workflows such as financial-rule review, report reconciliation, release verification, interface review, and security review.

A skill answers **how to perform a class of work**. A canonical document answers **what StallTrack is, what a rule means, or what constraints apply**.

Skills never override this documentation hierarchy.
