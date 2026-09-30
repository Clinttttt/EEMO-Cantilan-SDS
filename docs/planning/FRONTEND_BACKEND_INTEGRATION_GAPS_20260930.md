# Frontend / Backend Integration Gaps — 2026-09-30

**Lane:** Claude UI (`interface-v3/claude-ui`, worktree `claude-ui-v3`).
**Integrated:** `backend/claude-gap-completion-v3` at `354a6149` (merge `7c682b23`; `354a6149` verified as an ancestor).
**Authority:** Engineering integration record. It is not business authority; open business questions stay with
Clint / Core Brain (see the backend handoff `CLAUDE_BACKEND_OVERNIGHT_HANDOFF_20260930.md`).

## Merge notes

- The only conflict was `CLAUDE.md` (add/add). Resolved to the **UI-lane** file; the backend-lane `CLAUDE.md` stays
  on the backend branch. UI skills (`.claude/skills/stalltrack-ui-*`) are unchanged; the backend skills
  (`stalltrack-backend-*`, `stalltrack-financial-safety`) arrive under their own names.
- No backend Domain / Application logic / Infrastructure / migration file was edited by this lane.

## Cross-project client-contract files (transport only)

Each file below adds a call to an endpoint that **already exists** on the merged API. No server behaviour changed.

| File | Addition | Endpoint consumed |
|---|---|---|
| `EEMOCantilanSDS.Application/Common/Interface/ApiClients/IMobileApiClient.cs` | `GetOperationCapabilitiesAsync()` | `GET api/Mobile/operations/capabilities` |
| `EEMOCantilanSDS.HttpClients/ApiClients/MobileApiClient.cs` | implementation | same |
| `EEMOCantilanSDS.Mobile.Core/Services/CachingMobileApiClient.cs` | read-through cache keyed `operations\|capabilities\|{today}`; `operations` added to the write-invalidated prefixes | same |
| `EEMOCantilanSDS.Application/Common/Interface/ApiClients/ICollectorsApiClient.cs` | `GetCollectionOperationsAsync(Guid)` | `GET api/Collectors/{id}/collection-operations` |
| `EEMOCantilanSDS.HttpClients/ApiClients/CollectorsApiClient.cs` | implementation | same |

`CreateCollectorCommand` / `UpdateCollectorCommand` already carry `OperationCodes`, so create/update needed no client change.

## Integration contract matrix

| Area | Backend contract (merged) | Frontend now | State |
|---|---|---|---|
| WCF Web | `POST api/wcf-collections/collections` rejects new Web-origin posts (CB-01); obligations, activity, reconciliation reads | No collect action; readiness line from server truth: outstanding obligations open for canonical settlement (`CanCollectCanonical`) and in-office Cash Ticket stock "awaiting assignment" (previously mis-described as "available for field collection") | INTEGRATED |
| Collectors Web | Create/Update accept `OperationCodes`; ≥1 facility **or** operation; `GET/PUT {id}/collection-operations` (SuperAdmin) | Separate "Assigned Operations" group; facilities optional; operation-only collectors allowed; edit loads the server list and sends `null` (unchanged) when it cannot be read; table labels "No facility · operations only" | INTEGRATED (see G-2, G-3) |
| Collector Mobile — menu | `GET api/Mobile/operations/capabilities` | "Assigned Operations" status cards (Ready / Not yet + reason); no collect action; read failure = "could not be checked", not "none" | INTEGRATED (unreleased; needs a signed APK) |
| Collector Mobile — WCF | Capability `WCF` status + per-obligation `CanCollectCanonical` + posting revalidation | Capture enabled only when capability `IsCollectible` **and** the quote is canonical; `NotAssigned`, `PendingCutover`, `NeedsDocument`, … shown as plain reasons | INTEGRATED (unreleased) |
| Accountable Forms | `api/accountable-forms/books`, `cash-tickets/assign` (unchanged by backend) | Unchanged; verified against the merged controller routes | VERIFIED |
| Weight & Measure | `FeeTypeBreakdownDto.MeatKilos` / `MeatWeightMeasureAmount` (frozen); Fish kilos only; shadow query `GetNpmWeighingShadowReconciliationQuery` (no endpoint) | Meat: frozen kilos and amount from the server. Fish: kilos per stall, amount **"Rate evidence unavailable"** — the report's `FishFeeAmount` (kilos × rate at read time) is no longer shown as collected money | INTEGRATED (partial; see G-1) |
| Fish/Meat Vendor Fee | none (U-1) | Unavailable state (unchanged) | BLOCKED BUSINESS RULE |
| Market Fees / Vegetable-Fruit / Landing-Berthing / Transfer Large Cattle | Operation assignment only; capability `Unsupported` | Truthful blocked states; no record/collect actions; operations assignable to collectors | BLOCKED (no writer) |
| Canonical Monthly Income | `GET api/canonical-reports/monthly-income` | Not consumed (awaits Core Brain approval for a Reports page) | DEFERRED |
| CB-27 wording tests | — | `SectionNamesComeFromTheOfficeTests` pass (5/5); fixed by the V3 ECF/WCF Accounts/Report rewrite | RESOLVED |

