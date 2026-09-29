# Claude Backend Gap Audit — 2026-09-30

**Lane:** Claude Backend (`backend/claude-gap-completion-v3`, worktree `claude-backend-v3`).
**Baseline:** `2d42a39a` (accepted Luna checkpoints `627ee645`, `f71e0286`, `1b43f1de`, `2d42a39a`).
**Status:** Live gap ledger. Rows are updated as slices land; the overnight handoff is
[CLAUDE_BACKEND_OVERNIGHT_HANDOFF_20260930.md](CLAUDE_BACKEND_OVERNIGHT_HANDOFF_20260930.md).
**Authority:** This is an engineering audit, not business authority. It records what the accepted code does, what the
docs/rulings require, and which gaps are safe to close without new EEMO input.

States: READY · SAFE FOUNDATION · BLOCKED BUSINESS RULE · BLOCKED CUTOVER · BLOCKED DOCUMENT CUSTODY · BLOCKED MOBILE ·
ALREADY IMPLEMENTED · DEFERRED.

## Method

Static inspection of Domain/Application/API/Infrastructure on the baseline, cross-checked against the rulebook,
Revenue Architecture, Decision Registry (IA-029, IA-031–033, IA-043–048), ADR-001…006, the itemized baseline and the
Phase 3/4/5A planning inventories. Candidate branches were read only to confirm where a model lives.

## Cross-cutting findings

1. **Handoff matrix overstates the accepted baseline (documentation contradiction).** The 2026-09-29 handoff readiness
   matrix describes "historical `LandingBerthingActivity`", "tenant collection-point policy/setup" for Market Fees and
   "governed-service assessment evidence" for Vegetable/Fruit, Transfer Large Cattle and Fiesta/Araw. None of these
   types exist on this baseline (`git grep` finds no `LandingBerthingActivity`, `ConfigurableService*` or collection-point
   model on `interface-v2/clean-adoption` or this branch). They exist only on the unaccepted `interface-v2/ui-completion`
   candidate. For this lane they are therefore **absent source models**, not partially built sources.
2. **IA-029 versus current WCF direction.** IA-029 (FUTURE) says WCF must eventually be recordable from both Web/Admin
   and Collector Mobile. Current Clint/Core Brain direction (this sprint) says normal WCF field collection is Collector
   Mobile only and Head/Admin Web is monitoring/reconciliation. The current direction governs this lane; IA-029 should be
   updated by Core Brain so the registry does not keep advertising Web dual entry as the target.
3. **No correction writer exists.** `CollectionCorrection.Record` is a domain factory only; no application workflow
   records voids, reversals, replacements or document corrections. Readers must still honour correction evidence so
   they are correct when a writer arrives.

## Gap ledger

