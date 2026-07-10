# Facility Management (in-portal) — phased design

## Problem

Today an LGU's set of facilities is fixed at **onboarding/activation**
(`ActivateMunicipalityCommandHandler` creates the `Facility` rows, their `FacilityRate`
rows, SLH animal types, and OR-series from the operator's onboarding draft). There is **no
in-portal path** for a live LGU to add a canonical facility it skipped, or to adjust a
facility's presentation/rates — it would have to be re-onboarded, which is heavy and
operator-only.

## Why this is safe and consistent (not a second onboarding)

Two distinct operations, kept separate:

- **Onboarding / assessment = birth of a tenant.** One-time, *platform-operator* action that
  provisions a brand-new LGU end-to-end and flips it live. Requires assessment because nothing
  exists yet and an outside authority vouches for the municipality.
- **Facility management = a live LGU growing its own configuration.** Repeatable, *Head*
  self-service inside the LGU's own portal. No re-assessment needed: the tenant already exists,
  is authenticated, and only ever extends **its own** scope (never another LGU).

The domain already supports this — facilities are data-driven, not hardcoded:

- `Facility` is `IMunicipalityOwned` (per-LGU rows, stamped with `MunicipalityId`).
- Billing behaviour is stored as data: `BillingArchetype` (DailyStall, MonthlyRental, PerHead,
  PerTrip, WeeklyMarket, Custom).
- Fees live in per-LGU `FacilityRate` rows resolved at runtime (Phase-4B fee resolver); SLH
  animal types and OR-series are also per tenant.
- Stalls, contracts, occupants, and rate edits are already managed live in the portal.

Scope boundary: facilities are the **8 canonical types** (`FacilityCode` NPM…TPM). "Add a
facility" means enabling and configuring one of the standard types the LGU hasn't set up yet —
not inventing arbitrary new types.

## Guardrails (all phases)

- **Authorization:** Head/SuperAdmin of *that* LGU only — never cross-tenant.
- **Uniqueness:** one facility per `FacilityCode` per LGU; adding a duplicate is rejected.
- **Rate-history integrity:** rate changes are new **effective-dated** `FacilityRate` rows, never
  mutations of history, so a settled/receipted period keeps its original amount.
- **Safe retirement:** deactivating a facility with active stalls/obligations is a soft
  deactivate that preserves history — never a hard delete.
- **Cantilan untouched:** the default tenant's seeded configuration stays byte-for-byte; the
  Phase-0 goldens stay green. Reads add no behaviour; writes are additive and tenant-scoped.
- **Audit:** every write is captured in the audit trail (actor, before/after) like other
  financial-adjacent mutations.

## Phases

### Phase A — Read & surface (this phase, no writes)
- `GetFacilityConfigurationQuery` + handler + DTO: the current tenant's configured facilities
  (code, name, short name, archetype, active flag, stall count, and their fixed `FacilityRate`
  rows) **and** the canonical codes still available to add.
- `GET /api/facilities/configuration` (Authorize).
- Client: `IFacilitiesApiClient.GetFacilityConfigurationAsync` + a Head-only **Facilities**
  page (read-only) with an entry point under **Settings**. Pure visibility; nothing changes.
- Lowest risk; demonstrates the capability and frames the write phases.

### Phase B — Add a facility (IMPLEMENTED)
- `AddFacilityCommand` + validator + handler (Head/SuperAdmin only): adds one of the standard types to
  the caller's own tenant with a **Head-chosen name/short-name** (e.g. "Madrid Night Market" backed by
  an available slot). Billing archetype is derived from the canonical code so the collection/report
  machinery stays correct; duplicate code per tenant is rejected (409); `MunicipalityId` is stamped on
  save. No rate seeding needed — `FeeRateSnapshot.Resolve` already falls back **per key** to the
  ordinance defaults, and monthly-rental rates live per stall.
