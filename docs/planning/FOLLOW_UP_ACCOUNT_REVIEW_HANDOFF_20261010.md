# Follow-up Account Review — Phase 1 handoff

Checkout: `C:\dev\stalltrack\eemo`. Branch: `codex/vegetable-fruit-space-workspace`. Starting HEAD: `67a8ed7e`. Local implementation only; no integration, push, deployment, production changes or migration.

## Authority and evidence reviewed

AGENTS, CONTEXT, V2 master specification, current release state, documentation map, operational rulebook/business rules, revenue architecture, architecture/application guidance, Decision Registry, UI V3 and relevant rental handoffs. Collection/settlement/draft/idempotency/correction/governed-service ADRs and ADR-007 preserve existing source authority. IA-019/IA-020 distinguish delinquency from severity and unresolved qualifying arrears; IA-049 governs approved penalties and OR compatibility.

The October 9 registry handoff is historical evidence, not the present UI checklist: later rental polish at `67a8ed7e` already removed the redundant registry heading and moved advanced filters into the accepted compact affordance. This work does not change that registry or reopen the completed rental architecture. Earlier missing KM descriptive-evidence statements were superseded by the existing lease-evidence implementation; renewal/reopening/expiration remain gated. Prior local migration verification is not a production migration claim.

The three concurrently dirty planning documents are intentionally untouched and excluded from staging. Their preexisting numstats remain ACTIVE_WORKSTREAMS 4/0, CURRENT_RELEASE_STATE 7/1, VEGETABLE_FRUIT_WORKSPACE 10/0. Canonical reconciliation belongs to their owners; this focused handoff records the new checkpoint without replacing their edits.

## Implemented presentation

- Operations uses the existing sheet rows. `Others` follows Slaughterhouse, with Arrears & Delinquency and Fines & Penalties. Arrears leaves Stall Rental and Fines leaves Space Rental **navigation only**. `/operations/arrears`, `/operations/arrears/report`, `/operations/fines` remain unchanged. Fines retains OR; Arrears has no blanket receipt label. Tenant-configured enterprises remain a separate working section, named Configured enterprises.
- The Operations subtitle no longer claims that administrative navigation exactly mirrors Monthly Income. No official report classification or total changed.
- Live and past Follow-up rows use Review account or Review item, depending on the existing typed queue context, registered account identity and reason. Market-wide NPM coverage gaps without a StallId are operational items, not account debt. Non-account receipt/utility/excused/operational items do not receive a made-up receivable.
- `FollowUpAccountReview` reuses `VendorDrawerShell`, `AccountStatusPill`, existing table/notice/skeleton/button styles and a small focus-trap module. One scroll body and stable footer; Escape closes, focus returns to the originating control. Review closes before the original payment, receipt-evidence or utility form opens.
- Arrears & Delinquency remains a read-only management view over FinancialReport, Follow-up and official ARREARS cash-recovery reads. Qualifying Arrears explicitly reports the missing verified qualification/recovery contract; it does not fabricate a qualified register, reclassify debt or offer write-off. The feed can include ended occupancy follow-up; recorded row status remains visible rather than calling closure a financial settlement.
- Fines retains its actual approved definition setup and posted collection register. No unsupported Assessments tab or unpaid-fine total is added.

## Source adapters and scopes

| Context | Read and behavior |
|---|---|
| Monthly facility account | `GetOutstandingMonthsAsync(StallId, ContractId, period context)`; original occupancy/term retained. Current authoritative outstanding-period scope can differ legitimately from a rolling/historical queue figure. Collected is labeled loaded periods, not lifetime cash |
| NPM | Same server month position plus `GetSettleableNpmDaysAsync` for actual eligible dates, original term and approved fees. No generated weekend/absence calendar; RentGoal adjustments stay in existing Whole Payment |
| KM / VF Monthly | Typed operation-code → ObligationKind adapter, source-matching account and register. Real assessed/settled/remaining period quotes; account total reconciles against returned period balances |
| FA | Actual event/date obligation, lot account and event facts; no monthly grid, annual rent or three-year term introduced |
| Missing OR / utilities / operational exceptions | Recorded queue facts and original source-specific evidence/bill flow; no invented debt or fine |
| Contract reminder without debt | Contract/account information is expanded first; no empty debt dashboard implies an assessment |

