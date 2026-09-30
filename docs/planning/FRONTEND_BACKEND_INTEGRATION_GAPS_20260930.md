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

**G-6 — Contextual instrument policy is not exposed**
- current behavior: `GET` revenue classifications returns one `EffectivePolicy` (Default context). Vegetable/Fruit's `VegetableWholePayment` (OR) and `VegetableDailyTransaction` (CT) policies are not in the DTO.
- required behavior: the classification DTO lists effective contextual policies (`Context`, instrument, effective date).
- why the UI cannot truthfully implement it: showing the Default row would present a single (historical CT) instrument for a two-mode line, so the Vegetable / Fruits workspace states the IA-046 ruling instead of reading policy rows.
- exact frontend contract needed: `RevenueClassificationDto.ContextualPolicies: IReadOnlyList<(RevenuePolicyContext Context, RevenueClassificationPolicyDto Policy)>`.

## Runtime diagnostic — IDX10703 (local JWT)

- **Cause:** `AuthenticationExtensions.ConfigureServices` builds `new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))`. In the committed `EEMOCantilanSDS.Api/appsettings.json`, `Jwt:Key`, `Jwt:Issuer` and `Jwt:Audience` are all empty strings (by design — no secret in source). There is no user-secrets store for the API's `UserSecretsId` (`9c52f74a-9378-4de3-b669-9ac14e61ef44`) on this machine and no `Jwt__Key` environment variable, so the key is zero bytes.
- **Also required:** `TokenService` signs with `HmacSha512`, so the key must be **at least 64 bytes**. `Issuer`/`Audience` should be set too (validation is on for both).
- **How Clint supplies it locally (not committed):**
  ```
  dotnet user-secrets set "Jwt:Key" "<random string of 64+ characters>" --project EEMOCantilanSDS.Api
  dotnet user-secrets set "Jwt:Issuer" "<issuer>" --project EEMOCantilanSDS.Api
  dotnet user-secrets set "Jwt:Audience" "<audience>" --project EEMOCantilanSDS.Api
  ```
  (or environment variables `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`). The API both issues (`TokenService`) and
  validates the token from this one configuration, so any consistent local values work; if another worktree or the
  main checkout already has working values, reuse them — user secrets are per `UserSecretsId`, shared by every
  checkout of the API on the machine. A new key only invalidates existing local sessions.
- No secret was generated, printed or committed; authentication, `[Authorize]` and endpoint policies are unchanged.

## /operations directory — row audit (2026-09-30)

| Group | Row | Receipt shown | Workspace | Source of truth | Notes |
|---|---|---|---|---|---|
| Income from Market | Market Fees | CT | `/operations/market-fees` | Classification `MARKET_FEES` | Not recorded (CB-16) |
| | Electricity Consumption Fees (ECF) | OR | `/operations/ecf` | ECF obligations | Web OR draft path exists |
| | Water Consumption Fees (WCF) | CT | `/operations/water-consumption-fees` | WCF obligations | Mobile-only collection |
| | Tabo | OR (IA-045) | `/facility/tpm` when configured | Facility record | |
| | Fish / Meat Vendor Fees | OR | `/operations/fish-meat-vendor-fees` | none | U-1 |
| | Landing / Berthing | CT | `/operations/landing-berthing` | Classification | Not recorded (CB-18) |
| | Transportation Fees | CT | `/facility/trm` when configured | Facility record | |
| | Weight & Measure / Registration | OR | `/operations/weight-and-measure` | NPM weighing facts | Fish unresolved |
| | Transfer Large Cattle | — (not set) | **new** `/operations/transfer-large-cattle` | none | No classification (CB-19) |
| | Ice Plant | — | `/facility/ice` when configured | Facility record | Q-2 |
| Rent Income — Stall Rental | NPM / NCC / TCC / BBQ | OR | facility pages | Facility record | "couldn't be loaded" row on failure |
| | Arrears | — | none ("No workspace yet") | Classification `ARREARS` | Q-1 |
| Space Rental | Vegetable / Fruits | OR whole / CT daily (IA-046) | **new** `/operations/vegetable-fruit` | Ruling; G-6 | Not recorded (CB-17) |
| | Kanmanggay | OR | **new** `/operations/kanmanggay` | none | No classification |
| | Lot Rental — Fiesta / Araw | OR | **new** `/operations/fiesta-araw` | none | No classification |
| | Fines | OR | **new** `/operations/fines` | Classification `PENALTIES_AND_FINES` | Q-3 |
| Other operations | Slaughterhouse, office-defined facilities | OR / — | facility pages | Facility record | Section shown only when one is configured (Q-4) |

