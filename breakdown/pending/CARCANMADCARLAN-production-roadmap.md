# StallTrack → CARCANMADCARLAN: Phased Production Roadmap

**Status:** Approved basis — phased execution plan
**Scope:** Cantilan (EEMO) is the live, primary implementation; CARCANMADCARLAN (Carrascal · Cantilan · Madrid · Carmen · Lanuza) is the expansion path.
**Companion docs:**
- `CARCANMADCARLAN-multi-lgu-architecture.md` (architecture & migration design)
- `opus_web_admin_imemorycache_guidance.md` (caching architecture — already implemented)

---

## 0. Purpose

This document is the **official basis** for evolving StallTrack from a single-LGU system (Cantilan) into a
production-ready, multi-LGU platform — without rewriting Cantilan's financial logic and without ever
breaking the live implementation.

It is written to be executed **phase by phase**, where every phase is independently shippable, reversible,
and gated by tests that prove Cantilan's numbers did not change.

---

## 1. Current Architecture (verified anchor points)

The plan is grounded in the real codebase, not assumptions:

| Area | Current reality | Implication for multi-LGU |
|---|---|---|
| Entity base | `AuditableEntity : BaseEntity` (audit + soft delete) | Tenant-owned entities get a `MunicipalityId` via a marker interface |
| Facility | `Facility : AuditableEntity` with `FacilityCode Code` (enum NPM=1…TPM=8), seeded via `FacilitySeeder` | Facilities are already **data rows**; the enum conflates *identity* and *behavior* — the decoupling point |
| Fee rates | Fixed ordinance rates in `FeeRates` constants; per-stall `MonthlyRate`/`DailyRate`/`AreaSqm` stored | Fixed rates move to per-LGU config; per-stall rates already flexible |
| Current user | `ICurrentUserService` exposes `UserId/Username/Role/CollectorId` from token claims | Add a `MunicipalityCode` claim here — same pattern |
| Tenant seam | `ITenantContext` + `StaticTenantContext` (returns a constant tenant code) | Replace with a claim-bound resolver |
| Caching | `IEemoAppCache` with **tenant-prefixed** keys/regions (already implemented) | Cache layer is already multi-LGU-safe |
| Database | Single `AppDbContext`, one connection string (`DefaultConnection`), Npgsql/PostgreSQL | Chosen model: **shared DB + `MunicipalityId` + EF global query filters** |
| Auth | JWT; roles SuperAdmin/Admin/Collector; `CollectorId` taken from token, never the request | Tenant must likewise come from the token, never client input |

**Isolation decision:** shared database + `MunicipalityId` + EF Core global query filters (not database-per-LGU).
Rationale: lowest operational cost for five small municipalities and one team, keeps consolidated provincial
reporting trivial, and global query filters make isolation a single-point, systematic guarantee. Physical
isolation (schema- or database-per-LGU) is reserved for a future hard legal requirement only.

---

## 2. Guiding Rules (apply to every phase)

1. Cantilan stays live; its financial/report outputs never change unintentionally.
2. No phase touches financial/report math without snapshot/characterization tests around it first.
3. Every phase is independently shippable and `git`-revertible.
4. The active LGU is always derived from the **validated token claim**, never from a header, route, or the
   public selector the client controls.
5. Prefer configuration/data over new code; add new billing behavior only after validating a real LGU.

---

## 3. Phase Plan

### Phase 0 — Safety net & baseline lock
**Do this first. No features.**

- **Goal:** make it impossible to silently break Cantilan's money.
- **Work:**
  - Write characterization/snapshot tests freezing today's Cantilan outputs: dashboard totals, month-end
    report, financial report, collection rate, outstanding/unpaid, paid/partial/unpaid counts, NPM daily
    obligations, monthly rentals, absent/excused/closed behavior, SLH/TRM/TPM totals.
  - Add a CI gate that runs build + full test suite on every merge.
- **Exit criteria:** golden snapshots exist and pass; CI blocks changes that alter them.
- **Guard:** these snapshots are the tripwire for Phases 3–4.

### Phase 1 — Municipality registry + wire the selector
*(Additive; no operational-data risk.)*

- **Goal:** make the CARCANMADCARLAN presentation real and data-driven.
- **Work:**
  - Add a `Municipality` entity: `Code, Name, Province, Address, SealPath, OfficeName, Status, IsDefault, IsActive`.
  - Seed **Cantilan = Active / Default**; Carrascal, Madrid, Carmen, Lanuza = `Upcoming`.
  - Drive the public selector cards' badges and routing from `Municipality.Status`
    (Active → login; Upcoming → rollout status page).
  - Source Cantilan branding (name, seal, report header, office label) from this record — identical values,
    so nothing visually changes.
