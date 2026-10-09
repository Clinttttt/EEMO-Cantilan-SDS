# Governed fine assessments — financial design review

Status: **proposal only; persistence and settlement are not approved or implemented**.

Governance-finalization checkpoint: 2026-10-10, checkout `C:\dev\stalltrack\eemo`, branch `codex/vegetable-fruit-space-workspace`, starting HEAD `c288ed14`. The project owner has visually accepted the Account Review presentation. Freeze that design; this review changes documentation only. The three concurrently modified coordination/release documents are excluded.
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

## Final financial-contract preparation

**READY FOR OFFICE / FINANCIAL CONTRACT APPROVAL.** This means the proposal is ready for review, not that assessment creation or settlement is authorized. The detailed contract below refines the earlier conceptual shapes; none of the proposed endpoints, entities, permissions or migrations exist as a result of this task.

### Verified architecture and authority

All paths below are relative to the repository root.

| Evidence | Observed implementation | Design consequence |
|---|---|---|
| `EEMOCantilanSDS.Domain/Entities/Users/AdminUser.cs`; `docs/security/SECURITY_ARCHITECTURE.md`; `Application/Queries/Auth/GetMfaEnrolledAccounts/GetMfaEnrolledAccountsQuery.cs` | `AdminRole.SuperAdmin = 1`, `Admin = 2`; the MFA read explicitly maps `IsHead` to SuperAdmin. Security documentation names SuperAdmin / Head. | Reuse the existing municipality-scoped role mapping; do not invent or rename a Head role. This proves software mapping, not office assessment authority. |
| `EEMOCantilanSDS.Application/Common/Revenue/PenaltyDefinitionWorkflow.cs` | Reads require authenticated Admin/SuperAdmin and a matching municipality; definition creation additionally requires SuperAdmin. | Definition setup and assessment creation need separate policies even if both eventually use the same role. |
| `EEMOCantilanSDS.Domain/Entities/Revenue/PenaltyDefinition.cs` | Append-only effective versions; `AppliesTo` is explicitly descriptive, not eligibility; `CheckAmount` enforces positive centavos, fixed equality or the optional direct ceiling. | Reuse resolution/amount rules. A missing direct ceiling does not confer new authority to assess arbitrary amounts. |
| `EEMOCantilanSDS.Application/Common/Revenue/CollectionComposerWorkflow.cs` | `AddPenaltyLineAsync` uses a definition-version ID; requires an OR draft with a non-penalty item; checks draft revision and duplicate penalty code. `WithFinesPolicyAsync` resolves `RevenueClassificationCodes.PenaltiesAndFines` and the effective OR policy. | Keep legacy collected-penalty behavior intact. A saved assessment needs a distinct settlement line and adapter. The current companion-item requirement is implementation evidence, not confirmation that standalone fine payment is forbidden by MEEDO. |
| `EEMOCantilanSDS.Application/Common/Revenue/CollectionSessionWorkflow.cs` | Delegates registered-source search to source-owned discovery. | Reuse discovery identities, then apply fine-specific eligibility; search results alone do not authorize a fine. |
| `EEMOCantilanSDS.Application/Common/Revenue/MonthlyRentCollectionSourceAdapter.cs` | Selects the occupancy answering for the month and freezes StallId, ContractId, period and settlement-authority evidence. | Link a fine to the actual liable occupancy/contract, not only the stall's current holder. |
| `EEMOCantilanSDS.Application/Common/Revenue/ObligationCollectionSource.cs` | Reads source-owned accounts/periods; settlement derives from allocations less corrections; reads do not persist missing periods. | KM/VF/FA references remain obligation accounts. Do not assess rental periods as a side effect of discovering or reading a fine. |
| `EEMOCantilanSDS.Domain/Entities/Revenue/CollectionAllocation.cs`; `CollectionCorrectionAllocation.cs` | Allocation has a source ID and exact amount; correction allocation references `OriginalAllocationId` and a financial effect. | Calculate fine settlement from attributable allocations and correction effects, never a whole multi-period/multi-line Collection amount. |
| `EEMOCantilanSDS.Application/Common/Revenue/CanonicalCollectionPostingCoordinator.cs` | Adds the Collection and durable PostingOperation in one SaveChanges; specialized callers supply validated facts/projections. | Reuse the writer. It does not itself establish assessment locking or eligibility; the new adapter/workflow must provide those before posting. |
| `PenaltyRegisterRowDto` / `PenaltyDefinitionWorkflow.GetRegisterAsync` | Current register selects `CollectionSourceKind.PenaltyDefinition` lines; exposes SRC and correction status, not independently saved debt or an OR-number field. | Extend Collected reads to include future assessment-settlement lines without losing legacy rows. Show receipt evidence only when a supported read actually returns it. |

