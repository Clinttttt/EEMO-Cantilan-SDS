# ADR-003 — Durable posting operation identity

**Status:** Accepted

**Date:** 2026-09-26

**Owners:** Clint; MASTER / V2 Planner (sole implementation owner). N/O/P/Q are paused candidate workstreams whose partial work remains review evidence only.

**Related decisions/issues:** [IA-041 / Q44](DECISION_REGISTRY.md#ia-041--durable-posting-operation-identity); [Q42/Q43 cutover](ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md)

## Context

**CURRENT IMPLEMENTATION:** Mobile sync searches source-row `ClientOperationId` fields and returns an already-synced result without comparing intent. `PaymentRecord` and `UtilityBill` allow their stored key to be replaced. Dormant Collection persistence has a global unique ClientOperationId index. N's paused candidate returns a prior Collection ID without comparing changed posting inputs. These mechanisms do not implement the approved durable operation contract.

**CONFIRMED STALLTRACK DECISION:** Clint approved Q44 with the eleven requirements below. ClientOperationId identifies one attempted financial posting and is permanently bound to one normalized posting intent and durable outcome. It is not merely a duplicate-suppression flag. This defines target engineering architecture; it neither implements the registry nor approves/restarts the partial lanes.

## Decision

### 1. Tenant-scoped durable identity

TenantId + ClientOperationId identifies one durable operation record. Do not use mutable PaymentRecord, UtilityBill or other source rows as the long-term idempotency registry.

### 2. Semantic posting intent

Retain enough information to prove what the operation meant. Applicable business-significant inputs include operation type, actor/originating collector or user context, payor context, business date, source obligation/reference, revenue classification, instrument family, accountable document identity/number, line amounts, explicit allocations and relevant immutable posting inputs.

Use a deterministic normalized representation/fingerprint, not raw JSON text. Equivalent requests normalize identically despite JSON property ordering, ordering of lists where order has no business meaning, or transport-only metadata differences. Do not normalize away financially meaningful distinctions.

### 3. Same ID and same intent

For a successfully completed operation, return its recorded Collection/document outcome without executing financial effects again. A retry after a lost response creates no additional Collection, allocation or document consumption.

### 4. Same ID and different intent

Return an explicit IDEMPOTENCY CONFLICT. Never return the old success as though changed financial content succeeded. A genuinely new/different transaction needs a new operation identity and all normal validation; the bound intent under the old ID cannot be mutated.

### 5. Concurrent identical requests

Use persistence-level uniqueness and transactional coordination to ensure concurrent requests with the same tenant, key and normalized intent cause exactly one financial effect. The losing/waiting request resolves to the committed result rather than creating another Collection.

### 6. Atomic successful outcome

Commit success atomically with Collection, lines, allocations, accountable-document consumption, compatibility projections and the operation/idempotency record. Where the database transaction can prevent it, neither a success record without its financial effects nor financial effects without their durable success identity may occur.

### 7. Authorization before outcome access

The operation ID is not an authorization token. Before returning an existing outcome, enforce the appropriate tenant boundary, authentication, authorization and collector/source authority checks. Knowing another operation ID grants no access to another user's or tenant's financial result.

### 8. Reversal, void and replacement

Never delete or repurpose the original binding. It remains associated with its original Collection after reversal/void. Retrying it must not recreate the collection or post money again. Return the original outcome with its current disposition visible, such as Reversed/Void. A replacement transaction has a new operation ID and an explicit relationship to the original correction.

### 9. Independent document identity constraints

An operation ID and a physical document identity are related but not interchangeable. A different operation ID cannot bypass document uniqueness, consumption or reconciliation rules. A physically issued OR/CT remains consumed even if a later request presents a fresh key.

### 10. Failure semantics

Distinguish terminal business outcomes from infrastructure failure. If transport/database failure prevented reaching the durable operation transaction, retry the same ID. Durably accepted/committed operations retain their outcome for retries. If a durably rejected business operation requires changed financial intent, the corrected transaction uses a new ID; the old bound intent is preserved. Infrastructure failure must not be mislabeled as a durable business rejection.

### 11. Source-row keys are compatibility only

Existing mutable source ClientOperationId fields may temporarily remain for compatibility, but the durable operation registry is authoritative once this model is active. Later installments, corrections or source updates must not erase prior operation history.

## Example

Operation ABC posts WCF PHP 100 on CT #004120 as Collection C-1001. If the response is lost, an authorized retry of ABC with equivalent intent returns C-1001 and its document outcome. ABC with PHP 150 returns IDEMPOTENCY CONFLICT; the PHP 150 request was not posted. Later reversal preserves ABC -> C-1001 and exposes the current disposition. A new operation XYZ using CT #004120 still encounters that document's consumption/reconciliation rules.

## Consequences

### Positive

- Retry identity survives later source changes, installments, cutover and corrections.
- Changed requests cannot receive a misleading success acknowledgment for different financial content.
- Concurrent equivalent submissions have one financial effect and a recoverable outcome.

### Trade-offs / risks

- Durable operation persistence, semantic normalization and concurrent outcome recovery are required.
- The global Collection key index and source-row lookup behavior need an explicit compatible migration to the tenant-scoped registry contract.
- Financial fingerprint comparison does not replace authorization, document consumption checks, source validation or Q43 reconciliation.

## Compatibility and migration

MASTER owns the shared operation contract, tenant-scoped uniqueness, normalized immutable intent and transactional outcome persistence, and will review existing operation evidence without inventing missing historical intents. Web, reporting, Mobile and online consumers follow that contract as their source phases are implemented. N/O/P/Q remain permanently paused; their partial work provides candidate evidence only.

## Verification

Future acceptance checks cover equivalent serialization/list ordering, changed financial inputs, transport-only changes, cross-tenant and unauthorized outcome lookup, concurrent identical requests, lost responses, atomic rollback, durable business rejection versus outage, later source-key overwrite, reversal/replacement retries, and new-key attempts to reuse an issued document. These are future implementation criteria; no tests or runtime changes are claimed here.

Current evidence: `EEMOCantilanSDS.Infrastructure/Repositories/SyncRepository.cs`, `EEMOCantilanSDS.Application/Command/Sync/SyncOfflineCollections/SyncOfflineCollectionsCommandHandler.cs`, `EEMOCantilanSDS.Domain/Entities/Payments/PaymentRecord.cs`, `EEMOCantilanSDS.Domain/Entities/Payments/UtilityBill.cs`, and `EEMOCantilanSDS.Infrastructure/Persistence/Configuration/CollectionConfiguration.cs`.

## Supersedes / superseded by

Supersedes target reliance on a mutable source key or an unqualified duplicate flag as canonical posting idempotency. Does not claim legacy operation payloads can be reconstructed, authorize automatic historical backfill, or weaken existing tenant/document/source checks.