- **Exit criteria:** selector reflects the registry; portal branding sourced from the record; no behavior change.
- **Guard:** operational tables untouched.

### Phase 2 — Make the tenant seam live *(still single tenant)*

- **Goal:** turn `StaticTenantContext` into a real, claim-bound resolver — without a second LGU yet.
- **Work:**
  - Add a `MunicipalityCode` claim at login token issuance.
  - Expose it on `ICurrentUserService`.
  - Replace `StaticTenantContext` with a request-scoped `ClaimTenantContext` (defaults to Cantilan if the
    claim is absent, for backward compatibility).
  - The tenant-prefixed cache now runs on the real resolved tenant.
- **Exit criteria:** every request resolves the tenant from the token; cache/keys unchanged for Cantilan.
- **Guard:** the constant fallback keeps Cantilan working even if a claim is missing, so this cannot break prod.

> ✅ **Production Checkpoint A** — After Phases 0–2 plus the Hardening Track (Section 4),
> **Cantilan can go live in production** as a single-tenant system with the multi-LGU seams in place.
> Phases 3–6 are **not** required to ship Cantilan.

### Phase 3 — Data isolation model
*(Highest-risk structural step. Snapshot-gated.)*

- **Goal:** every operational row belongs to exactly one municipality, enforced centrally.
- **Work:**
  - Introduce `IMunicipalityOwned { Guid MunicipalityId }` — a marker interface, **not** blanket on
    `AuditableEntity` (Municipality itself is not tenant-owned).
  - Apply it to Facility, Stall, Contract, PaymentRecord, DailyCollection, SlaughterTransaction,
    TRM/TPM entities, users, AuditLog.
  - Migration: add the column and **backfill all existing rows to Cantilan's Id**.
  - Add **EF Core global query filters** on `MunicipalityId` (from `ITenantContext`) centrally in `AppDbContext`
    so every read is auto-scoped.
  - Stamp `MunicipalityId` on writes from the tenant context (save-changes interceptor).
  - Scope unique constraints (username, OR number) per municipality.
- **Exit criteria:** Phase 0 snapshots still pass byte-for-byte; verified that no query can return cross-tenant rows.
- **Guard:** global query filters are the systematic answer to "one missed filter leaks data." Snapshots mandatory.

### Phase 4 — Facility & rate configurability
*(Archetype decoupling. Snapshot-gated.)*

- **Goal:** stop assuming every LGU has Cantilan's eight facilities and rates.
- **Work:**
  - Add a billing **archetype**: `DailyStall, MonthlyRental, WeeklyMarket, PerTrip, PerHead, Custom`.
  - Map Cantilan's facilities to archetypes; **keep `FacilityCode` during migration** so existing report code
    keeps working (decouple facility *identity* from facility *behavior*).
  - Move the fixed ordinance rates in `FeeRates` (NPM ₱30/day, fish ₱1/kg, SLH per-head, TPM ₱100, TRM ₱30)
    into a per-facility/per-LGU `Rate` table with `EffectiveDate`, seeded with today's exact values.
  - Represent collection cycle/due rules as data.
- **Exit criteria:** Cantilan outputs identical (snapshots pass); rates and facilities now come from data.
- **Guard:** this is the trickiest code change — do it entirely behind the snapshots.

### Phase 5 — Runtime routing + multi-LGU capability
*(System becomes able to host a second LGU; still one live.)*

- **Goal:** the platform can serve a second municipality end to end.
- **Work:**
  - Subdomain (or path prefix) → pre-login branding resolution.
  - Login scoped to the selected municipality; issued token carries `mun`; users cannot switch LGU post-login.
  - Namespace the **mobile** offline cache and pending sync operations by LGU
    (e.g. `cantilan|records|2026-06`).
  - Optional: a province/super-admin read-only cross-LGU view.
- **Exit criteria:** a throwaway "test LGU" can be created, logged into (scoped), and shows fully isolated data
  with correct branding.

### Phase 6 — Onboarding workspace + first real LGU

- **Goal:** onboard a real municipality professionally.
- **Work:**
  - Private onboarding pipeline: LGU profile → facilities mapped to archetypes → rates/rules → users →
    branding/OR series → payor import → validation/dry-run → activation.
  - Activation flips `Municipality.Status` to `Active`.
  - Onboard one real LGU as proof.
