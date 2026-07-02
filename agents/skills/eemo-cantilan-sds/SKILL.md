---
name: eemo-cantilan-sds
description: Work safely in the EEMO Cantilan SDS revenue collection repository. Use when Codex is asked to inspect, implement, refactor, review, test, debug, cache, optimize, or document anything in this codebase, including Clean Architecture/CQRS handlers, EF Core repositories, Blazor admin/payor UI, typed HttpClients, MAUI mobile/offline sync, reports, online payments, audit, deployment, or financial collection workflows.
---

# EEMO Cantilan SDS

## First Moves

1. Confirm the working directory is the repo root: `C:\dev\EEMOCantilanSDS`.
2. Read `agents/CODEX_AGENTS.md` for the current role and guardrails.
3. Check `git status --short`; preserve unrelated dirty work.
4. Read the task-relevant source files before proposing or editing code.
5. Treat current source as authoritative when older docs conflict with it.

## Core Context

This is a municipal revenue collection system for Cantilan EEMO. It manages
collections, OR traceability, delinquency, contracts, reports, audit, online
payments, payor portal flows, and collector mobile workflows across eight
facilities.

Use these references only when relevant:

- Read `references/system-overview.md` for facilities, roles, business rules, and current module map.
- Read `references/implementation-patterns.md` before implementing or reviewing code.
- Read `references/verification.md` before final validation, deployment support, or release guidance.

Also consult the source knowledge files when working on substantial changes:

- `.amazonq/context/knowledge/arch-rules.md`
- `.amazonq/context/knowledge/patterns.md`
- `.amazonq/context/knowledge/ARCHITECTURE_DOCUMENTATION.md`
- `.amazonq/context/knowledge/EEMO_Complete_Documentation.md`
- `.kiro/steering/CONTEXT.md`

## Hard Rules

- Keep Clean Architecture boundaries intact.
- Keep business rules in Domain or canonical repository/report logic, not Razor components or controllers.
- Use repositories and `IUnitOfWork`; do not inject EF contexts into handlers.
- Use MediatR commands/queries, `Result<T>`, and FluentValidation.
- Keep controllers thin: bind, `Sender.Send`, `HandleResponse`.
- Use typed API clients from `EEMOCantilanSDS.HttpClients`; do not inject raw `HttpClient` in UI.
- Never auto-generate OR numbers.
- Never accept collector attribution from request payloads.
- Use `PhilippineTime` for business-day logic and UTC for persisted instants.
- Do not cache failed responses, auth flows, write results, payment gateway mutations, or webhooks.
- Add or update tests for financial logic, reports, caching, mobile sync, validation, and regressions.

## Task Workflow

For implementation:

1. Find a similar existing feature and mirror its wiring.
2. Trace the full path: UI/mobile -> typed API client -> controller -> handler -> repository -> domain/DB.
3. Keep DTO shape consistent across Application, API clients, Blazor, and Mobile.
4. Invalidate caches after successful writes, not before.
5. Run targeted tests/builds and clean temporary artifacts safely.

For reviews:

1. Lead with findings, ordered by severity.
2. Verify financial/report math and date scopes before style concerns.
3. Check architecture, validation, auth, cache invalidation, mobile sync idempotency, and test gaps.
4. If no issues are found, say so and state residual risk.

For UI:

1. Use existing design tokens and component-scoped CSS.
2. Build operational surfaces, not marketing pages.
3. Keep loading, empty, error, and success states.
4. Show page shells/skeletons promptly on heavy routed pages.

## Current Reality Notes

Older docs may say Mobile and online payments are future phases. Current source
already includes:

- Payor auth/portal and PayMongo-backed online payment lifecycle.
- API-side `IMemoryCache` abstractions and region invalidation.
- MAUI collector app with Mobile.Core offline queue/sync logic.
- Shared `EEMOCantilanSDS.HttpClients` package.
- Audit interceptor coverage for financial and account/payor/stall changes.

Verify current files before relying on old status text.
