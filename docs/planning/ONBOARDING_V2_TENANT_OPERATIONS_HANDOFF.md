# Onboarding V2 — tenant operations handoff

Written 2026-10-12. Scope: modernise the municipality onboarding so an LGU declares its own facilities and intended revenue
operations under its own names, without changing how StallTrack classifies or bills anything. Not deployed; no push; no
production database touched.

## Branches (all local, unpushed)

| Repo | Branch | Base |
|---|---|---|
| `platform` | `feat/onboarding-tenant-operations-v2` | `main` @ `db94c2e` |
| `eemo` (worktree `C:\dev\stalltrack-v2-worktrees\onboarding-ops`) | `codex/onboarding-initial-operations-v2` | `origin/master` @ `63a70e86` |

The ECF/WCF working tree (`C:\dev\stalltrack\eemo`, branch `codex/utility-workspace-ecf-wcf`) was never edited, staged or
stashed. This work lives in its own worktree and shares **no file** with it (see "Integration" below).

## Final architecture

```
LGU workspace  ──Save/Submit──▶  OnboardingDraft.ConfigJson (opaque jsonb, unchanged table)
   facilities[] (+ catalogKey)                │
   administrator, branding                    │  server reads ONLY operations{} (validate on save + submit)
   operations { version:1, items[] }  ◀───────┘
                                              ▼
Operator Validation / Activation (admin console)
   toReviewConfig → mapRequestToCommand → ActivateMunicipalityCommand   (operations are NOT in the command)
                                              ▼
Backend activation (atomic, unchanged shape) ──▶ Head Portal (existing pages)
```

**No schema change, no migration.** A declaration is *initial intent kept in the onboarding draft*; it is not configuration,
not a Collector assignment, not posting authority.

### Declaration contract (additive, versioned)

```json
"operations": { "version": 1, "items": [
  { "operationCode": "FIESTA_ARAW_LOT_RENTAL", "selected": true, "localDisplayName": "Pista Lot Rental" } ] }
```

* `operationCode` — stable server identity. `localDisplayName` — presentation only (≤ 80 chars, no control characters, never
  equal to another operation's standard name or another selected operation's name). It is never read to decide what an
  operation is, and is never a source code, classification, instrument or rate key.
* Absent block ⇒ nothing declared (every pre-existing draft). Unknown *top-level* fields are preserved on save. An
  unrecognised operation code is dropped by the workspace with a visible notice (the server rejects it on save).

### Supported initial operation catalog (11)

| Group | Neutral label | Code |
|---|---|---|
| Market | Fish / Meat Vendor Fee | `FISH_MEAT_VENDOR_FEE` |
| Market | Weight & Measure | `WEIGHT_AND_MEASURE` |
| Market | Market Fees | `MARKET_FEES` |
| Space & rental | Municipal Space Rental | `KANMANGGAY_SPACE_RENTAL` (Cantilan's label: Kanmanggay Space Rental) |
| Space & rental | Event / Temporary Lot Rental | `FIESTA_ARAW_LOT_RENTAL` (Cantilan: Fiesta / Araw Lot Rental) |
| Space & rental | Vegetable / Fruit Space Rental | `VEGETABLE_FRUIT_SPACE_RENTAL` |
| Utility | Electricity Consumption Fees | `ECF` |
| Utility | Water Consumption Fees | `WCF` |
| Other | Landing / Berthing | `LANDING_BERTHING` |
| Other | Transportation / Parking | `TRANSPORTATION` |
| Other | Transfer Large Cattle | `TRANSFER_LARGE_CATTLE` |

Deliberately **not** selectable: Stall Fees, Commercial Space Rental, Slaughterhouse, Terminal, Weekly Market. Each is owned by a
facility the office adds (`NPM_DAILY`, monthly-rental facility, `SLAUGHTERHOUSE`, `TRM`/`TERMINAL`, `TABO`); offering both would be
two answers to one question. The workspace shows them as "Already covered by your facilities". Existing internal codes were not
renamed — the two Cantilan-named codes keep their historical identity and only the *neutral wording* is new.

## Facility mapping corrections (the critical fix)

Before: `candidateCode()` read the facility **name** with regexes, fell back to `TCC` for anything unrecognised, and on a
collision handed the facility the first *free canonical code* (`ALL_CODES.find`). A second Public Market could become `TCC`.

Now (`activation.mapper.ts`):

* Identity comes from the workspace's `catalogKey` (`public_market→NPM`, `commercial→TCC`, `barbecue→BBQ`, `iceplant→ICE`,
  `slaughterhouse→SLH`, `terminal→TRM`, `weekly→TPM`). The name is never read for new drafts.