- **Exit criteria:** a second municipality is live, fully isolated, with its own facilities/rates/reports.

> ✅ **Production Checkpoint B** — Multi-LGU production-ready. Cantilan primary; others onboardable on demand.

---

## 4. Hardening Track *(parallel — required for ANY production launch)*

Independent of tenancy; needed for Checkpoint A:

- **Secrets:** JWT signing key, DB credentials, PayMongo keys moved out of `appsettings.json` into an
  environment/secret store.
- **Transport & security:** HTTPS with a valid certificate, security headers, rate limiting on auth endpoints,
  verified account lockout.
- **Data safety:** automated PostgreSQL backups with a tested restore; documented migration-deploy step.
- **Observability:** structured logging, error monitoring, a health-check endpoint.
- **CI/CD:** build + test (including Phase 0 snapshots) gate on every merge; repeatable deployment.

---

## 5. What NOT To Do (keep the basis clean)

- Do not create per-LGU projects or fork the backend per municipality.
- Do not add `MunicipalityId` and rely on scattered per-query `Where` filters — use the central global query filter.
- Do not touch financial calculations without the Phase 0 snapshots in place.
- Do not derive the tenant from a header, route, or the public selector — only from the validated token claim.
- Do not build the full onboarding workspace (Phase 6) before a second LGU is actually needed.
- Do not hardcode another LGU's facilities/rates without validating them first.

---

## 6. Recommended Near-Term Sequence

For the panel and the real Cantilan launch:

```
Phase 0  →  Phase 1  →  Phase 2  →  Hardening Track  →  Ship Cantilan (Checkpoint A)
```

Treat Phases 3–6 as the funded expansion path, beginning Phase 3 only when a second LGU becomes real.

---

## 7. Status Tracking

| Phase | Title | State | Notes |
|---|---|---|---|
| 0 | Safety net & baseline lock | ✅ Complete | Integration goldens + CI gate; 441/441 green |
| 1 | Municipality registry + selector wiring | ✅ Complete | Registry: entity + migration + seed + tests; selector/branding wiring deferred (separate landing) |
| 1.5 | Public read-only registry API | ✅ Complete (code) | `GET /api/municipalities` (`GetMunicipalitiesQuery` + `MunicipalityRepository`, anonymous, non-sensitive fields); 442/442 green. Landing consumption deferred until deployed (stable URL + CORS). |
| 2 | Live tenant seam (claim-bound) | ✅ Complete | Claim in JWT + `ClaimTenantContext`; tenant still `cantilan-sds` |
| — | Hardening Track | ◑ Partial — ops pending | Code-side done (probes, cookie policy, externalized JWT key, CI); runbook drives ops |
| A | **Production Checkpoint A (Cantilan live)** | Pending ops verification | Code-side ready; runbook ops actions remain (secrets in host, TLS, backups, monitoring) |
| 3 | Data isolation model | Not started — deferred | Until a real second LGU; highest risk (snapshot-gated) |
| 4 | Facility & rate configurability | Not started | Snapshot-gated, archetype decoupling |
| 5 | Runtime routing + multi-LGU capability | Not started | Enables a 2nd LGU |
| 6 | Onboarding workspace + first real LGU | Not started | Proof of expansion |
| B | **Production Checkpoint B (multi-LGU)** | Pending | After 3–6 |

**Working status (latest session):**
- **CARCANMADCARLAN demo — presentation-only, decoupled, no backend writes.** The public landing rollout
  pipeline and the platform admin console (`apps/admin` in the `stalltrack-landing` monorepo) implement the
  full journey: Assessment → Onboarding (comprehensive config workspace: facility pipeline cards mapped to
  archetypes, sections + section fees, per-head animal rates, fixed/metered add-on fees, Administrator
  account, branding + logo) → Validation (config-derived dry-run: Facilities · Total units · Rates & fees ·
  Collection models) → Activation (branded letterhead + Head account-activation link). See
  `CARCANMADCARLAN-onboarding-flow.md`.
- **Phase 1.5 registry API** built + tested (`GET /api/municipalities`, 442/442). Not deployed; the landing
  still uses its static `municipalities.js`.
- **Live Cantilan:** admin **sign-out** surfaced on the Settings page (reuses the existing revoke-on-logout
  pipeline; no new auth built).
- **Deferred (unchanged):** Phase 3+ until a real second LGU; Cantilan production/hardening ops until a
  go-decision.

---

## 8. Per-Phase Acceptance Criteria

A phase is **Done** only when every box below is checked. Do not advance to the next phase with unchecked
items in a prerequisite phase.

