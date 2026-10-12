# ECF / WCF utility workspace — checkpoint handoff (Core Brain V6)

Branch `codex/utility-workspace-ecf-wcf` (base `63a70e86`). Local commits only: nothing pushed, merged or deployed; no production configuration, data or migration touched. `StallTrack:NpmPartPayments:Enabled` stays `false`.

## What exists

- **Eligibility is source-native.** `UtilityServiceEligibility` (`Domain/Common`): a space is an ECF or WCF source when it carries that fee in `Stall.Fees` (NPM spaces remain always-eligible, for back-compatibility). No name matching, no assumption that every vendor has service. Applied in the composer, the Mobile ECF/WCF source discovery, the WCF workflow and the reading handler. Collector scope: assigned facilities (WCF also requires the WCF operation assignment and keeps NPM scope).
- **Server register** `GET api/utility-register` (office only): filtered, counted and paged in the database (page cap 100); statuses Unprepared / Unpaid / Partial / Settled / Direct / NeedsReview are server-derived; a direct source never shows an invented balance.
- **Preparation** `POST api/utility-register/prepare` (Admin/SuperAdmin): single and batch, per-source outcomes, expected-version and duplicate checks, through the existing writers. Creates no Collection, SRC or document.
- **One Web workspace** (`UtilityWorkspace`) for both pages: compact hero, filtered/paged register, selected-source panel (Prepare amount Review→Save), "Prepare multiple amounts" (review then per-row results), compact activity. No permanent "Current collection" panel.
  - ECF page: Record payment (Official Receipt) for prepared **canonical** sources through the existing draft contract (add → review → record under a stable operation id; amount ≤ outstanding; refresh-for-today; discard); "Review authority" for legacy; the unfinished-collection notice appears only when a server draft exists (Resume / Discard).
  - WCF page: Collector Mobile status/enable, legacy migration review, compact activity with search/status filter, reconciliation exceptions. **Web WCF recording stays retired** (Collector Mobile only).
  - Activity rows deep-link to `/collections/activity?src=<SRC>`.
  - `/operations/ecf/accounts` and `/operations/water-consumption-fees/accounts` remain as redirects to the workspace.

## Gated, by design (needs an owner decision)

1. **Web WCF recording** — retired (`WEB_CHANNEL_RETIRED`); un-retiring needs a ruling on who is recorded as collector.
2. **Vendor-registration, Fiesta/Araw and space-account utility sources** — no authoritative service record exists; would need an explicit, Head-authorized, effective-dated association (additive).
3. **Web ECF recording for legacy-authority or direct sources** — only prepared canonical sources can be recorded on the Web; direct amounts are Collector/Mobile.

## Database

No migration was required by V6 (reads and writes use existing tables). Nothing was applied to any persistent database.

## Verification

- Unit 2558 passed · Web component 1045 passed · Mobile component 100 passed · PostgreSQL integration 514 passed, 7 skipped (snapshot-dependent, pre-existing), 0 failed.
- PostgreSQL integration (Testcontainers): proven cross-facility eligibility, same names/stall numbers kept separate, 74-source stable paging, filters/totals, prepared vs direct, tenant/role isolation, mode freeze, partial/over-payment, refusals, batch outcomes, Mobile discovery (see `UtilityRegisterTests`, `UtilityCrossFacilityTests`).
- Not done here: Windows MAUI acceptance run and browser review at `https://localhost:7167` — Clint reviews the UI slice before integration.

## Protected files

The eight pre-existing modified files (Login.razor/.css, AuditTrail.razor/.css and four planning docs) were never staged.
