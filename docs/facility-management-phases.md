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

### Phase B — Add a facility
- `AddFacilityCommand` (Head/SuperAdmin only): creates the `Facility` (with `BillingArchetype`),
  its `FacilityRate` rows, and — for SLH — animal types/sections, stamped to the caller's own
  `MunicipalityId`. Rejects a duplicate code. Reuses the onboarding config concepts, scoped live.

### Phase C — Update / retire
- Rename / short-name / description edits.
- Rate changes as **new effective-dated** `FacilityRate` rows (never mutate history).
- Activate / deactivate (soft), preserving history.

### Phase D — Polish
- Audit-log entries, validation, regression tests, Cantilan/Phase-0 golden verification.

## Architecture notes

Follows the existing Clean Architecture / CQRS conventions:
`Application/Queries/Facilities/GetFacilityConfiguration/{Query,Handler}` + a DTO in
`Application/Dtos`, a repository method behind `IFacilityRepository`, an action on
`FacilitiesController`, a typed client behind `IFacilitiesApiClient`, and a routable Blazor page
using the shared design tokens and component-scoped CSS. No new patterns introduced.
