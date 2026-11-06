# Web Office Audit Trail — isolated Stage 1 improvement (2026-10-27)

**Scope:** Head-only Web Office Audit Trail, EF audit interceptor, filtered event details.
**Worktree:** `C:\dev\stalltrack\v18-sidebar-navigation-render-20261027`
**Release state:** isolated local work. No push, merge, production deployment, schema migration or financial rewrite.

## What changed
- Suppress only routine collector `LastActiveAt` login bookkeeping; preserve true account modifications and MFA/security changes as auditable.
- Redact sensitive credentials and gateway artifacts in **new** EF audit snapshots (`CheckoutUrl`, `RawPayload`, secrets, tokens, passwords, encrypted MFA material). Do not discard legitimate configured rate or fish-declaration evidence.
- Centralize the audit repository's tenant and historical foreign-record exclusion policy so list and single-event detail use identical restrictions.
- Add Head-only `GET /api/Audit/trail/{eventId}`. Return only an approved detail DTO; do not expose raw JSON or cross-tenant events.
- Reuse the existing `AuditDetailComposer` allowlist: four changes for the list summary, all permitted changes for the individual detail view.
- Add a compact two-line row and keyboard-accessible details dialog; show recorded timestamp (PH), actor, action, record identity and audit event ID. Missing evidence is never invented.
- Explain custom action counts, label the existing CSV capability as **Export page** rather than implying a full-range export.

## Verification completed before handoff
- API and Client Debug builds: **0 errors**.
- Focused audit tests: **21 passed, 0 failed** (including four new tests).
- Full backend unit suite: **3,273 passed, 0 failed**.
- Full Web Component suite: **1,507 passed, 0 failed**.
- Dedicated tests cover login housekeeping vs genuine edits; gateway URL/payload redaction; reviewed-field allowlisting; event detail absence for another tenant.
- Interactive authenticated browser review remains outstanding; compilation and HTTP smoke responses cannot prove animation, focus trapping, or subjective usability.

## Deliberately not in Stage 1
- No backfill, deletion, or rewriting of historical `AuditLog` rows; historical raw JSON remains in storage and requires separately approved retention/redaction policy. Its raw contents are **never returned** by the new details endpoint.
- No fake attribution of `system`/legacy events to the most recent user. The UI marks these as automated/unattributed and not independently verified.
- No new actor-correlation/request-IP scheme, transaction-level event grouping, or change to authoritative Collection, remittance, SRC, OR/CT, or Monthly Income records.
- No unified historical evidence projection for `OfficialReportRevision` or rental lifecycle; those require read-only linked adapters and explicit finance-domain tests.
- No claim of audit coverage for every operational action, external webhook, payor action, security event, or Platform onboarding action.
- Existing CSV remains **current-page only**; a full-range export requires a separate bounded, tenant-filtered streaming design.

## Stage 2 / Stage 3 follow-up gates
1. Inventory authoritative evidence sources, reference IDs and actors for Collections/SRC, corrections, turnovers/remittances, Online Payment and Monthly Income revisions.
2. Design linked activity projections without copying or mutating those financial records, then verify tenant boundary and idempotent correlation.
3. Expand decisions coverage (contract/space lifecycle, payor linking, onboarding, permissions, credentials), explicitly distinguishing human initiation, background processing and unknown legacy identity.
4. Define retention rules, field-level privacy review, secure full filtered export, request origin/correlation provenance, immutable storage enforcement and negative authorization tests.
5. Visual acceptance of event dialog, mobile sizing, scroll/focus interactions and authenticated list-to-details navigation before any production release.
