# Governed fine assessments — financial design review

Status: **proposal only; persistence and settlement are not approved or implemented**.
Received product direction: project-owner prompt, 2026-10-10. Fine assessments should be capable of being saved independently before receipt of payment and displayed as outstanding account-linked charges. Posting collected revenue remains a separate authorized Collection operation.

This direction does not approve automatic late fees, assessment permissions, waiver/backdating policies, or a new settlement authority. Follow [ADR-007](../decisions/ADR_007_SOURCE_NATIVE_COLLECTION_IDENTITY.md), the operational rulebook and existing Collection correction contracts. No production or local migration was created or applied by this work.

## Existing implementation evidence

- `Application/Common/Revenue/PenaltyDefinitionWorkflow.cs` and `Domain/Entities/Revenue/PenaltyDefinition.cs`: Head-governed versioned definitions, effective dates, fixed/direct-approved amounts and maximum validation. Reuse these; do not create another penalty catalog.
- `Application/Common/Revenue/CollectionComposerWorkflow.cs`, `AddPenaltyDraftLineRequest`: an OR draft must already contain an eligible non-penalty rental/ECF line. The penalty source ID denotes the approved **definition version**, not an assessed account debt. Origin is descriptive text, not a verified account foreign key. Draft revision and duplicate-code checks exist. Legacy/Pending settlement authorities cannot be bypassed.
- `Application/Dtos/Revenue/PenaltyDtos.cs`, `PenaltyRegisterRowDto`: posted fine collections, SRC and correction status. These rows are not independent unpaid assessments.
- `CollectionSourceKind.PenaltyDefinition` cannot be silently repurposed as a FineAssessment identity. A separately approved typed source/settlement adapter is necessary.
- Current activity reads do not expose every allocation of each line. Full CollectionLine money must not be represented as one period's settlement. Current period reads remain the authoritative totals.

## Proposed persistence and contracts

Prefer an additive `FineAssessment` obligation module beside existing source-owned obligations, using the existing definition/version engine. It must not duplicate monthly rental assessment generation.

Proposed assessment data:

| Evidence | Proposed representation |
|---|---|
| Identity | TenantId, stable assessment ID, client operation ID, request fingerprint, concurrency version |
| Account | Typed source kind + source ID; verified tenant-owned account/occupancy or event-lot identity; optional actual obligation-period ID |
| Snapshot | Recorded payor name, unit reference; never a master Business Payor dependency or same-name association |
| Approved basis | Definition-version ID, frozen calculation basis, fixed/direct-approved amount and ceiling evidence, currency/precision |
| Assessment | Business date, reason, documentary reference where required, authorized actor, recorded timestamp |
| Lifecycle | Approved assessment evidence; non-destructive adjustment/reversal events with reasons and correlation IDs |
| Settlement | Canonical CollectionLine/allocation references, exact allocated amounts and correction chain; authoritative remaining projection |

Use referential integrity to the penalty version. Polymorphic account references require a closed typed adapter registry that verifies existence, tenant, lifecycle eligibility and allowed penalty source before any write. Display text `AppliesTo` alone does not authorize a relationship. Whether an assessment also needs a separate approval entity is an office decision.

Proposed API shapes, **not existing endpoints**:

- `GET /api/fine-assessments?sourceKind=...&sourceId=...`: verified assessment register and authoritative position.
- `POST /api/fine-assessments`: typed account/period, penalty version, proposed amount when permitted, business date, reason/evidence, ClientOperationId. Resolve/freeze on server; reject mismatched fixed amounts and invalid versions.
- `GET /api/fine-assessments/{id}/quote`: remaining balance, concurrency version, permitted instrument/settlement capability and reason when unavailable.
- Existing Composer extension: an assessment-settlement draft line request containing assessment ID, quote version, permitted amount, draft expected revision. Do **not** call the existing “new penalty charge” request for an already assessed fine.
- Separate audited approval/reversal commands only if the office accepts those states and permissions. No physical DELETE.

Read DTOs should separate Assessed, Settled, Remaining, state, approved basis and actual settlement allocations. The account-review read joins only verified matching source IDs, never payor names. Fine remaining must remain separate from rental remaining.

## Lifecycle and posting

Proposed conceptual lifecycle: assessed outstanding → partially settled (only if permitted) → settled. Reversal/waiver/amendment paths require policy approval; do not expose unsupported statuses. A separate pending-approval state is conditional, not an invented current office procedure.

1. Assessment creation freezes authorized evidence, writes audit and idempotency result atomically. It creates **no Collection, SRC, receipt or revenue**.
2. Payment discovery selects an actual assessment, resolves the remaining amount and permitted account/source context.
3. Composer obtains an assessment-specific settlement claim; existing OR/CT compatibility, eligible source/context and canonical settlement-authority rules still apply.
4. Posting locks or concurrency-checks the assessment/claim, rejects overpayment or stale/reversed/settled debt, and commits canonical Collection plus allocation exactly once.
5. Settled/remaining projections derive from valid canonical allocations and approved corrections, not editable cash counters.
6. Collection reversal/replacement preserves the original record and adjusts effective settlement through the existing correction model. Assessment cancellation must not erase previously posted money or implicitly reverse cash.

