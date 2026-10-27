# V18 — Guided tenant revenue-source first-policy setup
Date: 2026-10-28 (Asia/Manila)

## User goal / evidence
Carrascal Head Portal displays eight selected operations, many as "Setup required" / "No revenue source set up"; ECF and WCF have dormant source identities but no effective accountable-instrument policy. The desired newly-onboarded municipality should reach working collections with minimal Head administration, **without applying Cantilan-specific LGU rules or creating unsupported posting authority**.

## Verified baseline and root cause
- `ActivateMunicipalityCommandHandler` stored selected TenantOperationSettings but seeded dormant revenue classifications for ECF/WCF/WEIGHT_AND_MEASURE only. No receipt policy or collector assignment was seeded, correctly.
- `RevenueSetup.razor` listed real readiness blockers but offered no action for a listed, setup-required operation. The separate source Details drawer was read-only.
- Existing authenticated, Head-only revenue classification endpoints support atomic source+first policy and append-first-policy. `GetOperationSetupQueryHandler` obtains readiness from the same capabilities as Mobile Collector. Keep those authorities intact.

## Isolated change
Worktree: `C:\dev\stalltrack\v18-guided-revenue-setup-20261028`.
Branch: `feat/v18-guided-revenue-source-setup-20261028`, based on `origin/master` commit `5a310317` (not dependent on separate draft report PR #73).
1. New LGUs: selected built-in nonfacility operations each get a **dormant tenant-owned classification identity** atomically at activation. Unselected operations receive none. No policy, amount, assignment, official receipt, custody, contract, Collection, financial record, or posting authority is created.
2. Current and newly activated LGUs: Head Revenue Sources > Collections has explicit **Set up** / **Review** action for non-ready operations. A drawer shows actual server blockers and can confirm **only the first default policy** using existing Head-only, tenant-scoped API. It collects approved OR/CT and current/future effective date, requires checkbox attestation, and asks the Head to finish collector/service/rate/physical-receipt prerequisites. Ready remains server-derived.
3. The drawer refuses retired classifications, sources with any existing/pending future policy, already ready operations, and Vegetable/Fruit split daily/monthly policy contexts. Where writer requires fixed OR/CT, only that choice is offered (Fish/Meat, Weighing, ECF and space obligations OR; WCF and Transportation CT). Other governed fees allow the Head's approved OR/CT choice.
4. Save path refreshes source state immediately before mutation; the server retains tenant authorization and policy-effective-date uniqueness checks. A confirmed save cannot later be reported rolled back due to UI refresh failure. All existing policies and histories remain untouched.

## Verification
- Client Release build: passed, zero errors (104 existing warnings).
- Head Portal RevenueSetup bUnit regression after final defensive changes: **20/20 passed**, including first-policy create, dormant append, retired/scheduled refusal and consent.
- Onboarding disposable PostgreSQL suite: 14/14 passed with selected/nonselected classification assertions, zero policy/Collection/collector assignment creation.
- Client component full build/test target compiled, 0 errors (pre-existing analyzer warnings).
- Full GitHub CI and authenticated tenant browser acceptance required before promotion. No branch merge or production deployment authorized.

## Deliberate scope constraints
- An operation marked "Setup required" after saving can still need actual LGU-approved fees, service configuration, active collector assignment, source cutover and/or accountable document custody. Merely selecting a source is **not** proof of collectible Mobile behavior.
- No migration or production data write. No automatic OR/CT policy or ordinance rate. No production activation/deployment authorized by this change.
- Baseline separate draft PR #73 report-label and remittance changes not merged into this isolated branch.
- Full collection-readiness UX automation across all facility-specific operations or dual-instrument modes is not claimed.
