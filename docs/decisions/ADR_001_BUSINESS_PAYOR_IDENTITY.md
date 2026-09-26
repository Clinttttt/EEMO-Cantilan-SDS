# ADR-001 — Business Payor identity independent of authentication

**Status:** Accepted

**Date:** 2026-09-26

**Owners:** Clint; MASTER / V2 Planner (sole implementation owner). N/O/P/Q are paused candidate workstreams whose concern boundaries and partial work remain review evidence only.

**Related decisions/issues:** [IA-038](DECISION_REGISTRY.md#ia-038--business-payor-identity-independent-of-authentication); MASTER itemized collections Grill Me Q41

## Context

**CURRENT IMPLEMENTATION:** `PayorUser` is an authentication identity. `PayorStallLink` associates that user with stalls for access. Existing specialized sources hold operational relationships and assessment/settlement facts. These do not establish a general business Payor independent of portal registration.

**CONFIRMED STALLTRACK DECISION:** Clint accepted Q41 with the constraints below. This is a StallTrack V2 engineering/domain decision, not a new EEMO accounting policy. Acceptance defines target semantics; it does not claim implementation or approve partial N/O/P/Q work. Clint approved the canonical baseline and MASTER's sequential implementation workflow.

## Decision

1. A Payor is the tenant-scoped canonical business identity for a person or organization when StallTrack intentionally maintains that identity across one or more operational relationships.
2. Payor identity is independent of `PayorUser` and portal activation. A person or business may have valid EEMO obligations without creating a StallTrack portal account.
3. `PayorUser` remains authentication/access identity. Its relationship to a Payor, where supported, is explicit and optional. Authentication identity is not the authoritative financial/business identity.
4. Never infer that source records belong to the same Payor solely from names/display text, spelling similarity, a phone number alone, OR/CT numbers, or other non-authoritative textual similarity. Cross-operation identity requires an explicit authoritative relationship.
5. Specialized source domains remain assessment authority. Payor introduces no independent balance, receivable, or settlement authority: it identifies who operational relationships belong to, not what they owe.
6. A Payor may have multiple approved source relationships, including applicable occupancies, rent accounts, utility relationships, and future obligation-backed operations. Use the smallest relationship model consistent with the existing domain; do not introduce unnecessary generic linking infrastructure.
7. Payor-first collection exposes only obligations derived from approved, explicitly associated source relationships. Never discover obligations by payer-name matching.
8. Permanent Payor creation is not mandatory for every transaction. Preserve anonymous, named-snapshot-only, and one-off transactions where the approved operation allows them, particularly applicable Cash Ticket workflows.
9. Posted collections preserve the payer identity/name evidence needed for historical accuracy. Later edits to a Payor master record must not rewrite the historical identity displayed for an already-posted collection/document.
10. Historical migration/backfill is conservative. Never merge historical records merely because names match. When authoritative existing relationships cannot reliably establish identity, preserve historical source evidence and leave canonical Payor linkage unresolved.
11. Payor identity and every associated relationship are tenant-scoped. Never connect them across municipalities/tenants.
12. Keep the initial implementation small. Payor is business identity, not a new accounting subsystem, generic receivable authority, assessment engine, or authentication replacement.

**TARGET DESIGN:** The shared collection contract distinguishes an optional canonical business Payor reference, historical payer evidence, and authentication/actor identity. Specialized source adapters remain responsible for authoritative operational relationships and obligations.

**OPEN DESIGN DETAILS:** Exact source relationship fields, access-link cardinality, creation/linking authorization and workflow, and migration mechanics must be established from the existing domain and remaining MASTER review. This decision does not authorize automatic linkage, a generic relationship framework, or a new balance ledger.

## Consequences

### Positive

- Office collection can serve established business relationships without requiring portal registration.
- Explicit relationships support safe payor-first collection across approved operations.
- Historical payer evidence remains stable when master data changes.

### Trade-offs / risks

- Similar names can remain separate or unresolved until authoritative linkage exists.
- Existing authentication/stall links require examination; their mere existence does not prove all historical occupancy or utility ownership.
- Every lookup, link, and collection validation must enforce tenant ownership.

### Required follow-up

- MASTER owns the minimal shared business identity and collection contracts; source domains retain their authority.
- O consumes authoritative eligible-source contracts and preserves permitted one-off payer contexts.
- P records identity evidence and unresolved linkage without speculative backfill.
- Q uses posted payer snapshots for history and explicit Payor IDs for linked queries, without name-based grouping.
- N/O/P/Q remain permanently paused as implementation sessions. MASTER proceeds sequentially. See [ACTIVE_WORKSTREAMS.md](../planning/ACTIVE_WORKSTREAMS.md#itemized-collections-single-master-implementation-governance).

## Compatibility and migration

Use an additive migration when implementation is approved. Preserve existing authentication and operational behavior until its explicit cutover. Do not require portal activation or mass-create/merge business Payors to make legacy records readable. Do not retroactively group documents or fabricate itemization. A historical record may retain source identity/name evidence with no canonical Payor link.

Adding a Payor does not transfer assessment, balance, or settlement authority from any existing source. The separately approved settlement transition is recorded in [ADR-002 / Q42](ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md).

## Verification

The approved implementation must demonstrate:

- business obligations and collection without a portal account;
- explicit multi-source association without name or phone inference;
- permitted anonymous/one-off collections without permanent Payor creation;
- immutable posted payer evidence after master-name changes;
- rejection of cross-tenant relationships and lookup leakage;
- conservative historical linkage, including equal names that remain unlinked;
- unchanged specialized assessment/settlement authority.

Current evidence: `EEMOCantilanSDS.Domain/Entities/Users/PayorUser.cs`, `EEMOCantilanSDS.Domain/Entities/Users/PayorStallLink.cs`, and `EEMOCantilanSDS.Domain/Entities/Revenue/Collection.cs`. Verification above is acceptance criteria for future implementation, not a claim that tests have run or these capabilities exist today.

## Supersedes / superseded by

Supersedes any target assumption that `PayorUserId`, portal activation, or matching payer text is sufficient canonical business identity. Does not supersede existing authentication behavior or established EEMO assessment rules.
