# Mobile itemized collection backend

Implementation scope: backend, shared contracts and durable Mobile.Core queue only. This feature branch starts at
`19ae118b`; it is not a deployed release or permission to activate a source. The approved request permits this bounded
source rollout. Older October 4 checkpoint statements that all Mobile writers still require OR entry are superseded by
the later Phase 2.x code/tests; this feature does not restore those writers.

## Purpose and money boundaries

One collector checkout receives several operation items for one authoritative payer context. A session is **not revenue**,
an operation, an obligation, a remittance or another receipt number. Every child keeps its source, classification,
effective policy, instrument, amount rules and allocation evidence.

`CollectionSessionWorkflow` preflights and delegates through `ICollectionSessionSources`. The Infrastructure dispatcher
calls the existing `WcfCollectionWorkflow`, `GovernedServiceWorkflow` and `CollectionComposerWorkflow` Mobile writers.
ECF facts and obligation allocation reuse the composer/source rules; no second calculation engine is introduced.

V1 preserves each writer's strict existing boundary: **one item produces one Collection/SRC**. Instrument totals describe
the checkout, not a promise to merge CT items. OR and CT never share a child Collection. Existing SRC generation,
reporting readers and remittance calculations are unchanged. Only canonical children count as money.

## Payer and authorization

Collector role, active collector, tenant, source assignment, governed enablement, date and policy are revalidated by the
orchestrator and original source workflows. Discovery reuses the existing Mobile menu and operation-capability queries.
Water, electricity and monthly vendor accounts require the session PayorId to equal the authoritative period/account link.
Names never establish identity. Missing links require office review. Anonymous sessions permit only existing payer-optional
governed services. Their writer retains its existing payer snapshot behavior; the session does not invent source ownership.

## Quote, commit and replay

Routes under `api/mobile/collection-session`, Collector-only:

- `GET eligible?payorId=...`: capabilities, unavailable reasons, current-month payer-linked WCF sources, payer-linked ECF
  quotes, Fish/Meat obligation periods and typed governed terms/modes/options. Legacy/uncollectible source facts may be shown,
  but are not eligible to record. A malformed ECF bill is isolated from valid bills. Existing source APIs remain usable for
  other supported billing periods.
- `POST quote`: `CollectionSessionIntent`; source-specific facts and collector-confirmed amounts. Response contains
  normalized item amounts, instruments, boundary GroupIds, instrument totals, grand total, problems and QuoteFingerprint.
  Quote creates no draft, assessment, PostingOperation or Collection. Fixed/prepared/balance facts come from server rules;
  permitted partial/direct amounts retain original validation. A changed prepared/fixed amount is returned for review.
- `POST record`: `RecordCollectionSessionRequest(Intent, QuoteFingerprint)`. All items are re-quoted before any child is
  posted. An absent/stale fingerprint returns NeedsReview. Reusing a recorded session ID with changed intent returns HTTP
  409 with typed SessionIntentConflict; no new money. Other review problems use the typed result, not text parsing.
- `GET {clientSessionId}`: original completed outcome for the same tenant/collector, with current Posted/Voided/Corrected
  disposition. It never creates a replacement Collection.

Keep ClientCollectionSessionId and every ClientItemId stable across retries/reordering. Intent hashing includes payer,
business date, item/source identities, mode, source-specific facts and unrounded amount intent, excluding display labels.
Child ClientOperationIds derive deterministically from tenant + session + item using a versioned SHA-256 namespace.
Quote fingerprints additionally cover authoritative source balance/version, setting/rate/policy identities and amounts.
Do not calculate GroupIds or SRCs on the device.

## Atomicity and non-financial storage

All V1 writers and `CollectionSessionStore` use the **same concrete AppDbContext** inside one Serializable transaction.
Source workflows are constructed on that context deliberately; the separate existing IAppDbContext DI registration is not
assumed to share a connection. Every child save, assessment, PostingOperation and completed-session result commits together.
Failure after a child save rolls everything back. Serialization conflicts return a winning recorded outcome when available,
otherwise a retryable 503. Sequence gaps on rollback are valid; SRCs are never renumbered.

