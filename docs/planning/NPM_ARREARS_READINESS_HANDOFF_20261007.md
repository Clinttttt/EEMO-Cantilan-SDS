# NPM arrears receipt cleanup and Collect All readiness

Feature branch: `codex/mobile-npm-micro-polish-followup`.
Starting HEAD: `4b7612ce`, preserving the accepted Mobile commit `f1be41df` and subsequent Web polish.

## Arrears confirmation

`Market.razor` no longer renders the physical OR field in the closed-month/current-month arrears confirmation sheet.
Both existing typed requests receive `ORNumber = null`:

- `SettleMobileNpmMonthRequest(StallId, Year, Month, ORNumber)`.
- `SettleMobileNpmDaysRequest(StallId, Dates, ORNumber)`.

No fake serial is supplied. Amounts, selected days, source identity, attribution, arrears layout and navigation remain unchanged.
`NpmWholePayment.razor`, its CSS and the accepted opt-in Month/Year popovers are unchanged.

## Actual local diagnosis

Read-only HTTP requests for active Collector `personal1`, Cantilan, on server date `2026-10-07`:

- `GET /api/mobile/menu`: HTTP 200, NPM assigned/available, **CanonicalCollection=false**.
- `GET /api/mobile/npm/collections?year=2026&month=10`: HTTP 200, zero collected, five pending.
- `GET /api/mobile/npm-daily-batch/readiness`: HTTP 200; all five occupied stalls below were blocked with **SourceStillLegacy**.

| StallId | Stall no. | Recorded occupant | CanCollect | ReasonCode | EffectiveCharge |
|---|---|---|---|---|---|
| `40042ec6-2ad3-4179-8c81-65ac93ff276b` | 1 | Ana Reyes | false | SourceStillLegacy | 30.00 |
| `9cd31ce5-53f1-4715-a31f-051c111a088c` | 23 | Vincent E. Doloriel | false | SourceStillLegacy | 30.00 |
| `83c47f39-e90b-4c27-8869-8dcb2fb1ef4d` | 1 | Lorna Buenades | false | SourceStillLegacy | 30.00 |
| `1a88c1a8-febe-4f04-b9b5-0e6e581d3995` | 1 | Pantom Dant | false | SourceStillLegacy | 30.00 |
| `081fde6e-6e99-4299-a7f8-09f12d7904b3` | 2 | Delta Rall | false | SourceStillLegacy | 30.00 |

These same IDs were `IsCollectableToday=true`, `IsCollectedToday=false` on the ordinary Daily read.
Ordinary Daily currently permits the legacy writer before the approved canonical switch; that is not an SRC collection.
The batch promises independent canonical Collections/SRCs and must not bypass the switch. No live payment, activation,
policy, rate or database mutation was performed. Enabling the approved canonical NPM daily policy/cutover remains a
configuration prerequisite for this local tenant; it was not silently enabled by this fix.

## Readiness contract correction

No route or DTO shape changes. Existing `NpmDailyBatchReadiness` retains `Sources`, `EligibleCount`,
`AlreadyCollectedCount`, `CanCollectAll` and `EligibleTotal`.

The old readiness response additionally listed 25 vacant stalls not present in the ordinary Daily round.
`SourcesAsync` now lists only sources with a month-answering occupancy, using the existing authoritative owner result
from `PreviewCore`. Explicit quote requests still validate every supplied ID and cannot post vacant/invalid sources.

A paid day's reason is now `AlreadyCollected` before considering the canonical cutover, so completed legacy payments
are truthfully reported as collected. This does not grant new posting eligibility or change any charge.

Mobile uses the server source facts:

- At least one `CanCollect=true`: enabled `Collect All · N stalls` (singular for one).
- Empty round or all returned sources `AlreadyCollected`: disabled `Nothing left to collect today`.
- Unpaid sources blocked by policy/source/rate authority: disabled `Collect All unavailable` with concise mapped copy.
- Internal reason codes are never displayed directly. Unknown reasons use neutral review wording.

Do not enable from the page's pending count, generate SRCs on the client, or automatically change the canonical switch.
The existing quote, deterministic child IDs, atomic writer, replay, financial classifications and amount rules are unchanged.

## Regression proof and validation

Four authenticated API-host/PostgreSQL regression cases failed before the readiness fix:
three included a vacant stall incorrectly (six sources instead of five), and one reported a paid legacy day as
`SourceStillLegacy` instead of `AlreadyCollected`.

Coverage includes closed-month and current-month confirmation without OR, accepted Whole Payment popovers,
five eligible/one paid/five paid Mobile states, five blocked reasons, HTTP ordinary-Daily/readiness agreement,
server charge equality, vacant exclusion, legacy authority isolation, existing batch posting/replay/stale protection.

Final validation:

- Focused Mobile arrears/Whole Payment/Collect All: 18 passed, no failures/skips.
- Full Mobile component suite: 61 passed, no failures/skips.
- Focused PostgreSQL `NpmDailyCanonicalTests`: 25 passed, no failures/skips.
- Focused Unit NPM/DailyCollection slice: 345 passed, no failures/skips.
- Windows Mobile Release: passed, 16 existing warnings, zero errors; Android not run.
- API Release: passed, zero warnings/errors on the final build.
- `git diff --check`: passed. No scoped CSS files were touched.
- Whole Payment and shared MobileChoice files have no diff from accepted `f1be41df`.

Full backend suites were not run; the affected NPM suites were run separately as above.
No schema migration, Web edit, production operation, push, merge or APK publication is part of this follow-up.