### Phase 0 — Safety net & baseline lock  ✅ (in place)
- [x] Snapshot tests exist for dashboard totals — `DashboardRepositoryTests` (integration goldens over the real `AppDbContext`)
- [x] Snapshot tests exist for the month-end report — `Phase0/CantilanMonthEndBaselineTests` (real repositories)
- [x] Snapshot tests exist for the financial report — `Phase0/CantilanFinancialBaselineTests` (real repositories, monthly all-facility); single-facility/yearly behaviour covered by `GetFinancialReportQueryHandlerTests` + `FacilityReports*` tests
- [x] Snapshot tests exist for NPM daily obligations — `FacilityReportsNpm*` suite (absent/excused, market closure, proration, obligation window)
- [x] Snapshot tests exist for monthly-rental facilities — `FacilityReportsTccComplianceTests` + `FacilityReports*` (paid/partial/unpaid, rate changes)
- [x] Snapshot tests exist for SLH/TRM/TPM service-facility totals — `SlaughterHistoryTests`/`TrmHistoryTests`/`TpmHistoryTests` + the two Phase 0 goldens
- [x] Full test suite runs green — **435/435 passing**
- [x] CI gate blocks merges that fail build or tests — `.github/workflows/ci.yml` (build API + portal + full test run on push/PR to master)
- [x] Golden snapshot values recorded and committed as the baseline — `EEMOCantilanSDS.Testing/Phase0/*`

> Note: the two new Phase 0 integration goldens fill the one real gap — the financial and month-end reports
> were previously only tested with **mocked** repositories, so a regression in the repository/`AppDbContext`
> layer (exactly where Phase 3's global query filters and Phase 4's rate config land) would not have been
> caught. Both goldens run the composed reports through the **real** repositories against one fixed seed and
> reconcile to identical totals (₱2,650 collected / ₱2,400 outstanding / 52% rate).


### Phase 1 — Municipality registry + selector wiring  ◑ (backend registry done)
- [x] `Municipality` entity + EF configuration + migration created — `Domain/Entities/Tenancy/Municipality.cs`, `MunicipalityConfiguration`, migration `20260702065757_AddMunicipalityRegistry` (additive: creates only the `Municipalities` table)
- [x] Cantilan seeded as `Active` and `IsDefault`; Carrascal/Madrid/Carmen/Lanuza seeded as `Upcoming` — `MunicipalitySeeder` (+ `Phase1/MunicipalitySeederTests`, idempotent)
- [~] Public selector badges are driven by `Municipality.Status` — the read-only public API now EXISTS (`GET /api/municipalities` → `GetMunicipalitiesQuery`/`MunicipalityRepository`, `[AllowAnonymous]`, non-sensitive fields; `Phase1/GetMunicipalitiesQueryTests`). Landing consumption still deferred until the API is deployed (stable URL + CORS); planned as a build-time snapshot with the current static `municipalities.js` as fallback.
- [~] Active card routes to the LGU login; Upcoming cards route to a rollout status page — already built in the landing project (data-driven within that project), not yet from the DB registry
- [ ] Cantilan branding (name, seal, report header, office label) is read from the `Municipality` record — DEFERRED: portal branding is still static; can be sourced from the registry in a follow-up
- [x] No operational tables changed; Phase 0 snapshots still pass — additive migration only; **437/437 tests green** (incl. Phase 0 goldens)
- [x] Cantilan portal is visually unchanged — no portal/operational code touched