Unique `(TenantId, ClientOperationId)` plus a request fingerprint should replay identical requests and reject reused keys with different payloads. Assessments need a separate duplicate-business-evidence policy; an idempotency key alone does not prevent intentional duplicate assessments. Settlement concurrency must be transactional across draft revision, claim and posting; UI disabled buttons are insufficient.

## Exact MEEDO decisions required

Safe defaults below are recommendations for review, **not confirmed rules**.

| Decision | Proposed safe default until accepted |
|---|---|
| Who assesses? | No new write permissions; propose Head-only initially, with server authorization and audit |
| Separate approval? | Do not invent two-stage approval; require explicit office answer |
| Eligible sources/penalties? | Explicit typed whitelist per approved version/policy; no display-name or name matching |
| Closed account assessments? | Block new assessments until liability and notice policy are confirmed; preserve existing debt |
| Backdated/future assessment? | Current Philippine business date only until approved effective-date/correction rules exist |
| Partial payment? | Full remaining settlement only until office authorizes partial allocation |
| Rental and fine on one OR? | Only existing compatible canonical source/context rules; separately approve standalone assessed-fine settlement eligibility |
| Waive/cancel/amend? | No destructive deletion; no new action until approver, evidence and cash effects are settled |
| Closure/transfer liability? | Preserve original source/occupant-period liability; never transfer to a successor automatically |
| Notice/documentary evidence? | Confirm required office notice/reference, issuance and approval evidence |
| Direct-approved amount? | Approved version/ceiling and an authorized amount decision; no unrestricted custom charge |
| Legacy collected fines? | Retain posted register unchanged; no fabricated assessment backfill or duplicate debt |

Also confirm duplicate assessment rules for one offense/period and how an assessment reversal interacts with already collected money. Confirm arrears qualification and recovery for each source separately: delinquency is a condition, not a new rental debt or automatic ARREARS reclassification.

## Migration, compatibility and rollout

An approved implementation will require an **additive migration** for assessments, evidence/audit, idempotency and settlement linkage/indexes, and possibly a typed eligible-source policy. Exact EF schema and indexes must be reviewed after the decisions above. No existing Stall, ObligationAccount, Collection, SRC or penalty-version rows should be rewritten. Historical unknowns remain unknown. Legacy collected fines continue in Collected; assessments start prospectively without guessed backfill.

Mobile/API compatibility requires versioned capability discovery and an assessment quote/selection path, preserving ClientOperationId offline retry behavior. Do not let older clients treat an assessment ID as a penalty-version ID. Legacy/Pending sources remain ineligible until their own canonical authority is approved.

Enable read/setup first behind capability flags; then assessment creation, then separately verified settlement. Roll back by disabling new actions/capabilities, never deleting assessment or Collection history or downgrading a production schema. Production migration/deployment requires separate approval.

Monthly Income and remittance include **actual collected cash only**. Assessment/outstanding views are non-cash and explicitly separate. Qualifying ARREARS recovery retains approved origin/classification; an assessed fine remains a separate fine obligation, not additional rental principal.

## Required verification before approval to ship

Real PostgreSQL tests: tenant/role/source rejection; fixed/version/date/ceiling validation; replay/fingerprint mismatch; business duplicate rules; concurrent creation and settlement; permitted partial/full payment; overpayment; settled/reversed assessments; canonical/Legacy authority restrictions; OR/CT and context compatibility; correction chains; audit; historical unchanged records; closure/transfer policies. Reporting tests must prove unpaid assessments contribute no cash, Monthly Income or remittance, and no balance is counted twice.

Phase 3 is **not authorized** by this proposal. Request explicit acceptance of the contract, office decisions and environment-specific migration plan before implementing persistence or settlement.

## Account-review presentation follow-up

The project owner reaffirmed the saved-before-payment direction in the account-review polish follow-up. Operations already provides Others → Arrears & Delinquency / Fines & Penalties after Slaughterhouse; this is navigation only, not a revenue reclassification.

The review remains the staging surface for existing source-authorized actions. It now separates the period ledger, account information and Other assessed charges. Until an assessment read exists, that section states that saved fine assessments are not supported; it must not claim a verified zero or “no saved assessments” from an absent API. No Assess fine or Add follow-up note writer is introduced by presentation work.

After financial-contract approval, Fines account selection should reuse verified source-native discovery for eligible Stall Rent, Space Rent and other expressly authorized registrations. Selecting a name alone cannot establish liability. The future Assessments view and account review must use the same assessment ID/read projection; Collected and Penalty setup retain their existing meaning. Follow-up notes require a separately verified non-financial audit contract. The office decisions, settlement-allocation contract and migration gates above remain open.
