# ADR-007 — Source-native collection identity and Business Payor retirement

**Status:** Accepted  
**Date:** 2026-10-06  
**Owners:** Clint / StallTrack V3  
**Business evidence:** [2026-10-06 MEEDO office clarification](../evidence/2026-10-06_meedo_office_terminal_fish_vendor_monthly_income_clarification.md)  
**Supersedes:** ADR-001 as the target product identity architecture. Historical ADR-001 remains useful evidence for why name matching is unsafe.

## Context

ADR-001 introduced a tenant-scoped canonical Business Payor so StallTrack could safely associate one person/organization with multiple operational relationships without using name matching.

The 2026-10-06 office clarification changes the product requirement. MEEDO does not require a separate Business Payor management workflow to collect ordinary revenue. The office works from source-owned records such as stall/space occupants, vendor registrations, utility accounts and direct one-off transactions.

The Business Payor page and manual-link prerequisite add office work without representing a required business artifact.

## Decision

1. **Source-owned identity is authoritative for collection eligibility.**
   - NPM uses its occupancy/stall source.
   - Monthly rentals use their renter/occupancy/account source.
   - Fish/Meat uses an independent vendor-registration source.
   - Weight & Measure uses that registered vendor identity.
   - Utilities use their own source/account identity.
   - Other specialized operations use their own approved source identity.

2. **Unified search is cross-source search, not a Business Payor master lookup.**
   Search may find several source records with the same displayed name. Equal text does not prove one identity and must not merge records automatically.

3. **The Business Payors product workflow is retired.**
   The target product removes the Business Payors page, routine manual linking and “Needs Payor” as a normal prerequisite where source-owned identity is sufficient.

4. **Direct/one-off collections remain valid without a persistent identity.**
   Where an operation allows optional/free-text payer context, the posted Collection freezes the entered payer/reference snapshot. That text does not create a permanent identity.

5. **Relationship-backed operations cannot use the direct fallback.**
   Rent, utilities, Weight & Measure and other source-backed charges require the source relationship their domain defines.

6. **Unified New Collection is source-aware.**
   After selecting a source-native search result, the server returns only the operations/choices that are genuinely eligible for that source. The client must not display the entire operation catalog and infer eligibility itself.

7. **Financial posting remains source-owned.**
   Unified/itemized collection is orchestration only. Every item delegates to the existing approved writer and retains its own classification, Collection and SRC.

8. **Existing BusinessPayor persistence may remain temporarily for compatibility.**
   This ADR removes its target product authority immediately, but physical table/entity deletion is deferred until callers, foreign keys, reports, queues, migrations and replay contracts are migrated safely.

9. **Historical evidence is preserved.**
   Existing BusinessPayor links and posted collections are not erased merely because the future workflow no longer needs the concept.

## Consequences

### Positive

- Daily collection follows the records MEEDO actually maintains.
- Fish/Meat vendors no longer need artificial NPM/BusinessPayor linkage.
- New Collection can offer relevant charges automatically without showing unrelated operations.
- Direct high-volume sources remain low-friction.
- Name matching remains prohibited.

### Trade-offs

- Cross-source search can return multiple records for the same displayed name.
- Compatibility code may carry BusinessPayor IDs for a transition period.
- Removing the persistence model requires a later dependency audit/migration.

## Required implementation sequence

1. Add source-native search/identity contracts.
2. Convert Fish/Meat, Weight & Measure and other affected workflows.
3. Update unified New Collection to consume source-native results.
4. Remove Business Payor navigation/manual-linking workflows.
5. Stop new feature dependencies on BusinessPayor.
6. Audit and remove persistence only when safe.

## Verification

The completed target must prove:

- a Fish/Meat fee can be collected without NPM or BusinessPayor linkage;
- Weight & Measure requires a registered vendor;
- an NPM/Kanmanggay/rental search result exposes only source-valid charges;
- a one-off direct source can post with payer snapshot only where policy permits;
- equal names are never auto-merged;
- itemized posting still returns separate Collection/SRC outcomes;
- existing historical collections remain unchanged.

## Relationship to ADR-001

ADR-001 remains historical architecture evidence and its safety rule against name-based identity merging still applies. Its requirement for a canonical Business Payor as the normal cross-operation identity is superseded by this source-native model.