Authority references: [ADR-002](../decisions/ADR_002_CANONICAL_SETTLEMENT_CUTOVER.md), [ADR-003](../decisions/ADR_003_DURABLE_POSTING_OPERATIONS.md), [ADR-004](../decisions/ADR_004_VERSIONED_WEB_COLLECTION_DRAFTS.md), [ADR-005](../decisions/ADR_005_CORRECTION_REPORTING_BASES.md), [ADR-007](../decisions/ADR_007_SOURCE_NATIVE_COLLECTION_IDENTITY.md), and IA-062 in the [decision registry](../decisions/DECISION_REGISTRY.md).

IA-062 supersedes older document-consumption clauses in ADR-003/004: canonical Collections require SRC, not a supplied physical OR serial, custody selection or document consumption. OR remains the approved instrument policy for fines. Optional physical evidence and unchanged legacy-authoritative writers retain their own semantics. This proposal must not revive receipt-stock gating.

### Proposed permission matrix

No new write policy is activated. Every future operation also requires a valid authenticated tenant, active actor and source-specific authorization. Cross-tenant IDs fail closed; Platform Operator authority does not implicitly authorize municipality financial actions.

| Action | Head / SuperAdmin | Admin | Collector | Payor / Platform Operator |
|---|---|---|---|---|
| Configure definitions | Existing permitted workflow | Existing denied write | No definition write | No implicit permission |
| View saved assessments | Proposed municipality-scoped read | Proposed office read | Only assigned/eligible collection context, if approved | No new read entitlement; operator policy remains separate |
| Create assessment | **Proposed only**, awaiting MEEDO | Deny initially, proposal | Deny initially, proposal | Deny |
| Separate approval | No action until office decides whether needed | No action | No action | No action |
| Record assessment payment | Proposed existing canonical office capability, after settlement approval | Same, subject to approved collection authority | Separate approved Mobile capability and source assignment required | No implicit collection entitlement |
| Correct / waive / reverse assessment | Gated; Head role alone does not establish the policy | Gated | Gated | Deny |
| Correct a Collection | Preserve existing collection-correction authority | Preserve existing authority | Preserve actual existing scope; do not expand it | No new authority |

### Source eligibility and liability

Use one closed typed account-reference contract with two initial adapter families, not a Business Payor master:

- **Facility occupancy:** tenant-owned StallId plus actual ContractId and answering occupancy identity resolved by the adapter. An optional affected month/date must belong to that liable registration. NPM Daily references an actual approved chargeable date or incident; it must not acquire a fabricated monthly fine basis.
- **Obligation account:** AccountId plus actual `ObligationKind`; optional existing PeriodId or event/date reference. KM, Vegetable/Fruits Monthly and FA retain their current kinds and independent numbering. FA remains an event lot, not a monthly lease.

These are candidate families, not a blanket whitelist. NPM/NCC/TCC/BBQ/ICE/KM/VF/FA each remain **unapproved for new fine assessments until the office identifies eligible penalty/source combinations**. Other registrations/transactions require their own verified adapter and explicit policy; unsupported sources are not exposed as collectible selections. Current source discovery supplies identity, not penalty applicability or cross-source basket permission.

`EEMOCantilanSDS.Domain/Entities/Facilities/StallOccupancy.cs` is a derived record around a Contract and occupancy window, not a separately persisted entity with an ID. For this adapter, the persistent liability anchor is the actual ContractId + StallId; freeze the resolved occupancy window as evidence. Do not add a fictitious OccupancyId foreign key or a duplicate occupancy table.