* `other` (an office's own facility) → reserved `Custom1…Custom5`, archetype `MonthlyRental`. A sixth is a **blocking error**
  with an actionable message (add it from the Head Portal after activation).
* Two facilities of one standard type, an unknown `catalogKey`, a billing type that contradicts the identity, or an
  unrecognised billing type on a legacy draft are **blocking errors** — never re-coded.
* Legacy drafts with no `catalogKey` still map by the old type/name reading, but now with a warning ("read as TCC…"); an
  unrecognised monthly rental becomes a custom slot, not `TCC`.
* Backend: `ActivateMunicipalityCommandValidator` now refuses a `Custom*` code with any archetype other than `MonthlyRental`.
  (Duplicate-code refusal already existed.)
* NCC is no longer reachable from a new draft (the catalog has one "Commercial Center" entry → TCC); a second commercial center
  is an office's own facility. Only a legacy draft whose name says "new commercial" still resolves to NCC.

## Rates that were never real (Phase A)

* **Monthly rental / custom facility:** the workspace asked for a facility-wide "Base rate" the billing engine never reads
  (`FacilityRateKeys.For` returns none for TCC/NCC/BBQ/ICE/Custom). Removed; the card now says rent is set per space after
  activation, and a figure an old draft carries is reported to the operator as "not filed".
* **Electricity / water add-on:** `ElecPerKwh` / `WaterPerCubicMeter` are read by **no** billing path (only a label DTO). The
  mapper no longer files them (previously zero-amount rows for a "metered" utility). The workspace add-on entry is replaced by
  a note pointing at Revenue Operations; an add-on an old draft holds stays visible/removable, and a stated positive amount is
  surfaced as a warning rather than vanishing. Financial behaviour is otherwise unchanged.
* **Blank required rates no longer become 0:** NPM (no priced area), TPM, TRM, SLH hog/large, custom animals now block with
  "return the draft for LGU review". An explicit `0` still passes with a warning.

## Activation contract

`ActivateMunicipalityCommand` is **unchanged in shape**. `FacilityCodeStr` (platform) gains `Custom1…Custom5` to match the
backend enum. Declared operations are shown to the operator ("Operation setup required after activation") and do **not**
block activation, enable a service, assign a Collector, register a source, or file a rate.

Server-side (`OnboardingOperationDeclarations`): validates the `operations` block on `UpdateOnboardingConfig` (save) and
`SubmitOnboarding`. Versionless drafts pass; malformed blocks fail with a clear message; unknown properties are ignored.

## Post-activation (Phase C) — what exists, what does not

Existing Head Portal paths (verified from `@page` routes): `/settings/facilities` (Facility Configuration: names, section labels,
fee rates), `/collectors` (accounts + operation assignments), `/office-profile`, and per-operation workspaces
`/operations/{ecf, water-consumption-fees, fish-meat-vendor-fees[/manage], weight-and-measure, market-fees, landing-berthing,
transportation, transfer-large-cattle, kanmanggay, fiesta-araw, vegetable-fruit, terminal}`. A selected operation is therefore
set up there; nothing about onboarding freezes those pages.

**BACKEND GAP — tenant display labels after activation**
- *Current behaviour:* the `localDisplayName` an office declares is kept only in its onboarding draft. Operation names shown in
  the Head Portal (`Operations.razor`, `Kanmanggay*.razor`, `RevenueSourceCatalog`) are fixed in code; no tenant label store exists.
- *Required behaviour:* a Head can set/see a tenant-level display label per operation code, shown on portal screens and
  receipts, with canonical codes/classifications and historical receipt evidence unchanged.
- *Why the UI cannot truthfully implement it:* there is nowhere authoritative to persist it (browser storage is forbidden), and
  a label must not be parsed to infer behaviour.
- *Contract needed:* additive `TenantOperationLabel(MunicipalityId, OperationCode, DisplayName ≤80)` (one row per tenant/code,
  unique index, audit fields) + `Get/Set` queries/commands for the Head, read by the Client's operation-name rendering with the
  neutral name as fallback. Requires an EF migration (additive, nullable-safe, no data rewrite) — **not built here**.

Also not built: a post-activation "Initial Setup" readiness summary. Real, server-supported readiness semantics (Not configured /
Needs setup / Ready) do not exist per operation, and a client-side one would fake Ready. Declared intent remains readable from
the retained `OnboardingDraft` for whoever builds it.

## Compatibility

* Old versionless drafts load unchanged (tested); facilities, sections, Head, branding, slaughterhouse data are untouched.
* Existing active tenants (Cantilan and others): nothing is migrated, rewritten or re-seeded; no code path touches them.
* No hidden feature flag is set by a declaration (integration-tested: zero assignments / governed services / obligation
  accounts / utility bills after activation).

## Migration status

None. No EF migration, no model change. **Deployment order:** EEMO API (additive validation only) may deploy before or after the
platform; neither requires the other, because the platform tolerates an older API (it never sends a new field) and the API
tolerates an older platform (it sees no `operations` block). The one API behaviour the platform now relies on is rejection of a
malformed `operations` block at save, which an older API simply would not enforce.

## Tests (actual results)

| Suite | Result |
|---|---|
| Platform `admin` (vitest) | 84 passed (61 existing + 23 new) |
| Platform `landing` (vitest) | 32 passed (existing updated; 10 new in `onboarding-operations.spec.ts`) |
| Platform `nx build admin` / `nx build landing` (production, type-checks templates) | both succeed |
| EEMO unit (`EEMOCantilanSDS.UnitTest`) | 2583 passed, 0 failed, 0 skipped |
| EEMO onboarding unit subset | 93 passed |
| EEMO `OnboardingActivationTests` (Testcontainers PostgreSQL 16, disposable) | 6 passed |
| `eslint` | one pre-existing error (`activation.mapper.ts` `\-`); new warnings only (non-null assertions in specs, unused destructure names) |

`dotnet build EEMOCantilanSDS.slnx --configuration Release` (EEMO worktree): succeeded, 0 errors.

Existing landing specs that asserted "a monthly rental must have a rate" were rewritten to the corrected rule (terminal used as the
unfinished example). Component tests / the full integration suite were not run (unrelated to this change; the existing onboarding
integration tests were not re-run beyond the new file).

## Known limitations

1. Local display names are captured and reviewed but **not yet shown** in the Head Portal (BACKEND GAP above).
2. No post-activation readiness summary.
3. The operation catalog exists in three places (server, landing, admin) with a "keep in step" comment; there is no shared lib
   in this repo yet. A server-side catalog endpoint would remove the duplication.
4. The LGU-facing workspace cannot be exercised end-to-end through a browser here (landing needs the live API + token); it is
   covered by component-level tests and a production build, not a manual walkthrough. **Clint should click through
   `/onboarding/:token` once.**
5. NCC is unreachable from a new draft (see above) — intentional, but an LGU that genuinely has two commercial centers gets
   `Custom1` for the second, not `NCC`.

## Conflicts to avoid with the ECF/WCF workstream

This branch edits **only**: `Application/Command/Onboarding/{ActivateMunicipality,SubmitOnboarding,UpdateOnboardingConfig}`
(validator / handler), new `Application/Common/Onboarding/OnboardingOperationDeclarations.cs`, new tests, and this doc. It does
not touch `DependencyInjection.cs`, `CollectionComposerWorkflow*`, `WcfCollectionWorkflow`, utility DTOs/readers/controllers, the
Client, migrations, or `docs/planning/{ACTIVE_WORKSTREAMS,CURRENT_RELEASE_STATE}.md`. The only shared *concept* is the operation
codes `ECF`/`WCF`: this work records them as **declarations only** and relies on no ECF/WCF contract. If the utility agent
introduces an `ECF` entry in `CollectorOperationCodes`, add it to `SupportedCodes` and nothing else changes.
