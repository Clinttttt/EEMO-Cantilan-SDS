# ADR-006 — Governed Configurable Service Operations

**Status:** CONFIRMED STALLTRACK ARCHITECTURE  
**Decision ID:** IA-044  
**Date:** 2026-09-27  
**Scope:** StallTrack V2 Operations, configurable EEMO services, Web administration, Collector Mobile, and canonical collection integration.

## 1. Decision

StallTrack may represent a locally defined EEMO revenue/service operation through a **governed configurable service** when the workflow is structurally simple and does not require a specialized domain lifecycle.

Configuration may define approved operational and financial policy, but it must never become unrestricted free-form collection entry.

A configured service may feed the same canonical Collection, CollectionLine, AccountableDocument, PostingOperation, reporting, and correction architecture used by specialized sources.

This decision resolves how StallTrack handles an operation whose exact local policy is not yet known. It does **not** invent the missing LGU business rule.

## 2. Specialized versus configurable

Use a specialized source domain when the operation has material assessment, regulatory, lifecycle, reconciliation, or domain-specific behavior.

Examples include permanent tenancy, NPM, utilities, slaughterhouse, transportation/parking, Tabo, and other operations whose source facts require dedicated logic.

Use a governed configurable service when the operation can be represented safely by approved configuration plus captured transaction facts.

A configurable service is not permanently generic. It may later be promoted to a specialized source domain without rewriting historical posted Collections.
## 3. Required governed configuration

Before a configurable service may produce money, its active policy must provide the applicable required facts, including:

- stable tenant-scoped service identity and display name;
- approved Revenue Classification;
- effective-dated OR/CT instrument policy;
- approved calculation basis, such as Fixed, Per Unit, Quantity × Rate, Weight × Rate, or another explicitly supported basis;
- approved effective-dated rate or controlled amount rule;
- Payor requirement: required, optional, or permitted anonymous context;
- supported collection channels, such as Web Office and Collector Mobile;
- required operational fields and references;
- active/effective state.

Collectors and ordinary collection staff do not create these rules during a transaction.

Changing future configuration must not recalculate historical posted transactions. Posted Collection Lines retain the applicable classification/policy and calculation/source snapshot.

## 4. Setup lifecycle

A configurable operation uses three practical setup states:

- **Setup Required** — visible to authorized Web office setup/directory surfaces, but incomplete and unable to post financial Collections.
- **Active** — complete approved policy exists and the operation may be used only through permitted channels and authority.
- **Disabled** — previously configured but intentionally unavailable for new transactions; historical evidence remains intact.

The Operations directory may show an incomplete operation such as **Transfer Large Cattle — Setup Required** without pretending that its rate or receipt instrument is already known.

An incomplete operation must never post a Collection.
## 5. Transfer Large Cattle

Transfer Large Cattle is the first explicit example of this pattern.

The V2 system may provide its operation shell, directory entry, transaction register, and configuration boundary now.

The following Cantilan-specific business facts remain pending EEMO confirmation:

- the exact activity/event that triggers the charge;
- the approved fee or calculation basis;
- whether the accountable instrument is Official Receipt or Cash Ticket;
- any mandatory regulatory/reference fields beyond the currently known transaction concept.

Until those facts are configured from approved office policy, Transfer Large Cattle remains **Setup Required** and is not collectible.

If later evidence shows that cattle transfer requires a materially specialized approval, certification, animal-registration, or lifecycle model, promote it to a specialized source domain while preserving the shared collection architecture.

## 6. Collector Mobile

Collector Mobile remains focused and assignment-driven.

Mobile renders only operations that are:

1. Active;
2. allowed for the Mobile channel;
3. authorized/assigned to the collector; and
4. compatible with the collector's accountable-document custody where a physical OR/CT is required.

A collector records transaction facts. StallTrack resolves classification, instrument, rate/calculation policy, and document requirements from the specialized source or approved tenant configuration.

Mobile must never expose a generic form that lets a collector invent a charge name, arbitrary financial line, rate, or OR/CT choice.
For a configurable service, the Mobile form is generated from the approved operation definition only to the extent supported by the governed field model.

If Mobile collection is not permitted, the operation remains Web Office only.

If setup is incomplete, ordinary collectors should not receive a collectible action. Head/Admin Web may still see the Setup Required state.

Offline Mobile continues to follow durable ClientOperationId, assigned accountable-form consumption, sync/reconciliation, and no-document-reuse rules already approved for canonical collections.

## 7. Channel and authority boundary

Head/Admin may manage governed operation configuration only within existing configuration authority.

Collector authority is transactional, not configurational.

Payor Online support is separate and must be explicitly enabled and designed; it is not implied by Web or Mobile activation.

Channel enablement never overrides tenant isolation, source eligibility, settlement authority, accountable-form custody, or canonical posting rules.

## 8. No arbitrary financial lines

This ADR does not weaken the approved no-arbitrary-lines rule.

Every posted line must still resolve to an approved Revenue Classification / charge definition and effective policy.

Optional remarks and descriptive transaction detail may explain a charge but do not create a new revenue identity or price.

## 9. Historical and reporting behavior

Posted configurable-service transactions preserve their service identity, classification, calculation basis, quantity/rate inputs where applicable, effective policy, source detail, business date, and accountable document.

Reports derive from posted classified Collection Lines. A service label or Operations-directory grouping does not replace revenue classification.

A future promotion from configurable service to specialized source domain does not rewrite prior posted history.

## 10. Consequence

The exact Cantilan policy for Transfer Large Cattle remains a business question, but the StallTrack handling of that uncertainty is now resolved: use governed setup, block incomplete financial posting, and reuse canonical collection infrastructure when activated.