Account reads use existing tenant/role-guarded APIs. `ObligationWorkflow.GetAccountsAsync/GetRegisterAsync` and period quotes are read projections, not registration or collection commands. Source mismatches fail closed. Failed/forbidden reads display errors and retry; they never silently render a zero balance.

## Period-level detail and settlement transition

Actual outstanding periods are chronological and grouped by actual year, oldest group initially open. Year totals sum returned authoritative remaining amounts. Partially settled periods count as **periods with a balance**, not wholly unpaid months. Settled assessed periods appear separately only when the source register returns them. Missing/unassessed years are not generated. Individual period expansion shows assessed, allocated/settled and remaining amounts, status, and recorded OR/date evidence only if the source returned it.

KM/VF/FA register balances must reconcile to the account total. A mismatch displays an alert and blocks payment transition. Facility totals use the endpoint's returned outstanding-period scope rather than pretending to reconcile to a different queue snapshot scope. Current read date and follow-up snapshot date are explicitly distinct; this is not historical as-of reconstruction.

Selecting a period carries year/month into the existing `PayBillModal` alongside original StallId/ContractId. Its server-owned eligible months/days, Whole/All Outstanding logic and validations remain unchanged. No second payment POST exists. Successful payment reloads the queue/register and returns to a fresh review; payment cancellation also returns. Missing-OR and utility forms retain their existing handlers, and closing them returns to review without stacked overlays.

**Existing contract limits:** KM/VF/FA source-owned OR collection is on Collector Mobile; no equivalent Web account writer exists. The review gives the correct registered Monthly/event-lot instruction and original source-account link. It does not expose a raw amount form. Existing live NPM source navigation and past NPM PayBill calendar remain source-specific. A source link is labeled Open source account, not falsely described as complete transaction history.

## Allocation/history gap — read-model proposal, not implementation

`CollectionAllocation` records one immutable source application; `CollectionCorrectionAllocation` refers to its original allocation. Current activity DTOs expose only the first allocation for some lines and cannot prove all period-specific SRC/correction details. The review therefore does not display a full CollectionLine amount as one period's settlement and explicitly marks detailed receipt allocations unavailable. Authoritative period totals still show what is owed.

Propose a tenant/role-guarded account-period evidence read through each source adapter, not a new writer:

- Verified source account/occupancy/term identity, actual period ID/date, read scope/as-of and authoritative assessed/settled/remaining.
- Every matching CollectionAllocation joined to CollectionLine/Collection; actual allocation ID and amount, canonical Collection ID/SRC/business date, recorded collector and accountable-document/OR evidence if present.
- Correction allocation references and effects, reversal/replacement identity/chain, and effective settlement under the existing correction rules. Never merge same-name accounts or double-count original and replacement amounts.
- Explicit Legacy evidence and incomplete-history availability; reconcile canonical allocations to existing period projections without claiming legacy evidence is canonical cash.
- Paging for long history, source/tenant/term authorization, tests for split-period lines, multi-line same source, original/correction/replacement, historical as-of, closed occupancies and source isolation.

No such API/DTO is implemented here. A safe read-only contract is needed before promising complete SRC/OR allocation and correction history or recorded rate-version detail that current period DTOs do not supply. Full facility contract/expiry evidence remains accessible through original source profiles; the review does not manufacture it from queue text.

## Verification

