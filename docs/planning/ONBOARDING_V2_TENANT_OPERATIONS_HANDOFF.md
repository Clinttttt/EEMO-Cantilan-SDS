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

* `operationCode` — stable server identity. `selected` — **required boolean** on every item. `localDisplayName` — an
  **initial declaration used for onboarding review only** (≤ 80 chars, no control characters; a *selected* operation's name may
  not be another operation's standard name or another selected operation's name; an operation may use its own standard name).
  It is not yet stored per tenant and **does not appear in the activated Head Portal, on receipts or on collection documents**;
  the workspace and operator screens say so. It is never read to decide what an operation is, and is never a source code,
  classification, instrument or rate key.
* Absent block ⇒ nothing declared (every pre-existing draft). Unknown *top-level* fields are preserved on save.
* **Unknown or unreadable entries are preserved, not dropped.** An entry the workspace cannot edit (a code from a newer
  version, a repeated code, an item with no boolean `selected`) is held untouched and written back verbatim on every save, so
  an older frontend saving another section cannot erase it. The notice says plainly that the entries are kept (not deleted)
  and that **saving or submitting may be refused** if StallTrack does not recognise them, until the workspace supports them or
  the office discards them. Discarding is a two-step confirmed action ("Discard these saved entries…" → "Yes, permanently
  discard"). The server decides: codes it knows pass; a code it does not know, or a malformed item, is refused with a message
  naming it — a blocked save, never a silent loss. Unknown codes are never mapped to another operation.
* **A whole `operations` block from a newer version is kept byte-for-byte.** If `version` is not 1, `items` is not a list, or
  the block is not an object, the workspace does not edit, normalise or downgrade it: the selector is hidden, an explanation
  is shown, and the block is written back unchanged (also across reload). Facilities, Head and branding stay editable. The
  server refuses an unsupported version, so submitting is blocked until the workspace is updated or the office confirms a
  discard. Block-level fields beyond `version`/`items` on a supported block are written back untouched.
* Validation rules exist in three places and are kept equivalent: server (`OnboardingOperationDeclarations.Validate`), landing
  (`localNameProblem`/`readOperations`) and admin (`parseOperations`). Contract tests in each repo pin the same 11
  `code|label` pairs, so changing one copy fails a test in whichever suite is touched.

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
  that says so plainly. **There is no "add it later" route:** the Head Portal's Add Facility (`AddFacilityCommandHandler`) files a
  facility under a `FacilityCode` and refuses one already configured, so it draws on the same five reserved slots. The
  limit is 5 custom facilities **in total**, onboarding and later combined; the workspace picker says so and shows
  "Limit reached". Add Facility could still add a *canonical* code the LGU does not yet have (e.g. NCC) — that is a Head
  decision outside onboarding and is not offered here.
* Two facilities of one standard type, an unknown `catalogKey`, a billing type that contradicts the identity, or an
  unrecognised billing type on a legacy draft are **blocking errors** — never re-coded.
* Legacy drafts with no `catalogKey` still map by the old type/name reading, but now with a warning ("read as TCC…"); an
  unrecognised monthly rental becomes a custom slot, not `TCC`.
* Backend: `ActivateMunicipalityCommandValidator` now refuses a `Custom*` code with any archetype other than `MonthlyRental`.
  (Duplicate-code refusal already existed.)
* NCC is no longer reachable from a new draft (the catalog has one "Commercial Center" entry → TCC); a second commercial center
  is an office's own facility. Only a legacy draft whose name says "new commercial" still resolves to NCC.
* **A second commercial center** is represented as a custom monthly-rental facility (`Custom1`…). It bills like TCC/NCC/BBQ/ICE
  (stalls, contracts, payments, delinquency are keyed by the facility's Id, not its code), but it is *not* NCC: NCC-specific
  behaviour (extension/corner area locations) and any report or screen keyed on the canonical `NCC` code do not apply to it,
  it has no dedicated `/facility/ncc`-style route (it is reached through `/facility/{slug}`), and it counts against the shared
  five-slot limit. An LGU that needs NCC semantics for its second center is an explicit Head/Backend decision, not something
  onboarding infers.

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
| Platform `admin` (vitest) | 92 passed |
| Platform `landing` (vitest) | 45 passed |
| Platform `nx build admin` / `nx build landing` (production, type-checks templates) | both succeed |
| EEMO unit (`EEMOCantilanSDS.UnitTest`) | 2588 passed, 0 failed, 0 skipped |
| EEMO integration (`OnboardingActivationTests` + `ActiveOnboardingPipeline*`, Testcontainers PostgreSQL 16, disposable) | 16 passed |
| `eslint` (admin + landing workspace) | 0 errors, 16 warnings (non-null assertions in specs, unused destructure names, a `console.ts` warning that predates this work). The earlier `no-useless-escape` error in `activation.mapper.ts` (`/[^0-9.\-]/`) is fixed. |
| `git diff --check` | clean (only LF→CRLF notices) |

`dotnet build EEMOCantilanSDS.slnx --configuration Release` (EEMO worktree): succeeded, 0 errors.

Existing landing specs that asserted "a monthly rental must have a rate" were rewritten to the corrected rule (terminal used as the
unfinished example). Component tests / the full integration suite were not run (unrelated to this change; the existing onboarding
integration tests were not re-run beyond the new file).

## Known limitations

1. Local display names are captured and reviewed but **not yet shown** in the Head Portal (BACKEND GAP above).
2. No post-activation readiness summary.
3. The operation catalog exists in three places (server, landing, admin) with a "keep in step" comment; there is no shared lib
   in this repo yet. A server-side catalog endpoint would remove the duplication.
4. **No browser walkthrough was performed.** `/onboarding/:token` needs a running API plus a real onboarding token, and no
   isolated API/test environment with one was available; none was fabricated. The workspace is covered by component tests and a
   production build only. **Clint should click through a non-production token once** (select operations, name one, save, reload,
   submit, then check Validation/Activation).
5. NCC is unreachable from a new draft (see above) — intentional, but an LGU that genuinely has two commercial centers gets
   `Custom1` for the second, not `NCC`.

## Conflicts to avoid with the ECF/WCF workstream

This branch edits **only**: `Application/Command/Onboarding/{ActivateMunicipality,SubmitOnboarding,UpdateOnboardingConfig}`
(validator / handler), new `Application/Common/Onboarding/OnboardingOperationDeclarations.cs`, new tests, and this doc. It does
not touch `DependencyInjection.cs`, `CollectionComposerWorkflow*`, `WcfCollectionWorkflow`, utility DTOs/readers/controllers, the
Client, migrations, or `docs/planning/{ACTIVE_WORKSTREAMS,CURRENT_RELEASE_STATE}.md`. The only shared *concept* is the operation
codes `ECF`/`WCF`: this work records them as **declarations only** and relies on no ECF/WCF contract. If the utility agent
introduces an `ECF` entry in `CollectorOperationCodes`, add it to `SupportedCodes` and nothing else changes.

## Final state (2026-10-12)

* Platform `feat/onboarding-tenant-operations-v2` — final correction commit `d2bf9ec` (on `326b0a9`).
* EEMO `codex/onboarding-initial-operations-v2` — backend code last changed in `299d3e69`; this doc is the only later change.
* Final verification: admin 92, landing 45, EEMO onboarding unit subset 98 (full unit suite 2588 last run on `299d3e69`; no
  backend code changed since), activation/pipeline integration 16 (disposable PostgreSQL), both production builds succeed,
  eslint 0 errors / 16 warnings, `git diff --check` clean. Whole-solution Release build was last run on `299d3e69`, 0 errors.
* **No browser walkthrough was done** (no isolated API/token available). Someone must click through a non-production token.
* Existing drafts: versionless, block-less and version-1 drafts load unchanged (tested). No other LGU's data is touched
  (integration-tested tenant isolation). Nothing was pushed, merged, deployed or migrated.
