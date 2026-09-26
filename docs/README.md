# StallTrack Documentation

This directory is the permanent, tool-neutral source of project knowledge for StallTrack. It is intentionally independent of Kiro, Codex, ChatGPT, IDE choice, or any other agent/runtime.

`AGENTS.md` is the concise entry point for coding agents. This directory contains the durable product, architecture, security, interface, business, testing, operational, and decision material those agents and human maintainers must follow.

## Authority and conflict handling

Use this precedence when sources disagree:

1. Explicit current EEMO/business ruling or explicitly approved task-specific decision.
2. Accepted decision record in `docs/decisions/`.
3. Accepted business/revenue architecture and business-rule documentation.
4. Security and architectural invariants.
5. Current code, migrations, tests, CI/workflows, and verified production behavior as evidence of what is implemented now.
6. Interface/design documentation for presentation and information architecture.
7. Skills/runbooks as execution guidance.
8. Historical planning notes and evidence.

Do not silently choose whichever source is easiest to implement. Surface the contradiction, identify which source is stale, and resolve it before changing financial, tenancy, authorization, or accountability behavior.

## Core reading order

Before changing V2 code, read:

- `v2/STALLTRACK_V2_MASTER_SPECIFICATION.md` — first-stop map of current production, target V2, future/hidden capability, decision gates, sidebar direction, migration strategy, and UI-preservation rules.
- when concurrent V2 sessions are active, `planning/ACTIVE_WORKSTREAMS.md` — temporary worktree/file ownership and shared-file locks. This is coordination guidance, not business authority.
- this `README.md` — authority order and canonical documentation map.
- the relevant authoritative business/architecture source for the task, especially `architecture/ARCHITECTURE_RULES.md`, `architecture/APPLICATION_PATTERNS.md`, `business/EEMO_BUSINESS_RULES.md`, and `business/REVENUE_ARCHITECTURE.md`.
- `decisions/DECISION_REGISTRY.md` before assuming a business, accountability, report, route-identity, target, remittance, or classification decision.
- current code, migrations, tests, CI/workflows, and verified production behavior as implementation evidence.
- the relevant specialized interface, security, testing, operations, or evidence documents below.

## Documentation map

### V2 orientation

- `v2/STALLTRACK_V2_MASTER_SPECIFICATION.md` — canonical V2 orientation and change-control specification. Start here; follow its links to the detailed authoritative domain documents.
- `v2/ITEMIZED_COLLECTIONS_CANONICAL_IMPLEMENTATION_BASELINE.md` — MASTER itemized collection implementation baseline prepared for Clint's approval; distinguishes accepted decisions, proposed implementation sequencing and remaining rollout gates. It does not authorize implementation while pending approval.

### Architecture

- `architecture/ARCHITECTURE_RULES.md` — layering, CQRS, tenancy, money, EF Core, auth, Blazor, testing, deployment rules.
- `architecture/APPLICATION_PATTERNS.md` — command/query/repository/controller/API-client/component/test patterns.
- `architecture/SYSTEM_ARCHITECTURE.md` — system design rationale.
- `architecture/REPOSITORY_STRUCTURE.md` — solution and folder ownership.
- `architecture/TECH_STACK.md` — frameworks, runtimes, database, deployment and local configuration.
- `architecture/ONBOARDING_FLOW.md` — municipality onboarding flow.

### Business

- `business/EEMO_OPERATIONAL_RULEBOOK.md` — direct EEMO Head/staff rulings, office-evidence reconciliations, confirmed operational rules, and the short list of genuinely unresolved business questions. Read this before asking EEMO to re-confirm workflow details.
- `business/EEMO_BUSINESS_RULES.md` — current specialized facility behavior and accepted office semantics.
- `business/REVENUE_ARCHITECTURE.md` — target collections, classifications, accountable documents, reporting, and migration phases.

### Interface

- `interface/INFORMATION_ARCHITECTURE.md` — target Web/Mobile information architecture and vocabulary.
- `interface/MIGRATION_PLAN.md` — incremental interface migration.
- `interface/DESIGN_SYSTEM.md` — reusable UI hierarchy, tokens, states, accessibility, and consistency rules.
### Security

- `security/SECURITY_ARCHITECTURE.md` — authentication, authorization, token, MFA, secrets, audit, and security invariants.
- `security/TENANT_ISOLATION.md` — municipality scoping and cross-tenant safety.

### Decisions

- `decisions/DECISION_REGISTRY.md` — confirmed facts, architecture decisions, blocked items, future capabilities, and unresolved questions.
- New long-lived architecture decisions should use ADR files in this directory when one decision deserves an independent lifecycle.

### Testing

- `testing/TESTING_STRATEGY.md` — which test layer proves which kind of behavior and required validation gates.

### Operations

- `operations/PRODUCTION_VERIFICATION.md` — production release verification checklist.
- Existing detailed operational procedures may also live as reusable skills under `.agents/skills/`.

### Planning and evidence

- `planning/ACTIVE_WORKSTREAMS.md` — active V2 session/worktree ownership, shared-file locks, integration boundaries, and completion handoff format. Temporary coordination record only.
- `planning/ITEMIZED_COLLECTIONS_PHASE3_WRITER_INVENTORY.md` — current monthly PaymentRecord settlement writers and their Phase 5 cutover readiness prerequisites; inventory only, not activation authority.
- `planning/PHASE4_WCF_WRITER_READINESS.md` — current UtilityBill Water/WCF writers, compatibility readers, Cash Ticket custody, and scoped cutover prerequisites; inventory only, not activation authority.
- `planning/IMPLEMENTATION_HISTORY.md` — historical implementation/backlog record. It is evidence, not automatic current authority.
- `evidence/` — office/reference evidence retained with the repository.
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
- Space/Stall, Occupancy/Term, Payor, and Account are different concepts.
- Current production authority is not the same as target architecture.

## Documentation change rules

- Preserve business meaning when reorganizing files.
- A move/rename is not permission to rewrite a business rule.
- Update internal links, tests, comments, workflow path filters, and skills when a canonical document moves.
- Avoid duplicating the same rule in several documents. Prefer one canonical rule and link to it.
- Historical evidence should be clearly labeled as historical.
- Proposed/future capability must never be described as current production behavior.
- Security, financial, tenancy, and accountability changes require explicit evidence and focused tests.
- UI documentation must not introduce presentation-side financial calculations.

## Skills versus documentation

`.agents/skills/` contains procedural workflows such as financial-rule review, report reconciliation, release verification, interface review, and security review.

A skill answers **how to perform a class of work**. A canonical document answers **what StallTrack is, what a rule means, or what constraints apply**.

Skills never override this documentation hierarchy.