## COLLECTOR MOBILE READINESS

| Operation | Assignable (Web) | Mobile shows | Collectible today | What unblocks it |
|---|---|---|---|---|
| NPM / facilities | Yes (facilities) | Facility menu (unchanged) | As before | — |
| WCF | Yes | Status card + gated capture in Market misc sheet | Only when capability `Ready` **and** source Canonical; today the backend reports `PendingCutover` | Q43 scoped Water cutover (CB-22), CT assignment to the collector, CB-26 decision, signed APK |
| Vegetable / Fruit Space Rental | Yes | Status card "Not available on Collector Mobile yet" | No (`Unsupported`) | Source model + mode-aware OR/CT writer (CB-17) |
| Landing / Berthing | Yes | Status card | No (`Unsupported`) | Source model + CT writer (CB-18) |
| Transfer Large Cattle | Yes | Status card | No (`Unsupported`) | Governed service + classification + OR custody (CB-19) |
| Market Fees | Yes | Status card | No (`Unsupported`) | Collection points, rates, CT writer (CB-16) |
| Weight & Measure | No (NPM-derived) | Meat kilos captured in NPM (backend) | Via NPM only | Real adapter blocked on custody/cutover (CB-09) |

No APK was built for release, signed, uploaded or published; `LatestVersion` was not touched. The Android target was
compiled locally only to prove the Mobile changes build.

## BACKEND GAPS

**G-1 — W&M shadow comparison has no endpoint**
- current behavior: `GetNpmWeighingShadowReconciliationQuery` exists in Application with tests, but no controller exposes it.
- required behavior: a read-only Head/Admin endpoint (e.g. `GET api/canonical-reports/npm-weighing-shadow?from&to`) returning `NpmWeighingShadowReconciliationDto`.
- why the UI cannot truthfully implement it: the classified comparison (projected rows, unresolved Fish rows, difference) is server logic; recomputing it in the Web would duplicate financial rules.
- exact frontend contract needed: the endpoint above plus `Task<Result<NpmWeighingShadowReconciliationDto>> GetNpmWeighingShadowAsync(DateOnly from, DateOnly to)` on a typed client.

**G-2 — No collector-independent operation catalog**
- current behavior: the supported operations (code + name) are returned only by `GET api/Collectors/{id}/collection-operations`, which needs an existing collector.
- required behavior: `GET api/Collectors/collection-operations` returning the catalog for the create form.
- why the UI cannot truthfully implement it: the create form currently lists the Domain `CollectorOperationCodes` with names mirroring the server catalog; a renamed or added operation would drift.
- exact frontend contract needed: `Task<Result<IReadOnlyList<CollectorOperationAssignmentDto>>> GetCollectionOperationCatalogAsync()` (Assigned = false).

**G-3 — Collector list has no operation codes**
- current behavior: `CollectorListDto` carries facilities only.
- required behavior: include `IReadOnlyList<string> OperationCodes`.
- why the UI cannot truthfully implement it: the register can only say "No facility · operations only", not which operations, without N extra calls.
- exact frontend contract needed: `CollectorListDto.OperationCodes` (additive, defaulted).

**G-4 — Fish weighing amount is read-time priced in legacy reports**
- current behavior: `FeeTypeBreakdownDto.FishFeeAmount` = kilos × the current `_npmFishRate` (`FacilityReportsRepository.Breakdowns.cs`); `NpmFacilityDetailDto.FishWeightMeasureCollected` is documented as the same kilo-based amount.
- required behavior: a Fish weighing amount only where a rate/amount was frozen (future source change, needs approval), or an explicit "unresolved" flag.
- why the UI cannot truthfully implement it: historical rate evidence does not exist; the W&M page therefore shows "Rate evidence unavailable". Other legacy NPM reports still print the read-time figure — Core Brain should decide whether those reports need the same treatment.
- exact frontend contract needed: `FishWeighingAmountResolved: bool` (or the shadow endpoint G-1).

**G-5 — Web WCF posting method remains on the typed client**
- current behavior: `IWcfCollectionsApiClient.PostAsync` still targets `POST api/wcf-collections/collections`, which the server now rejects. No Web page calls it.
- required behavior: remove or mark obsolete when Core Brain closes CB-03.
- exact frontend contract needed: none; cleanup only.

## Test notes

- Unit suite: 2277/2277 passed (run alone).
- Component suite: 579/580 before the date fix; `RevenueSetupTests.AsOfDate_ReloadsRegisterWithRequestedBusinessDate` hard-coded 2026-09-30, which is today, so it collided with the initial "today" load — made date-independent.
- **Pre-existing failure (not this slice):** `VendorRegistryTerminologyTests.TpmPageRetainsTemporaryVendorTerminology` expects "Vendor Attendance / Vendor Name / Add Vendor" in `TPM.razor`; those strings were already gone at baseline `024556fd` (Tabo rewrite `cca17c16`). Needs a Core Brain terminology decision before the test or page changes.
