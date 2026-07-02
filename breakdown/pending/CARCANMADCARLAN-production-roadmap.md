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
| 0 | Safety net & baseline lock | Not started | Prerequisite for 3–4 |
| 1 | Municipality registry + selector wiring | Not started | Additive, low risk |
| 2 | Live tenant seam (claim-bound) | Not started | Builds on existing `ITenantContext`/cache |
| — | Hardening Track | Not started | Required for Checkpoint A |
| A | **Production Checkpoint A (Cantilan live)** | Pending | After 0–2 + Hardening |
| 3 | Data isolation model | Not started | Snapshot-gated, highest risk |
| 4 | Facility & rate configurability | Not started | Snapshot-gated, archetype decoupling |
| 5 | Runtime routing + multi-LGU capability | Not started | Enables a 2nd LGU |
| 6 | Onboarding workspace + first real LGU | Not started | Proof of expansion |
| B | **Production Checkpoint B (multi-LGU)** | Pending | After 3–6 |

---

## 8. Per-Phase Acceptance Criteria

A phase is **Done** only when every box below is checked. Do not advance to the next phase with unchecked
items in a prerequisite phase.

### Phase 0 — Safety net & baseline lock
- [ ] Snapshot tests exist for dashboard totals (collected, outstanding, collection rate, occupied count)
- [ ] Snapshot tests exist for the month-end report
- [ ] Snapshot tests exist for the financial report (monthly and yearly, all-facility and single-facility)
- [ ] Snapshot tests exist for NPM daily obligations (paid/partial/unpaid, absent/excused, market closures)
- [ ] Snapshot tests exist for monthly-rental facilities (TCC/NCC/BBQ/ICE) paid/partial/unpaid
- [ ] Snapshot tests exist for SLH/TRM/TPM service-facility totals
- [ ] Full test suite runs green (existing + new snapshots)
- [ ] CI gate blocks merges that fail build or tests
- [ ] Golden snapshot values recorded and committed as the baseline

### Phase 1 — Municipality registry + selector wiring
- [ ] `Municipality` entity + EF configuration + migration created
- [ ] Cantilan seeded as `Active` and `IsDefault`; Carrascal/Madrid/Carmen/Lanuza seeded as `Upcoming`
- [ ] Public selector badges are driven by `Municipality.Status` (no hardcoded state)
- [ ] Active card routes to the LGU login; Upcoming cards route to a rollout status page (no fake dashboard)
- [ ] Cantilan branding (name, seal, report header, office label) is read from the `Municipality` record
- [ ] No operational tables changed; Phase 0 snapshots still pass
- [ ] Cantilan portal is visually unchanged

### Phase 2 — Live tenant seam (claim-bound)
- [ ] Login issues a JWT containing a `MunicipalityCode` claim
- [ ] `ICurrentUserService` exposes the resolved municipality
- [ ] `StaticTenantContext` replaced by a request-scoped claim-based `ITenantContext` (with Cantilan fallback)
- [ ] Cache keys/regions resolve from the real tenant; Cantilan cache behavior unchanged
- [ ] Tenant is never read from a header, route, or client input — only the validated token
- [ ] Phase 0 snapshots still pass

### Hardening Track (required for Checkpoint A)
- [ ] JWT signing key, DB credentials, and PayMongo keys moved out of `appsettings.json` to env/secret store
- [ ] HTTPS enforced with a valid certificate; security headers configured
- [ ] Rate limiting on auth endpoints; account lockout verified
- [ ] Automated PostgreSQL backups configured; a restore has been tested
- [ ] Migration-deploy step documented and repeatable
- [ ] Structured logging + error monitoring in place; health-check endpoint responds
- [ ] CI/CD builds, runs tests (incl. snapshots), and deploys repeatably

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
- [ ] Verified a query cannot return cross-tenant rows
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