Persist the actual liability anchor. No same-name linkage, automatic successor transfer, fabricated occupancy or conversion to `FacilityCode.KM/FA`. Store unit/payor text as audit snapshots only. Original source references must remain historically readable after closure and must not be cascade-deleted. For any source without a stable historical occupancy anchor, assessment creation remains disabled until that identity is resolved.

Eligibility should be an append-only approved policy version linked to the penalty version, expressing allowed source family/kind, required incident/period/evidence and approval reference. `AppliesTo` stays display text. Until such policies are accepted and populated, fail closed. Settlement checks the assessment's original eligibility/liability evidence and current settlement capability; it must not reassign liability to the current occupant.

### Exact proposed read and command contracts

Route names below are proposals. Implementation should use the repository's existing authenticated API client, Result/error conventions and Composer controller prefix; do not add an anonymous client or a universal payment endpoint.

| Proposed API | Request / response | Guard and failure behavior |
|---|---|---|
| `GET /api/fine-assessments/accounts?query=&cursor=&limit=` | Existing source discovery filtered through accepted eligibility; returns typed AccountRef, display identity, allowed penalty versions and required evidence. Bounded page, opaque continuation. | Office read or expressly approved collector scope; wrong tenant/unsupported source excluded. Never match by name. |
| `GET /api/fine-assessments?accountKind=&accountId=&status=&from=&to=&cursor=&limit=` | `FineAssessmentPage`: rows plus explicit as-of/correction basis; stable sort business date + ID. | Tenant-owned rows only; server validates date range/page bounds and account identity. |
| `GET /api/fine-assessments/{id}` | `FineAssessmentDto`: ID, AccountRef, period/incident, payor/unit snapshot, frozen penalty basis, assessed/net settled/remaining, derived status, version, as-of and capability/disabled reasons. | Authorized tenant read; inaccessible ID returns NotFound without disclosing another tenant. |
| `GET /api/fine-assessments/{id}/history` | Assessment audit plus exact settlement allocation IDs, allocated amount, CollectionId/SRC/date, corrections/replacement references and receipt evidence where recorded; paginated. | Same guard. No whole-line amount substituted for an allocation. |
| `POST /api/fine-assessments` | `CreateFineAssessmentRequest`: ClientOperationId, AccountRef, optional affected reference, PenaltyVersionId, EligibilityPolicyVersionId, optional ProposedAmount, BusinessDate, reason, documentary/incident reference. Returns ID + frozen evidence + version; replay returns same result. | Proposed Head-only, **inactive until confirmed**. Server owns tenant/actor/time/status/amount resolution. Unauthorized, Forbidden, Invalid, eligibility Conflict and idempotency Conflict are distinct. |
| `POST /api/fine-assessments/{id}/settlement-quote` | Requested amount and optional draft/context ID; returns `FineSettlementQuote`: assessment ID/version, exact remaining, permitted amount, original AccountRef, effective classification/OR policy, quote fingerprint/expiry and allowed/disabled reason. Read-only: no reservation, Collection or rental assessment. | Approved payment capability only. Stale/reversed/settled debt or unsupported authority cannot yield a payable quote. |
| Composer `POST .../drafts/{draftId}/fine-assessment-lines` | `AddFineAssessmentSettlementLineRequest`: AssessmentId, ExpectedAssessmentVersion, QuoteFingerprint, Amount, ExpectedDraftRevision. Returns updated existing draft DTO. | Distinct from `AddPenaltyDraftLineRequest`. A preview does not reserve or settle debt; duplicate assessment in a draft rejected. |
| Existing Composer review/post | Existing draft review and ClientOperationId posting contract, extended normalized intent to include assessment ID, allocation amount, version and policy. | Final authoritative revalidation and transaction; never a second `POST /fine-assessments/{id}/pay` writer. |
| Conditional `POST /api/fine-assessments/{id}/corrections` | Only after policy acceptance: operation ID, expected version, approved correction type, reason/evidence; returns immutable correction event. | No DELETE. Do not expose waiver/amendment/approval commands before office accepts their states and effects. |

`AccountRef` is a discriminated source-owned reference, not a free-form facility string. The facility variant requires the registration/contract anchor; the obligation variant requires actual account ID/kind. The server verifies all relationships. Tenant ID, assessed/settled totals, collector authority, approval status and payor snapshot are never trusted request values.

