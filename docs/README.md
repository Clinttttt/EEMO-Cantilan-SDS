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

Before changing code, read:

- `architecture/ARCHITECTURE_RULES.md` — non-negotiable implementation boundaries.
- `architecture/APPLICATION_PATTERNS.md` — established code shapes to copy.
- `architecture/SYSTEM_ARCHITECTURE.md` — rationale and trade-offs.
- `business/EEMO_BUSINESS_RULES.md` — accepted current business semantics.
- `business/REVENUE_ARCHITECTURE.md` — approved target revenue architecture and phased migration.
- The relevant domain-specific documents below.

## Documentation map

### Architecture

- `architecture/ARCHITECTURE_RULES.md` — layering, CQRS, tenancy, money, EF Core, auth, Blazor, testing, deployment rules.
- `architecture/APPLICATION_PATTERNS.md` — command/query/repository/controller/API-client/component/test patterns.
- `architecture/SYSTEM_ARCHITECTURE.md` — system design rationale.
- `architecture/REPOSITORY_STRUCTURE.md` — solution and folder ownership.
- `architecture/TECH_STACK.md` — frameworks, runtimes, database, deployment and local configuration.
- `architecture/ONBOARDING_FLOW.md` — municipality onboarding flow.

### Business

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