Wording: "None in StallTrack" is replaced by the actual reason — "No workspace yet", "Not configured for this
office", "Couldn't be loaded" (facility record unavailable) or "Loading…". When the facilities API fails, facility-backed
lines say "Couldn't be loaded" and the rent-income group shows one "couldn't be loaded" line instead of silently
omitting the facilities; no fallback facility catalog is used.

### Questions for Core Brain

- **Q-1 Arrears placement:** the Monthly Income sheet lists Arrears under Rent Income, but arrears are settled through
  each facility's accounts (and NPM arrears on Mobile). Is Arrears an independent report line with its own workspace,
  or a reporting classification of facility collections? Left without a workspace.
- **Q-2 Ice Plant:** no receipt type is recorded for Ice Plant and its row is a facility workspace. Which instrument
  (OR/CT) and which income group does it belong to?
- **Q-3 Fines:** the sheet places Fines under Space Rental; the classification is Penalties/Fines. Confirm the group,
  and whether stall-payment penalties already recorded elsewhere count toward this line.
- **Q-4 Other operations:** Slaughterhouse and office-defined facilities are shown in a separate section that is not a
  Monthly Income group. Confirm where Slaughterhouse income sits on the sheet.

## Test notes

- Unit suite: 2277/2277 passed (run alone).
- Component suite: 579/580 before the date fix; `RevenueSetupTests.AsOfDate_ReloadsRegisterWithRequestedBusinessDate` hard-coded 2026-09-30, which is today, so it collided with the initial "today" load — made date-independent.
- **Pre-existing failure (not this slice):** `VendorRegistryTerminologyTests.TpmPageRetainsTemporaryVendorTerminology` expects "Vendor Attendance / Vendor Name / Add Vendor" in `TPM.razor`; those strings were already gone at baseline `024556fd` (Tabo rewrite `cca17c16`). Needs a Core Brain terminology decision before the test or page changes.

## Additional gaps recorded at the operational functionalization checkpoint

BACKEND GAP: Market Fees / Landing report pages
- current behavior: the report pages still print "—" because the Monthly Income source is not cut over (CB-06).
- required behavior: read canonical operation collections once the cutover is authorized.
- why the UI cannot truthfully implement it: showing canonical totals beside legacy reports would create two authorities.
- exact frontend contract needed: a Monthly Income reader endpoint declared authoritative for the period.

BACKEND GAP: Slaughterhouse custom animal rate
- current behavior: `RecordSlaughterCommandHandler` accepts a collector-typed `CustomRate` for AnimalType.Other.
- required behavior: an approved rate or Head-approved amount ceiling (policy needed from Clint).
- why the UI cannot truthfully implement it: inventing the governance would fabricate a financial rule.
- exact frontend contract needed: an approved definition list for non-standard animals, if approved.

Other notes: Fish reads for legacy rows remain read-time priced (unchanged); a penalty-only OR is not permitted; collector totals rollup across legacy and operation collections is undecided.

## Final completion gaps (Mobile V3 dependencies and remaining backend work)

BACKEND GAP: Collector Mobile Transportation
- current behavior: the server exposes vehicle classes with rates in force in the governed-service terms and accepts `VehicleClassCode` on the governed sync payload; the legacy Mobile trip screen is unchanged.
- required behavior: Mobile V3 offers the class list, shows the approved rate, and posts the class with the physically issued Cash Ticket.
- why the UI cannot truthfully implement it now: keep the service `MobileEnabled=false` until then, or a collector without a class would have a physically issued CT quarantined.
- exact frontend contract needed: `GET terms` -> `VehicleClasses[{Code,Name,Amount}]`; sync `GovernedService` op with `VehicleClassCode`.

BACKEND GAP: Collector Mobile Slaughterhouse custom animal
- current behavior: the server refuses an animal the Head/Admin has not approved and any rate other than the approved one; Mobile still shows typed animal and rate inputs.
- required behavior: Mobile selects from `GET api/slaughter/animal-rates` and shows the approved rate read-only.

BACKEND GAP: mixed-period official Monthly Income
- current behavior: the canonical reader is canonical-only and lists each source's reporting authority; legacy classified totals are not merged.
- required behavior: an assembled official view that adds legacy money only for sources whose authority is legacy for that period and canonical money only where canonical, using `CollectionSourceAuthorityMap`.