- `POST /api/facilities` [Authorize SuperAdmin]; typed client method.
- Portal: the "available to add" tiles are now clickable → an inline add form (name/short-name/optional
  description, billing model shown read-only) → on success the facility moves to Configured. Regression
  tests: add success, duplicate rejected, unknown code rejected. Additive only — Cantilan/goldens intact.

### Custom facility types — assessment (deferred, NOT bundled)
Truly arbitrary custom types (beyond the 8 canonical `FacilityCode` values) are **not** safe to add in a
single change: `FacilityCode` is referenced ~738× across ~122 files (reports, collections, mobile,
dashboard, routing controllers keyed by `{facilityCode}`, cache keys, payor formatting). Introducing a
non-enum facility would touch that entire surface and risk the Phase-0 financial goldens. So Phase B
delivers flexibility via **custom naming on the standard types** instead. A future dedicated phase can
introduce a generic "Custom" facility backed by `BillingArchetype` + a generic monthly-rental/per-
transaction flow, built incrementally with the goldens guarding the canonical facilities — on its own,
never rushed onto the live system.

### Phase C — Update / retire
- Rename / short-name / description edits.
- Rate changes as **new effective-dated** `FacilityRate` rows (never mutate history).
- Activate / deactivate (soft), preserving history.

### Phase D — Polish
- Audit-log entries, validation, regression tests, Cantilan/Phase-0 golden verification.

## Phase E — Custom facilities (IMPLEMENTED for monthly; per-day/week/ad-hoc deferred)

Lets a Head add per-LGU **custom facilities** that reuse the standard machinery.

**Implemented — custom MONTHLY-rental facilities (full tracking):**
- Reserved `FacilityCode.Custom1..Custom5` (101–105) — additive; no DB migration (reuses the existing
  `Facility`/`Stall`/`Contract`/`PaymentRecord` tables, all keyed by facility `Id`, not the code).
- A custom facility is Head-named and defaults to `BillingArchetype.MonthlyRental`, so it behaves
  exactly like TCC/NCC/BBQ/ICE: stalls, contracts, monthly payments, **delinquency/arrears**, dashboard,
  and financial/collection/month-end reports — with full tracking, per the "same logic as monthly →
  same delinquency" guidance.
- Every facility classifier was audited and now treats custom codes as monthly-rental:
  `FacilityState.RentalCodes`, `DashboardRepository.stallCodes`, `MonthlyRentalFacilities`,
  `SetStallMonthlyException`, and the three report handlers' rental sets. Reports resolve the custom
  facility's Head-chosen name (canonical names untouched).
- **Cantilan is inert:** it has no custom facilities, so `tenantCodes` never contains a custom code and
  every added classifier entry is a no-op. Phase-0 goldens stay byte-for-byte green (563/563).
- Add UI: the portal "Available to add" offers one **Custom facility** slot (gold, Head-named); the
  existing `AddFacilityCommand` creates it (custom code → MonthlyRental).

**Deferred (deliberately, per "if it's the tricky part we don't do it"):**
- **Per-day** and **per-week** custom facilities. Daily is NPM's `DailyCollection` engine (fish fee, day
  proration, market closures) and weekly is TPM's `TpmAttendance`/market-day engine — both are
  facility-specific with dedicated tables and `FacilityCode.NPM`/`TPM` branches. Generalising them for
  custom facilities would risk the live NPM/TPM collection flows, so they are out of scope.
- **No-schedule (ad-hoc)** custom facilities: would need a new generic logged-collection model (no
  obligation engine). Not built.
- **Mobile / bulk-import** for custom facilities: not wired yet (custom stalls are added individually in
  the web portal). Additive follow-ups.

## Architecture notes

Follows the existing Clean Architecture / CQRS conventions:
`Application/Queries/Facilities/GetFacilityConfiguration/{Query,Handler}` + a DTO in
`Application/Dtos`, a repository method behind `IFacilityRepository`, an action on
`FacilitiesController`, a typed client behind `IFacilitiesApiClient`, and a routable Blazor page
using the shared design tokens and component-scoped CSS. No new patterns introduced.