Proposed DTO field types (documentation only; no C# contract files created):

```csharp
abstract record FineAccountRef;
record FacilityFineAccountRef(Guid StallId, Guid ContractId) : FineAccountRef;
record ObligationFineAccountRef(Guid AccountId, ObligationKind Kind) : FineAccountRef;
record FineAffectedReference(Guid? ExistingPeriodId, DateOnly? PeriodStart,
    DateOnly? IncidentDate, string? IncidentReference);
record CreateFineAssessmentRequest(Guid ClientOperationId, FineAccountRef Account,
    FineAffectedReference? Affected, Guid PenaltyVersionId, Guid EligibilityPolicyVersionId,
    decimal? ProposedAmount, DateOnly BusinessDate, string Reason, string? EvidenceReference);
record AddFineAssessmentSettlementLineRequest(Guid AssessmentId,
    long ExpectedAssessmentVersion, string QuoteFingerprint, decimal Amount,
    long ExpectedDraftRevision);
```

Proposed bounded inputs: reason 1–1000 characters, evidence/incident reference up to 200, payor snapshot up to the existing Collection limit of 200, unit snapshot up to 100; trimmed nonempty values where required, positive UUIDs, actual DateOnly period semantics, and bounded structured evidence JSON (16 KiB maximum). These limits require contract acceptance, not changes to existing definitions. Server-generated quote evidence must bind amount, account, version and policy; reject altered/expired fingerprints and recompute authoritative facts regardless of client-supplied token. Do not accept a raw hash as an authorization credential.

`FineAssessmentDto` includes `Guid Id`, typed AccountRef/Affected, immutable basis/version/eligibility evidence, `decimal Assessed/NetSettled/Remaining`, derived status, `long Version`, `DateOnly BusinessDate/AsOf`, UTC recorded cutoff, payor/unit snapshots, audit actor evidence and typed capabilities with disabled reasons. History rows identify the exact allocation, Collection/SRC and correction chain. Reads never return a misleading zero when allocation/projection evidence fails to reconcile.

Use decimal cent precision and `Collection.MaximumMoneyAmount`. For FixedAmount, the server uses the approved amount and rejects a mismatching supplied value. For DirectApprovedAmount, require an amount validated by the version and accepted amount-authorization evidence. Reject a retired, future, unknown, wrong-tenant or non-effective version at creation; return version-changed/review-required if the form's selected version is no longer applicable.

Settlement freezes the **existing assessment's basis**, not a newly effective definition's amount. A definition update must not reprice an existing fine. Revalidation resolves current collection instrument/classification policy and accepted eligibility/correction restrictions while retaining original assessment evidence. Office decision E/I must establish how any later suspension affects existing liabilities.

### State transitions and balance authority

| Transition | Proposed condition / authority | Financial effect |
|---|---|---|
| Create → Outstanding | Authorized evidence; proposed Head action; current business date and duplicate checks | Saves assessment/audit/operation result only. No Collection, SRC, OR serial, rental period or cash. |
| Outstanding → Settled | Approved full remaining payment, canonical allocation posted | One actual Collection/SRC; remaining becomes zero through projection. |
| Outstanding → Partial → Settled | **Disabled unless partial settlement approved** | Only actual allocated cash reduces balance. |
| Settled → Outstanding/Partial after collection reversal | Existing authorized attributable cash correction, using original allocation ID | Assessment remains; valid settlement decreases. Original Collection/SRC and correction chain remain readable. |
| Assessment correction / waiver / cancellation | **No allowed command until decision I is accepted** | Must distinguish reducing liability from reversing money. No implicit cash correction or deletion. |
| Pending → Approved | **Absent unless decision B requires separate approval** | Approval itself records no money. |

Normal view status is derived from approved liability and net effective allocations. Do not store editable Settled/Remaining counters. For the initial no-adjustment lifecycle, `Remaining = AssessedAmount - NetValidAllocatedAmount`; invariant is `0 <= NetValidAllocatedAmount <= AssessedAmount`. Report an integrity error rather than masking negative/overpaid totals. Future approved liability corrections need explicit immutable effects, not overwritten principal. Read contracts declare business as-of date, recorded cutoff and AsOf/LatestCorrected basis per ADR-005.

### Atomicity, replay and correction strategy

1. **Creation:** validate tenant/role before replay lookup; canonicalize intent including actor context, account/occupancy, offense, affected reference, version, amount, date and evidence. Unique tenant + creation ClientOperationId binds one fingerprint/result. Same key/same intent returns original ID; changed intent conflicts. Unique business-offense key is a separate policy gate, not inferred from idempotency.
2. **Quote/draft:** bind version + current net allocation position + policy + exact original account; expiry alone cannot guarantee consistency. Any material change invalidates review. Closing a browser does not settle or reserve anything.
3. **Post:** acquire assessment-row locks in stable ID order inside the existing posting transaction, then re-read net corrections/allocations and accepted policy. Verify draft revision, reviewed fingerprint, authority, amount and tenant. Increment an assessment settlement-generation version on every attributable posting/correction; it is a concurrency token, not a balance. Every settlement/correction writer must participate in this protocol.
4. **Commit:** canonical coordinator writes Collection/lines/allocations and PostingOperation together with draft disposition, assessment version and provenance. Unique assessment/allocation provenance prevents attaching one allocation twice. A second collector with a different operation ID cannot pay an already exhausted assessment. Retry/deadlock handling revalidates; do not silently recalculate and post a different amount.
5. **Correction:** retain original allocation and append attributable correction effect. Replacement uses a new Collection/operation ID and explicit predecessor chain; never both the reversed original amount and its replacement as effective settlement. Assessment cancellation cannot implicitly refund cash. A reversal replay returns its prior outcome. Multi-assessment corrections take the same ordered locks.

The coordinator's SaveChanges alone does not prove the new multi-source locking behavior. PostgreSQL integration must establish the explicit transaction boundary and conflict protocol across Composer, correction and any later Mobile entry point before settlement release.

### Additive EF/PostgreSQL migration proposal — not generated

| Proposed persistence | Columns / constraints / purpose |
|---|---|
| `FineAssessments` | UUID ID + MunicipalityId; typed account family; facility Stall/Contract/occupancy anchor **or** ObligationAccountId/kind; optional affected period/date/incident; immutable payor/unit snapshot; PenaltyVersionId + EligibilityPolicyVersionId; frozen basis/version code/amount/ceiling; numeric money matching existing Collection mapping; business date; UTC recorded time; actor/authority evidence; reason/reference; bigint Version starting at 1. Positive centavos and exactly-one account-family CHECKs. Tenant/account/date/status-read indexes; no mutable paid/balance columns. |
| `FineAssessmentEligibilityVersions` | Tenant, penalty-version reference, typed source applicability, required evidence/offense rules, effective date, approval reference, actor/time; append-only approved versions. No guessed automatic eligibility rows. |
| `FineAssessmentOperations` | Tenant + ClientOperationId UNIQUE; normalized intent schema/fingerprint; terminal outcome/error and AssessmentId; actor/time. A durable assessment operation, **not** a successful Collection PostingOperation without a Collection. Failed infrastructure attempts create no fictitious success. |
| `FineAssessmentEvents` | Tenant + assessment + event ID/sequence; event kind, actor, timestamp, business date, correlation ID, reason/evidence, optional prior event; append-only creation now, policy-approved approval/correction later. UNIQUE assessment/event sequence. |
| `FineAssessmentSettlementLinks` | Tenant + AssessmentId + CollectionAllocationId, UNIQUE tenant/allocation; composite FKs to assessment and immutable allocation. Authoritative money remains in allocation/correction records; link contains no independently mutable settled counter. Record enough provenance to traverse CollectionLine → Collection/SRC and correction allocations. |
| Future typed source discriminator | Add a distinct `CollectionSourceKind.FineAssessment` value only with separately approved implementation; never change the numeric meaning of existing `PenaltyDefinition` (9). Update source validation, draft/allocation dispatch, correction adapters and register/report reads together. No KM/FA FacilityCode additions. |

Use composite tenant+ID foreign keys (with additive alternate keys where existing referenced tables require them) for penalty version, eligibility version, source registration, affected period, allocation and assessment. Enforce source-family consistency with explicit nullable family-specific FKs/CHECKs instead of an unvalidated generic GUID. Verify the actual occupancy persistence anchor when translating the adapter into EF; do not invent a second occupancy record. No cascade delete of historical evidence. Optional past period metadata must reference actual records or be explicitly an incident/date, never a manufactured rental obligation.

Observed `CollectionAllocationConfiguration` already has tenant+ID alternate key, numeric(18,2), restricted tenant-scoped line FK and `CK_CollectionAllocations_SourceShape` enumerating allowed allocation kinds. The approved migration must extend that CHECK for the new assessment allocation kind as well as all corresponding line/draft checks; merely adding an enum value would not make posting valid. Proposed principal/ceiling precision is numeric(18,2), with existing domain money maximum checks and server cent validation. Composite source validation must prove the selected Contract belongs to the selected Stall and the affected period belongs to the selected account, not merely that each ID exists separately.

Duplicate-offense uniqueness requires decision K: proposed normalized office-issued incident/notice key + liable registration + penalty code + affected scope. A penalty-version change must not evade this business constraint. Multiple genuine incidents in one month must remain distinguishable. Do not impose `UNIQUE(account, penalty, month)` without office acceptance. The migration must be revised with the accepted key before implementation approval.

No historical assessment backfill. Legacy fine lines keep version IDs, SRCs and collected status. New assessed fines start prospectively; historical unknown account references remain unknown. No rewrite of Stall, Contract, ObligationAccount, rent periods, Collections, lines or penalty versions. Existing-column alternate keys and enum acceptance are additive compatibility work, not a ledger migration. Exact generated SQL/down behavior requires a later review; no migration file is created now.

### Complete office-decision register

Every recommended default is a **proposal**, not confirmed local policy. Product approval for saved-before-payment does not resolve these questions.

| ID / question | Verified rule | Recommended policy | Financial implication | Implementation dependency | Approval status |
|---|---|---|---|---|---|
| A — Who assesses? | Head maps to SuperAdmin; definitions require that role. No assessment writer. | Head-only initial assessment with tenant guard. | New liability creation authority. | Create policy + audit. | Proposed by owner; **MEEDO confirmation pending**. |
| B — Separate approval? | No established independent assessment approval stage. | Treat authenticated Head assessment as approval only if office confirms; do not invent second stage. | When debt becomes enforceable. | State/event model and form. | Pending. |
| C — Which source/penalty pairs? | Source-owned registrations exist; `AppliesTo` is descriptive. | Explicit office-approved eligibility versions; all others disabled. | Liability and classification cannot follow a name. | Discovery/adapters/eligibility persistence. | Pending for NPM/NCC/TCC/BBQ/ICE/KM/VF/FA and other sources individually. |
| D — What offense/evidence? | Approved definitions exist; delinquency is not automatic fine authority. | Require accepted incident/notice/affected reference per penalty; distinguish debt from offense. | Proof of assessment and duplicate scope. | Evidence fields/validation. | Pending documentary requirements. |
| E — Amount and direct authority? | Fixed equality and direct ceiling checks implemented. | Freeze effective version; require accepted direct-amount approval evidence. Do not reprice saved debt. | Prevent arbitrary/new duplicate charges. | Resolver, evidence and later suspension policy. | Definition behavior verified; assessment authority pending. |
| F — Partial payment? | No fine-assessment settlement exists. | Full remaining only initially. | Allocations/status/reconciliation. | Quote/post/correction rules. | Pending. |
| G — Standalone fine payment? | Current new-penalty Composer requires companion item; fines policy is OR. | Allow standalone only if expressly accepted; compatible shared OR only for verified context. | No invented rent to unlock fine payment; no OR/CT mix. | Draft initialization/context and settlement capability. | Standalone policy pending; current requirement not promoted to office ruling. |
| H — New fine after closure? | Historical liability must stay source-owned; no assessment rule. | Block new closed-account assessments until accepted exceptions exist; retain existing debts. | Closure does not erase old liability. | Eligibility and historical reads. | Pending. |
| I — Correct / waive / reverse? | Collections have correction chains; no independent assessment waiver rule. | No delete; no new assessment correction until actor/evidence/liability versus cash effects accepted. | Refund/replacement distinct from liability cancellation. | Events, correction commands and locks. | Pending. |
| J — Assessment date? | Philippine business date used by workflows. | Current business date only; back/future dating disabled initially. | Effective version and audit/report cutoff. | Date validation. | Pending office date policy. |
| K — Duplicate offense? | Operation keys stop replay, not a separately entered duplicate offense. | Office-defined unique incident + registration + penalty + affected scope, not name or version ID. | Avoid double liability while allowing distinct offenses. | Business UNIQUE key/evidence rules. | Pending exact offense identity. |
| L — Automatic penalties? | Product approval expressly excludes automatic delinquency fines. | Manual governed assessment only. | No automatic rent/fine generation. | No background/late-fee writer. | **Excluded by accepted product direction**; any future policy separately approved. |
| M — Legacy collected fines? | Existing definition-version Collection lines valid. | Preserve as Collected, no invented assessment or outstanding debt. | No duplicate cash/debt. | Union read/classification/correction compatibility. | Preservation required; no backfill authorized. |
| N — Closure / transfer liability? | Money belongs to liable period/occupancy; equal names not identity. | Keep fine on original registration; never automatically move to successor/renewal. | Outstanding debt and future settlement responsibility. | Stable account anchor and office transfer/collection policy. | Identity safeguard accepted; operational exceptions pending. |

Minimum implementation approval must resolve A–K and N for the intended initial source/penalty scope, with explicit exclusion of unsupported exceptions. Confirm whether fines may be settled after closure and whether retired/suspended definitions restrict collection of already assessed liabilities. A technical default is not a substitute for this approval.

### Workspace, reporting and rollout

- **Assessments:** saved obligations with assessed/net settled/remaining, typed source, date, status and account review. No enabled Assess Fine before the real authorized writer exists. Account selector uses verified eligible accounts, not a payor-name picker.
- **Collected:** preserve existing penalty-version rows and include assessment settlements through the same classified canonical cash read. Do not count assessment principal as collected. Preserve SRC/corrections and optional evidence where available.
- **Penalty setup:** current versioned catalog and Head authority remain separate. No duplicate definitions.
- **Accepted Account Review:** extend only Other assessed charges with the shared account-linked read when implemented. Rental and fine totals remain separately labeled; combined amount only for the same verified liability context and reconciling projections. Preserve year ledger, subordinate daily view, footer and existing authorized payment navigation.
- **Reports/remittance:** actual valid Fines/Penalties Collection cash only under effective classification/policy. No unpaid assessment in Monthly Income or remittance; no rental debt duplicated into Arrears. Keep ADR-005 as-of/latest-corrected distinctions.

Controlled implementation sequence after acceptance: (1) persistence + authorized creation/idempotency + PostgreSQL proof; (2) bounded assessment/account read models and Fines/Review UI; (3) separately approved assessment-specific canonical quote/draft/post/correction protocol; (4) reporting and complete regression/acceptance. Mobile settlement requires explicit versioned capability and assignment/offline replay coverage; it is not enabled implicitly by office Web completion. Existing financial workflows remain unchanged throughout.

Rollback is capability disablement and preserved records, not deletion or schema downgrade. Assessments must remain readable even if settlement rollout pauses. Production application/startup migration, deployment and source activation need separate environment-specific approval and readiness verification.

### Required test and authenticated acceptance matrix

These are **future tests**, not test results for an unimplemented feature. PostgreSQL tests must use an authorized disposable environment and the actual constraints/transactions; mocks cannot establish uniqueness, rollback or locking.

| Cases | Unit / API / component coverage | Real PostgreSQL proof |
|---|---|---|
| A01 Head creation; Admin/Collector/Payor rejection; operator not implicitly Head | Policy matrix, route visibility, errors | Persisted actor/tenant audit, no unauthorized rows |
| A02 wrong tenant ID/source/penalty/period/history/replay | Fail-closed scoped adapters; nondisclosing NotFound | Composite FK/filter enforcement; cross-tenant request leaves no effects |
| A03 source eligibility, historical occupancy, invalid/closed account, event variant | Typed adapters, accepted policy, NPM day versus monthly/FA distinctions | Real anchors, closure race, no successor liability or new rental period |
| A04 effective/retired/future penalty, fixed mismatch, cent precision, direct ceiling/evidence | Existing resolver + new create validator | Frozen version/evidence persists and reloads; later version cannot reprice |
| A05 required evidence and duplicate same offense; distinct incidents | Accepted business key normalization | Unique offense key across clients and penalty versions; concurrent duplicates |
| A06 operation replay, changed intent, simultaneous identical request, failure/retry | Intent normalization and outcome access authorization | Exactly one assessment/audit/result; lost-response replay; rollback and key conflicts |
| A07 assessment without money | Creation contract + UI distinction | No Collection, SRC allocation/sequence request, draft, remittance or rent assessment from creation |
| S01 full payment; conditional partial; overpayment; settled/reversed debt | Quote/amount/status rules and review invalidation | Exact allocation, authoritative remaining, transactional rejection |
| S02 stale quote and two collectors/drafts/IDs; mixed assessment locks | Stale-version error/refresh | At most available balance; ordered locks, rollback, race and retry |
| S03 OR policy, standalone accepted rule, incompatible CT/source contexts, Legacy/Pending restrictions | Capability/disabled reasons and draft validation | No forbidden classified line or source cutover; SRC-first posting without serial |
| S04 idempotent posting/browser refresh/replayed corrected Collection | Existing posting intent + new assessment identity | One effective outcome per draft/key; original outcome retained after reversal |
| S05 multi-period/multi-assessment allocations, reversal/replacement, correction replay | Account history uses allocated portions, correction basis | Attributable effects only; replacement not double-counted; version generation advances atomically |
| R01 legacy penalties, unpaid/new assessments, Fines register/Monthly Income/remittance | Separate read projections and classifications | Legacy unchanged; zero new cash before payment; correct valid cash after payment |
| U01 account selection, Save Fine and refreshed Fines/Review | Component loading/error/permission/reconciliation states | API read-after-write with actual IDs and amounts; no same-name association |

Future authenticated scenarios: Head creates an accepted fixed fine without payment and sees the same assessment in Fines and the linked account; Admin/Collector cannot create; direct-amount evidence/ceiling validation; closed/unsupported source disabled; standalone/shared OR according to office decision; full payment refreshes remaining and exposes actual SRC; stale second collector cannot double settle; correction/replacement preserves both histories; legacy Collected rows remain; 1440/1024/390 layouts preserve the accepted Account Review scroll/footer. No browser acceptance of these future features is claimed now.

### Approval checkpoint and unauthorized actions

Technical investigation is sufficient to submit the financial contract. **Implementation remains blocked on office decisions and explicit architecture/migration acceptance**, not on completion of a UI mockup. No FineAssessment entity/DTO file, migration, API writer, assessment settlement, enabled Assess Fine action, automatic fine, new receipt requirement, production change, push or integration is authorized by this review.

The accepted modal and existing rental/payment behavior are unchanged. Complete receipt allocation history remains a separately unimplemented read capability; this proposal does not claim to fix it. No renewal, reopening, expiration or Arrears reclassification policy is approved here.

### Executed verification for this documentation checkpoint

- `dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release --filter FullyQualifiedName~PenaltyDefinitionTests --no-restore`: **13 passed, 0 failed, 0 skipped**. These characterize existing definition rules only; they do not test a FineAssessment implementation. Compilation reported existing duplicate-using, nullability, unused-parameter and analyzer warnings; no source code changed in this checkpoint.
- All 7 relative documentation links resolve; scoped `git diff --check` passes. Full diff check also passed with Git's existing LF/CRLF notices.
- No Web, Mobile, PostgreSQL or standalone Release build run is claimed for this documentation-only change. Future financial integration tests and authenticated scenarios above remain unexecuted until approved implementation exists. Prior UI acceptance is the owner's stated acceptance, not a new browser verification in this task.
- Only this review document is staged for the checkpoint commit. Protected concurrent diffs remain unstaged with their starting numstats: ACTIVE_WORKSTREAMS 4/0, CURRENT_RELEASE_STATE 7/1, VEGETABLE_FRUIT_WORKSPACE 10/0. No migration created/applied, financial write, push, merge, integration or deployment performed.
