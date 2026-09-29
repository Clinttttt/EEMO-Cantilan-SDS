# CLAUDE.md — StallTrack backend lane

Read `AGENTS.md` first; it is the repository entry point and it wins over this file. This file adds the working
rules for the **Claude Backend** lane only.

## What StallTrack is

A multi-tenant revenue collection platform for LGU-managed economic enterprises, in production. Cantilan (EEMO,
Surigao del Sur) is the reference tenant and the accuracy baseline: a change for another municipality must never move
a Cantilan figure.

## Lane boundary

This lane owns Domain, Application, API, Infrastructure, persistence/migrations, canonical collection
infrastructure, backend authorization, tenancy, financial source/read models, backend tests, Mobile *server*
contracts and backend documentation.

It does **not** own Web Razor/CSS/components (Claude UI lane) or product/business interpretation and integration
(Core Brain). Do not edit `*.razor`, `*.razor.css`, `wwwroot` or UI docs; record frontend needs as
`FRONTEND CONTRACT FOLLOW-UP` in the handoff instead. Never touch another worktree.

## Source-of-truth order

1. Latest explicit Clint / Core Brain direction for the current task.
2. Confirmed direct Cantilan EEMO rulings (`docs/business/EEMO_OPERATIONAL_RULEBOOK.md`).
3. `docs/decisions/DECISION_REGISTRY.md` and accepted ADRs (`docs/decisions/ADR_00*.md`).
4. Canonical business docs (`docs/business/REVENUE_ARCHITECTURE.md`, `EEMO_BUSINESS_RULES.md`,
   `docs/v2/ITEMIZED_COLLECTIONS_CANONICAL_IMPLEMENTATION_BASELINE.md`).
5. Accepted current implementation and tests (evidence of what the system does now).
6. Historical/candidate branches — evidence only, never authority.

An old implementation is not a business rule; a UI label is not backend authority; another LGU's rule is not
Cantilan policy. When sources disagree, surface the contradiction in the handoff — do not silently pick one.

## Inspect before editing

Read the domain entity, the application workflow/handler, its EF configuration and the existing tests before
changing anything. Reuse existing rule holders (`IFeeRateResolver`, `RatePeriod.AsOf`, `Stall.ResolveDailyFee`,
`DomainRules` month ledger, `StallOccupancy.AnsweringForMonth`, `CanonicalCollectionPostingCoordinator`,
`PostingOperation`) instead of writing parallel logic.

## Patterns

- CQRS via MediatR: `{Action}{Entity}Command` / `Get…Query`, handler + FluentValidation validator, `Result<T>`
  (`Success`, `Failure(msg, status)`, `NotFound`, `Forbidden`). No expected-failure exceptions.
- Canonical revenue code lives in `Application/Common/Revenue/*Workflow.cs` (tenant/actor resolved inside, thin
  controllers under `Api/Controllers/Revenue`). Follow the shape of the nearest existing workflow.
- Controllers are thin: authorize, send, `HandleResponse`.
- Out-of-scope targets answer `NotFound`, not `Forbidden`.

## Financial invariants (never collapse these)

Operational source ≠ assessment/obligation ≠ Collection ≠ CollectionLine ≠ Allocation ≠ AccountableDocument ≠
Revenue Classification ≠ Report.

- A report row does not need a writer. An assessment is not cash. A physical CT does not prove a Collection.
- Specialized sources keep their authority (NPM `DailyCollection` + month ledger, `PaymentRecord`, `UtilityBill`,
  TPM, TRM, slaughter). Do not build another settlement authority beside them.
- NPM owns new Fish/Meat activity. Stall rent ≠ `FISH_MEAT_VENDOR_FEE` ≠ `WEIGHT_AND_MEASURE`. Never infer the vendor
  fee from `BaseRentalAmount`; never hard-code ₱900/₱30.
- Payor is tenant business identity; `PayorUser` is login. Never link/merge Payors by name, phone, OR/CT number or
  similar text — only explicit relationships.
- Idempotency: `TenantId + ClientOperationId` binds one immutable normalized intent (ADR-003). Same intent → return
  the durable outcome (after authorization). Changed intent → explicit conflict. A new key never bypasses document
  uniqueness or source authority.
- A physically issued OR/CT never returns to stock because posting failed — mark reconciliation required.
- Corrections are separate immutable evidence. Keep AsOf (recorded-knowledge cutoff) and LatestCorrected distinct
  (ADR-005). Do not decide official cross-period RCD treatment (IA-043 is open).
- No source activation or cutover without the Q43 reconciliation gate (ADR-002). Schema readiness ≠ activation.
- Collectors never choose classification, rate, instrument or charge identity (ADR-006).

## Tenancy

Every tenant-owned entity carries `MunicipalityId` and is filtered globally; canonical workflows also filter
explicitly by the resolved tenant. `IgnoreQueryFilters()` needs a written reason. New tables need same-tenant
composite FKs, tenant-scoped uniqueness, and registration in export/backup/restore coverage.

## Persistence

Migrations are additive only. Never edit or delete an existing migration. After any model change run
`dotnet ef migrations has-pending-model-changes`. Prove relational behaviour with PostgreSQL/Testcontainers
(`EEMOCantilanSDS.IntegrationTests`, requires Docker).

## Tests

Money, reporting, tenancy and auth changes need a test that fails before the fix — reintroduce the defect once to
prove it. Run suites separately (see `AGENTS.md`). Never claim a suite passed that you did not run.

## Hard limits for this lane

No push, no merge, no master changes, no deployment, no production migration or data change, no APK build/publish,
no backfill, no source cutover. Local bounded commits only (one gap per commit), staged by explicit path.

## Skills

Project skills under `.claude/skills/` (procedure only; they never override the docs):

- `stalltrack-backend-engineering` — the workflow for any backend change. Use at the start of a slice.
- `stalltrack-financial-safety` — money/document/idempotency/tenancy risk review. Use before committing anything
  that touches Collections, allocations, documents, sources, reports or rates.
- `stalltrack-backend-review` — the pre-commit checklist. Use before every commit.

Repository-wide runbooks live in `.agents/skills/` (financial-rule review, report reconciliation, security review).

## Where things live

- Business rules: `docs/business/`. Decisions/ADRs: `docs/decisions/`. V2 map: `docs/v2/`.
- Current backend state and readiness matrices: `docs/planning/BACKEND_OPERATIONAL_COMPLETION_HANDOFF_20260929.md`
  (append checkpoints; do not rewrite earlier ones) and `docs/planning/CLAUDE_BACKEND_GAP_AUDIT_20260930.md`.