### Phase 2 — Live tenant seam (claim-bound)  ✅ (seam live)
- [x] Login issues a JWT containing a `MunicipalityCode` claim — `TokenService.CreateToken` adds `AppClaimTypes.Municipality` (default tenant, since users aren't municipality-scoped until Phase 3)
- [x] `ICurrentUserService` exposes the resolved municipality — `MunicipalityCode` (reads the claim)
- [x] `StaticTenantContext` replaced by a request-scoped claim-based `ITenantContext` — `ClaimTenantContext` (registered in DI; `StaticTenantContext` retained for reference)
- [x] Cache keys/regions resolve from the real tenant; Cantilan cache behavior unchanged — tenant still resolves to `cantilan-sds`; **441/441 tests green** (Phase 0 goldens + caching tests unchanged)
- [x] Tenant is never read from a header, route, or client input — only the validated token claim (via `ICurrentUserService`)
- [~] Token-less flows resolve the tenant from a trusted persisted record — currently fall back to the default tenant (correct while single-LGU); per-record resolution (webhook/sync) is wired in Phase 3 when a second LGU is real
- [~] The Cantilan fallback is explicitly temporary — documented as such in `ClaimTenantContext`; the flip to warning/error on a missing/invalid claim is enforced once a second LGU is active
- [x] Phase 0 snapshots still pass — verified (441/441, `Phase2/ClaimTenantContextTests` added)

### Hardening Track (required for Checkpoint A) — see `CANTILAN-production-hardening-runbook.md`
- [~] Secrets to env/secret store — `Jwt:Key` already externalized (empty in config); confirm DB connection string + PayMongo keys (runbook §1)
- [~] HTTPS enforced + valid certificate; security headers — HTTPS redirect on outside Dev; HSTS + headers pending (runbook §2/§3, CSP tested separately)
- [x] Account lockout verified (5 fails → 15-min lock); [ ] request rate limiting on auth endpoints, lenient (runbook §4)
- [ ] Automated PostgreSQL backups configured; a restore has been tested (runbook §5)
- [~] Migration-deploy step — `Database:ApplyMigrationsAtStartup` toggle exists; strategy to document (runbook §6)
- [x] Health probes respond — `/health` (liveness) + `/health/ready` (DB readiness); [ ] structured logging + error monitoring (runbook §7)
- [x] CI/CD builds API + portal and runs the full test suite on push/PR — `.github/workflows/ci.yml`

### ✅ Production Checkpoint A — Cantilan live
- [ ] Phases 0, 1, 2 complete
- [ ] Hardening Track complete
- [ ] Cantilan verified in the production environment (login, record payment, daily collection, reports, mobile)
- [ ] Backups and rollback path confirmed

### Phase 3 — Data isolation model
- [ ] `IMunicipalityOwned { Guid MunicipalityId }` marker interface introduced
- [ ] Applied to Facility, Stall, Contract, PaymentRecord, DailyCollection, SlaughterTransaction, TRM/TPM entities, users, AuditLog
- [ ] Migration adds `MunicipalityId` and backfills all existing rows to Cantilan's Id
- [ ] EF Core global query filters on `MunicipalityId` applied centrally in `AppDbContext`
- [ ] Writes stamp `MunicipalityId` from the tenant context (interceptor)
- [ ] Unique constraints (username, OR number) scoped per municipality
- [ ] Verified a query cannot return cross-tenant rows — isolation tests prove one LGU's data never appears in another's views, with explicit guards for the filter-bypass paths: `IgnoreQueryFilters()`, raw SQL, and background/scheduled jobs that don't run under a user token
- [ ] Phase 0 snapshots pass byte-for-byte (no financial change)

### Phase 4 — Facility & rate configurability
- [ ] Billing archetype introduced (`DailyStall, MonthlyRental, WeeklyMarket, PerTrip, PerHead, Custom`)
- [ ] Cantilan facilities mapped to archetypes; `FacilityCode` retained during migration
- [ ] Facility *identity* (data) decoupled from facility *behavior* (archetype)
- [ ] Fixed `FeeRates` ordinance rates moved to a per-facility/per-LGU `Rate` table with `EffectiveDate`
- [ ] Cantilan rates seeded with today's exact values
- [ ] Collection cycle/due rules represented as data
- [ ] Phase 0 snapshots pass byte-for-byte (no financial change)

### Phase 5 — Runtime routing + multi-LGU capability
- [ ] Subdomain (or path prefix) resolves pre-login branding
- [ ] Login scoped to the selected municipality; token carries `mun`; no post-login LGU switching
- [ ] Mobile offline cache and pending sync operations namespaced by LGU
- [ ] (Optional) province/super-admin read-only cross-LGU view
- [ ] A throwaway test LGU can be created, logged into, and shows fully isolated data + correct branding

### Phase 6 — Onboarding workspace + first real LGU
- [ ] Onboarding pipeline implemented (profile → facilities/archetypes → rates/rules → users → branding/OR → payor import → validation/dry-run → activation)
- [ ] Dry-run validates sample reports and obligations before activation
- [ ] Activation flips `Municipality.Status` to `Active`
- [ ] One real LGU onboarded end-to-end and verified isolated from Cantilan

### ✅ Production Checkpoint B — Multi-LGU
- [ ] Phases 3–6 complete
- [ ] Second municipality live and fully isolated with its own facilities/rates/reports
- [ ] Cantilan data and numbers unaffected by the second LGU

---

*This roadmap preserves Cantilan as the stable primary implementation while making the system provably
ready for CARCANMADCARLAN expansion — configuration over duplication, isolation by construction, and no
change to financial correctness without proof.*