- Focused Web: **52 passed, 0 failed, 0 skipped**. Includes source/period isolation, partial/paid/multi-year/missing-year/closed account, FA event, actual NPM eligible dates, market-wide operational-item identity, mismatch guard, forbidden/error/retry, Escape and period transfer to existing payment form.
- Regression proof: temporarily removed reconciliation guard; its targeted test failed (1 failed). Restored the guard, then passed focused/full checks.
- Full Web separately: **880 passed, 0 failed, 0 skipped** on final rerun. Previous run had 877 passed and one timeout in unchanged NCC registry fixture; rerun without concurrent compilation passed. Earlier development failures were fixture setup/obsolete assertions and were corrected.
- Full Unit separately: **2,526 passed, 0 failed, 0 skipped**.
- Client Release: **0 warnings, 0 errors**. Recompilation/test logs retain existing analyzer/package warnings; no dependency-upgrade scope was added.
- CSS: **164** source/bundle files checked, **0** unbalanced. `git diff --check` clean.
- Rendered fixtures: multi-year review, NPM review, three-action payment review and Operations at **1440/1024/390**; twelve screenshots generated, measured for document overflow and inspected across the review/layout cases. Nine drawer cases have visible footers and keyboard focus containment. At 390px amounts stack beneath the period label; desktop retains the compact table. Modal body scrolls independently.
- Authenticated local Head review: live and past Follow-up routes, Operations, Arrears & Delinquency and Fines & Penalties. NCC review showed actual July–October periods; selecting August transferred the exact account/term/month into existing PayBillModal, and Cancel returned to review. NPM review showed actual partial month amounts and server-returned unpaid daily dates (paid October 8 excluded). FA showed its October 9 event obligation and Mobile OR instruction. Missing-OR review opened the existing evidence form and Done returned to review without saving. No money, receipt evidence, assessment or registration was posted.
- Actual drawer screenshots were inspected at desktop/narrow widths, including 1440/1024/390. The three-action footer initially clipped Close at 390px. A source-local wrapping footer and drawer width correction fixed it; final buttons fit within the document width (380px content width in a 390px viewport). This leaves the accepted rental registry/shell behavior unchanged. KM/VF had no current local Follow-up rows; their source adapters were checked with component fixtures, not claimed as runtime account checks. Utility mutation and successful payment posting were not manually exercised.
- Artifacts (ignored, not committed): `artifacts/account-review/`, focused/full/Unit/build/red-proof logs. Fixture checks and authenticated engineering checks do not constitute project-owner visual acceptance. Local Client was refreshed on existing configured ports with tested Release build and local API, without changing committed launch settings.

Commands:

```powershell
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~FollowUpAccountReviewTests|FullyQualifiedName~FollowUpQueueCompositionTests|FullyQualifiedName~OperationsFacilitiesHubTests|FullyQualifiedName~OperationReportsAndArrearsTests|FullyQualifiedName~FinesWorkspaceTests'
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --no-build --no-restore
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release --no-restore
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj --configuration Release --no-restore
node artifacts/account-review/visual-check.cjs
git diff --check
```

No backend/shared financial contract changed; PostgreSQL, Mobile and API Release were not separately required/run for this UI slice. No migration generated/applied. Current local lease-evidence migration status is inherited evidence from prior handoffs, not newly verified here.

## Remaining gates

Authenticated local review was completed after the project owner signed into the Codex browser. Project-owner visual acceptance remains pending; integration is not authorized. Full per-period receipt allocations/correction history still requires the read-model above. Qualifying Arrears recovery and unpaid fine persistence remain separate financial gates.

[Fine assessment design](FINE_ASSESSMENT_DESIGN_REVIEW_20261010.md) records accepted product direction, existing penalty/Composer limitations and exact policy/API/migration decisions. **Phase 2 is proposal only; Phase 3 is not authorized.** No assessment receivable, automatic fine, universal settlement writer, reopening, renewal or contract expiration automation was implemented. Source identity, OR/CT, canonical Collections/SRCs, assessments, classifications, allocations and historical data remain unchanged.