| ID | Area | Current authority | Current implementation | Missing capability | Business authority | Risk | Blocks | Safe now? | Needs EEMO? | Needs deploy/cutover? | State / next action |
|---|---|---|---|---|---|---|---|---|---|---|---|
| CB-01 | WCF Web channel | Clint/Core Brain: normal WCF = Collector Mobile; Web = monitoring | `POST api/wcf-collections/collections` (`WcfCollectionWorkflow.PostWebAsync`) accepts new Admin/Head posts against an in-office CT whenever the Water part is Canonical | Server-side refusal of new Web-origin WCF Collections while preserving prior Web outcomes | Current direction (§9 of sprint brief); ADR-003 replay rules | High (bypasses Mobile channel/custody model once any source activates) | V3 UI removal of "Collect with CT" is cosmetic without it | Yes | No | No | **IMPLEMENTED** (`fix(wcf): enforce collector mobile collection channel`) |
| CB-02 | Legacy Web cumulative Water writer | Legacy UtilityBill authority | `UtilitiesController.RecordPayment` → `RecordUtilityPaymentCommandHandler` still writes cumulative Water status while the part is Legacy; after Pending/Canonical it is preserved as reconciliation evidence | Decision whether office legacy Water entry stops before cutover | ADR-002/Q43 quiesce step; Phase 4 inventory | Medium | Water cutover | No — it is the live production legacy path | Core Brain/EEMO operational decision | Yes (scoped quiesce) | **BLOCKED CUTOVER** — quiesce as part of the Water scope transition; do not remove unilaterally |
| CB-03 | IA-029 wording | Decision Registry | IA-029 still states Web+Mobile WCF target | Registry reconciliation | Core Brain | Low | Documentation consistency | n/a (Core Brain owns registry interpretation) | No | No | **DEFERRED** to Core Brain |
| CB-04 | Canonical Monthly Income reader | ADR-005/IA-043, Revenue Architecture §3/§8, baseline §13 | No reader. Only WCF activity and collector-report readers touch canonical rows | Read-only classified gross / correction effect / net by month with explicit AsOf or LatestCorrected basis and canonical-only coverage | ADR-005 (technical bases approved) | Medium | Phase 8 report cutover, targets | Yes (read-only, no cutover) | No | No | **SAFE FOUNDATION → implement** (`feat(reports)`) |
| CB-05 | Official cross-period RCD treatment | IA-043 open | — | Office policy | Office | High if guessed | Official RCD presentation | No | Yes | — | **BLOCKED BUSINESS RULE** |
| CB-06 | Monthly Income legacy + canonical combination / production report switch | Legacy report readers remain authoritative | Legacy readers (`FacilityReportsRepository`, `GetReportOfCollections`, dashboards) | Source-by-source coverage merge and cutover | ADR-002 §8, baseline §13 | High | Official Monthly Income from StallTrack | No | No | Yes | **BLOCKED CUTOVER** |
| CB-07 | Correction writer (void/reversal/replacement/document correction) | Baseline §10, ADR-005 | Domain factory only | Authorized Head/Admin correction workflow with its own operation identity | Void/replacement approval policy not confirmed | High | Correcting canonical cash | No | Yes (void/replacement approval policy) | No | **BLOCKED BUSINESS RULE** |
| CB-08 | Fish/Meat Vendor Fee source | Rulebook §9 (separate OR classification, ≈₱900/month in ₱30 installments) | No distinct field/rate/source. NPM Fish/Meat sections bill the NPM daily stall fee (`NpmDailyStall*` keys, Cantilan ₱30/day, ₱900 month) through `DailyCollection.DailyFee` | Authoritative evidence of whether the vendor fee is the same money as the NPM Fish/Meat daily stall fee (a reporting classification of it) or an additional charge | Not stated | High (double counting stall rent as vendor fee) | Vendor-fee canonical line, Monthly Income row | No | **Yes** | — | **BLOCKED BUSINESS RULE / SOURCE MODEL** — see question U-1 |
| CB-09 | Weight & Measure canonical adapter | NPM `DailyCollection` weighing facts (IA/rulebook §9: W&M = OR) | Meat: kilos, rate, effective date and amount frozen server-side (from 2026-09-29). Fish: kilos only; rate resolved at read time; no frozen amount. Source-part `MeatWeighing`/`FishFee` representable. Document = legacy free-text OR string. No `DailyCollection` settlement-authority marker | Exact-once adapter, OR custody, correction relationship, cutover | Rulebook §9; baseline §8 | High | W&M canonical cash | Shadow/comparison only | Fish historic rate evidence (not guessed) | Yes for real adapter | **SAFE FOUNDATION (shadow) → implement if time**; real adapter **BLOCKED CUTOVER / DOCUMENT CUSTODY** |
| CB-10 | Accountable Forms CT custody | Baseline §10; Head/Admin authority | Receive OR/CT book + units; list; bounded CT range assignment to active same-tenant collector; per-tenant number uniqueness and per-book serial uniqueness; `State` concurrency token; consumed / reconciliation-required / voided states; assignment history rows; audit interceptor coverage; Head/Admin-only API | — | — | — | — | — | — | — | **ALREADY IMPLEMENTED** (no proven backend defect found) |
| CB-11 | Forms: return-to-office, void, spoiled/cancelled, OR collector custody, remittance, RCD sign-off, stock return | IA-024, IA-032, rulebook §5 | Domain methods exist for `ReturnToOffice`/`Void` but no API; no OR assignment | Operating policy | Not confirmed | High if invented | Full custody lifecycle | No | Yes | — | **BLOCKED DOCUMENT CUSTODY** |
| CB-12 | Web WCF page reads office tickets via WCF typed client | — | `IWcfCollectionsApiClient` used by `WaterConsumptionFees.razor` | Generic accountable-forms client | — | Low | — | Frontend | No | No | **FRONTEND CONTRACT FOLLOW-UP** (not a backend defect) |
| CB-13 | Collector creation for non-facility operations | `1b43f1de` operation catalog; rulebook/ADR-006 assignment-driven Mobile | Create/Update require ≥1 facility; operation assignments only via a separate Head endpoint afterwards | Atomic create/update with facilities optional when ≥1 legitimate operation assignment exists | Existing semantics: a collector must have legitimate work; accepted operation catalog | Medium | Operation-only collectors (WCF, Landing, etc.) | Yes | No | No | **READY → implement** (`feat(collectors)`) |
| CB-14 | Mobile assigned-vs-collectible contract | ADR-006 §6; rulebook §13 | Mobile menu lists facilities only; no operation capability | Read-only server-derived capability per operation (assignment, policy, custody, writer, settlement authority) | ADR-006 | Medium | Mobile operation surfaces | Yes (read-only, unused) | No | No (consumer needs APK) | **SAFE FOUNDATION → implement** |
| CB-15 | Collector Mobile with zero facilities / operation menu | — | App expects facility menu | Mobile UI + signed APK | — | Medium | Operation-only collectors in the field | No (APK) | No | APK release | **BLOCKED MOBILE** |
| CB-16 | Market Fees writer | Rulebook §4 (CT), Q1 grouping | Classification + assignment only; **no collection-point/source model on baseline** | Approved collection points, rates, CT custody, allocation | Rates/points not confirmed | High | Market Fees collection | No | Yes | Yes | **BLOCKED BUSINESS RULE** |
| CB-17 | Vegetable/Fruit writer | IA-046 (Whole=OR, Daily=CT), contextual policy (`2d42a39a`) | Contextual instrument policy only; **no source/assessment model on baseline** | Space/renter source, rate, mode-aware writer | Rate/source not confirmed | High | Vegetable collection | No | Yes | Yes | **BLOCKED BUSINESS RULE** |
| CB-18 | Landing/Berthing writer | Rulebook §4 (CT) | Classification + assignment only; **no activity/source model on baseline** | Source identity, fee basis, CT custody, offline writer | Fee/rate not confirmed | High | Landing collection | No | Yes | Yes | **BLOCKED BUSINESS RULE** |
| CB-19 | Transfer Large Cattle | IA-044/IA-048, ADR-006 | Operation assignment only; `RevenueClassificationCodes` has no `TRANSFER_LARGE_CATTLE` code and there is **no configurable-service model on baseline** | Governed service definition, approved amount policy, OR custody | Fee schedule/form pending | High | TLC collection | No | Yes | Yes | **BLOCKED BUSINESS RULE** |
| CB-20 | Handoff matrix accuracy | — | Earlier matrix treats candidate-only models as present | Correction note | — | Low | Planning accuracy | Yes (docs) | No | No | **Record in handoff** (done in this audit) |
| CB-21 | WCF Mobile device queue / payload v1 on APK | Phase 5A | Server contract ready; APK capability unproven | Signed APK, per-device evidence | — | High | WCF activation | No | No | APK | **BLOCKED MOBILE** |
| CB-22 | WCF source activation | ADR-002/Q43 | Control plane exists; no real source Canonical | Scoped reconciliation and approval | Clint approval per scope | High | Canonical WCF cash | No | No | Yes | **BLOCKED CUTOVER** |
| CB-23 | Tabo canonical | IA-045 (OR from 2026-09-27) | TPM shadow reconciliation only | OR custody, writer, cutover | — | Medium | Tabo canonical cash | No | No | Yes | **BLOCKED CUTOVER / DOCUMENT CUSTODY** |
| CB-24 | ECF canonical | IA-048 (OR, direct approved amount) | ECF Web draft/Composer OR path exists; source Legacy | Cutover, OR custody lifecycle | — | Medium | ECF canonical cash | No | No | Yes | **BLOCKED CUTOVER** |
| CB-25 | Municipality code length | Phase 5A note | `Municipality.Create` accepts codes longer than the 30-char column; onboarding validator only checks non-empty | Fail-fast validation | Existing schema | Low | Onboarding reliability | Yes | No | No | **READY (low priority)** — implement only if time remains |

## Implementation order for this run

CB-01 → CB-04 → CB-13 → CB-14 → CB-09 (shadow) → CB-25, each as its own commit, then the overnight handoff.