Additive migration `20261005031824_MobileCollectionSessions` adds only orchestration metadata: tenant/session unique key,
collector/payer/date, intent fingerprint and result JSON containing child references. PostingOperation has one CollectionId
and cannot alone retain a multi-child checkout result. No quote/pending/partial server ledger is needed with true atomicity.
Tenant export/restore includes this correlation table so restored retries remain deterministic. It has no classification,
assessment, remittance or income reader. It is applied only to throwaway integration databases during validation.

## Supported sources

| Source | Existing authority and boundary |
| --- | --- |
| WCF | Existing enabled WCF writer; prepared remaining amount wins; approved direct collection for business month; legacy settlement requires office cutover |
| Landing/Berthing | Existing governed service terms and writer |
| Market Fees | Existing governed service; stable configured FeeOptionId when option-based, never label identity |
| Transportation | Existing governed service with approved vehicle class/rate |
| Transfer Large Cattle | Existing governed service and approved choices |
| Vegetable/Fruits | Existing governed WholePayment/DailyTransaction mode and contextual instrument; no new rental engine |
| ECF | Existing composer Mobile writer, canonical-authority bill only |
| Fish/Meat Vendor Fee | Existing monthly ObligationAccount Mobile writer; partial payment within authoritative remaining balance |

## Explicitly unavailable in V1

| Source | Exact adapter gate; existing standalone workflow remains intact |
| --- | --- |
| Kanmanggay / Fiesta-Araw | Web obligation accounts exist, but no existing Collector operation assignment/capability and authorized Mobile obligation writer for these kinds; do not invent permissions |
| NPM Daily | Day-state command/repository/poster needs dedicated read-only preflight and shared-transaction adapter |
| NPM Whole payment | Accepted monthly quote/settlement owns a separate Serializable transaction; cannot nest it in this checkout |
| Tabo | Vendor registration and vendor-day duplicate preflight need a read-only adapter before mutation |
| Slaughterhouse | Specialized private transaction calculator needs a read-only preflight adapter; do not merge slaughter transactions |
| Other facility rent / future sources | No implemented session adapter; canonical readiness alone does not grant basket support |

## Mobile.Core handoff

No UI is included. `IMobileApiClient` exposes discovery, quote, record and reconcile. `MobileSyncService.EnqueueCollectionSessionAsync`
durably stores one `ItemizedCollectionSession` operation with the exact RecordCollectionSessionRequest. Owner scoping and existing
queue status rules remain in force. Persist session/item IDs, facts, date, allowed amount intent and the reviewed fingerprint;
no OR/CT serial or device SRC. Use the server-issued session/menu business date when available, with the established device
fallback only when unavailable. The display total in PendingOperation is not server financial authority.

Unknown network response retries the same request. Success retains **all** child Collections/SRCs in CollectionSessionResult;
do not read the old single-Collection sync fields as this session's outcome. Source/quote changes enter ReconciliationRequired
and stop automatic retry. Re-quote and obtain collector confirmation using the existing review/discard/new-intent queue pattern;
never silently substitute a new fingerprint or amount. An entirely offline unquoted basket may be saved, but cannot post until
online quote/review has occurred. A posted session cannot be edited; corrections use existing canonical void/reversal rules.

Claude must not assume one checkout = one Collection, same instrument = one SRC, all canonical sources are supported,
display-name equality establishes a payer, cached eligibility authorizes money, or a quote is permanently valid.

## Validation checkpoint

- PostgreSQL: 114 relevant tests passed, including 12 new checkout scenarios: mixed instruments/classifications,
  replay/reordering/concurrency, payer/tenant/assignment guards, stale balances/rates, configured fee identity,
  prepared/direct WCF, canonical-only ECF, malformed-bill isolation, atomic rollback, voided replay, reports and remittance.
- Rollback test proved red with the outer transaction removed (one child survived), then green after restoration.
  Intent precision test likewise proved red before removing centavo-rounding from fingerprint normalization.
- Full unit suite: 2,504 passed; one inherited EF allowlist failure for baseline `BusinessPayorWorkflow` and
  `EcfActivationWorkflow`. Neither those files nor the architecture allowlist changed in this feature.
- Component suite: 691 passed; one inherited terminology test forbids the existing baseline `/payors` route.
  No Web/Mobile screens, navigation or component tests changed.
- API, Web, Mobile.Core and Windows Mobile Release builds passed. EF reports no pending model changes.
  Migration applied only by the throwaway PostgreSQL fixture. Android was not built; no application version changed.

These two inherited test gates remain visible follow-up work; they are not reported as green or bypassed.
