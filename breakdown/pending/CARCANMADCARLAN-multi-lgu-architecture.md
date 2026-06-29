# CARCANMADCARLAN Multi-LGU Architecture & Migration Design

**Status:** Design / Spec only — **NOT** for implementation yet.
**Author:** Engineering
**Date:** 2026-06-29
**Scope decision (confirmed with project owner / panel):** Prove that StallTrack's architecture *can* support the five CARCANMADCARLAN LGUs. Cantilan remains the **main / default** implementation and must stay stable. Other LGUs are a **future migration**, not an immediate onboarding.

---

## 1. Problem Statement

The current system (StallTrack / EEMO Revenue Collection System) is built for **one municipality — Cantilan**. The review panel asked whether it can serve the broader **CARCANMADCARLAN** cluster:

> **CAR**rascal · **CAN**tilan · **MAD**rid · **CAR**men · **LAN**uza — the five northernmost municipalities of Surigao del Sur. Cantilan was historically the parent town of all five.

Each is a **separate Local Government Unit (LGU)** with its own:

- official name, seal/logo, address, and report headers (branding)
- facilities (and facility names/acronyms)
- fee rates and collection cycles (per ordinance)
- payors / vendors / stalls / contracts
- collectors and admins
- official receipt (OR) series
- reporting requirements

The ask is **not** to onboard all five now. It is to **document an architecture** that proves the system can scale to multiple LGUs **without breaking Cantilan** and **without forking the codebase per municipality**.

---

## 2. Guiding Principles

1. **One codebase, many LGUs.** Differences are handled by **data and configuration**, never by per-municipality forks. (Per-LGU forks = duplicated bugs, N× maintenance, divergence.)
2. **Cantilan is the baseline.** It is the default seeded LGU and must keep working *exactly* as today through every phase.
3. **Additive-first migration.** Every change ships additive (new tables/columns nullable & defaulted to Cantilan) before any behavior is scoped. Cantilan runs as the sole/default tenant until each boundary is proven.
4. **Financial correctness is non-negotiable.** No tenant-scoping of collected/compliance/rate/report logic without **snapshot-equivalence tests first** (the same rule already applied to the dashboard consolidation work).
5. **Government data isolation.** Five politically separate LGUs must not co-mingle financial data in a way that risks one LGU seeing another's collections.

---

## 3. The Decision That Drives Everything: Isolation Model

Three options were weighed:

| Model | Isolation | Ops cost | Risk |
|---|---|---|---|
| Shared DB + row-level tenancy (`MunicipalityId` + global query filter) | Logical only | Lowest (1 deploy, 1 DB) | A single missed filter leaks one LGU's money data into another's |
| **DB-per-LGU, single codebase** (app selects connection string by tenant) | **Guaranteed by construction** | Medium (N migrations/backups) | Low — cross-tenant leak is structurally impossible |
| Instance-per-LGU (same code, separate deployments) | Strong | Highest (N deployments) | Operational sprawl |

### Decision: **DB-per-LGU on a single shared codebase.** ✅ (confirmed)

Rationale:
- Government **data sovereignty** — each LGU owns its own database; backups, audits, and handover are per-LGU and trivial.
- Isolation is **by construction** — there is no shared table where a missing `WHERE MunicipalityId = …` could leak data. This eliminates the scariest class of multi-tenant bug.
- **Cantilan is unchanged** — it simply becomes "tenant #1's database." The existing Cantilan DB/code stays stable; multi-LGU is a future migration layered on top.

Trade-off accepted: more operational work (one migration run + one backup schedule per LGU). For five distinct government units this is worth it.

> **Note:** This document standardizes on DB-per-LGU. Should constraints later force a shared-DB model, **every** tenant-owned query would need an enforced global filter + extensive leak tests — a materially higher-risk path. Revisit only with strong justification.

---

## 4. The Core Technical Reframing: `FacilityType` (archetype) vs `Facility` (per-LGU data)

The deepest Cantilan assumption in the code is **not** branding — it is that **facilities are a hardcoded `enum`**:

```
FacilityCode { NPM=1, TCC=2, NCC=3, BBQ=4, ICE=5, SLH=6, TRM=7, TPM=8 }
```

This enum is woven through DTOs, queries, `switch` statements, mobile facility chips, and reports. Madrid or Lanuza will **not** have "NPM/TCC"; they will have *their own* facilities.

### Key insight: there are only ~5 **collection archetypes** (the reusable engine)

| Archetype (`FacilityType`) | Pattern | Cantilan example(s) |
|---|---|---|
| `DailyStall` | ₱X / day per stall (+ optional fish/kg) | NPM |
| `MonthlyRental` | ₱X / month per stall (+ utilities) | TCC, NCC, BBQ, ICE |
| `PerHead` | ₱X per head, per transaction | SLH |
| `PerTrip` | ₱X per trip | TRM |
| `WeeklyMarket` | ₱X per vendor per market day | TPM |

### The model

- **`FacilityType`** becomes a **stable enum/catalog** describing the *collection pattern* (the engine). It drives all collection/obligation/recognition logic.
- **`Facility`** becomes a **per-LGU data row**: belongs to a `Municipality`, references a `FacilityType`, and carries its own **name, acronym, rates, and cycle**.
- Cantilan's 8 facilities become **8 seeded `Facility` rows of tenant #1**, each pointing at the right archetype with Cantilan's current rates.

### What this unlocks

Carmen can add a *"Public Market (DailyStall)"* or a *"Bus Terminal (PerTrip)"* with **zero new code** — it instantiates an existing archetype with its own name and rates. **Customization = configuration, not forks.** This is what makes "the other LGUs are custom" true without custom code.

> Migration reality: do **not** rip out `FacilityCode`. Introduce `FacilityType` alongside it, keep Cantilan's existing facilities as seeded rows, and migrate logic to key off `FacilityType` + the `Facility` row **gradually**, gated by snapshot tests (see §8).

---

## 5. Target Domain Additions

A new tenant-root aggregate:

```
Municipality (a.k.a. LGU)
  Id            : Guid
  Code          : string   // e.g. "CAN", "CAR", "MAD", "CRM", "LAN"
  Name          : string   // "Municipality of Cantilan"
  Province      : string   // "Surigao del Sur"
  Address       : string
  SealPath      : string   // per-LGU seal/logo
  IsDefault     : bool     // Cantilan = true
  IsActive      : bool     // onboarded & live
  // branding/report header fields, contact, etc.
```

`Facility` gains `MunicipalityId` + `FacilityTypeId/FacilityType`. Tenant-owned records (stalls, contracts, payments, daily collections, vendors, transactions, users, audit) are **isolated by database** under DB-per-LGU (no `MunicipalityId` column strictly required for isolation, but `Municipality` config still lives per-DB as the single seeded row identifying that DB's LGU).

> Under DB-per-LGU, the `Municipality` table in each LGU's database holds exactly **one** active row describing that LGU (its branding/config). The **catalog** of all five LGUs for the selector/login lives in a small shared registry (see §6).

---

## 6. Tenant Resolution & Login

**Recommended: resolve the LGU from the authenticated account** (each admin/collector belongs to exactly one LGU), backed by the **database the app connects to for that tenant**.

- **Per-LGU subdomain or selection → connection string.** A small **LGU registry** (config or a tiny shared lookup) maps an LGU code → its database connection + branding. The app resolves the tenant (from subdomain, a pre-login municipality selection, or the account's home LGU) and binds the request to that LGU's `DbContext` connection for the rest of the request.
- **Pre-login municipality selector (UI):** professional CARCANMADCARLAN landing → choose LGU → that LGU's branded login. Mostly branding/routing; the **authoritative** tenant binding is still the connection chosen for that LGU.
- **Avoid a free-form "switch LGU after login"** for ordinary users — it invites mis-attribution. A future **provincial/super-admin** role could switch tenants deliberately.

Cantilan stays the **default**: if no LGU is chosen, the system behaves exactly as today (Cantilan).

---

## 7. Cross-Cutting Concerns (grounded in current code)

These are concrete couplings observed in the codebase that the migration must address:

1. **OR (official receipt) uniqueness must become per-LGU.**
   Today `IsORNumberUniqueAsync` enforces a **global** unique OR across all tables. Each LGU issues its **own OR series**, so two towns legitimately reusing `OR-0001` must not collide. **DB-per-LGU solves this for free** (uniqueness is naturally per-database). The slaughterhouse "same OR across a visit's animal rows" rule (`IsORNumberAvailableForReceiptAsync`) remains per-LGU.

2. **Mobile offline cache keys must include the LGU.**
   The read-through cache and pending-operation queue keys (e.g. `records|…`, `npm|…`) must be **namespaced by LGU/connection** so a collector context never serves another tenant's cached lists. The offline pending queue likewise must be tenant-scoped on device.

3. **`FeeRates` constants → per-LGU/per-facility config.**
   Fixed ordinance values (NPM ₱30/day, fish ₱1/kg, TPM ₱100, SLH per-head, TRM per-trip) are **Cantilan's** rates and currently live in `FeeRates` constants. They must move to **per-facility rates** (per-stall `MonthlyRate`/`DailyRate` are already data, which helps). Each LGU configures its own rates.

4. **Branding is data, not code.**
   Municipality name, seal, address, report headers, and the "EEMO" office label become per-LGU config consumed by the web Client and mobile app.

5. **Financial engine + reports are the high-risk zone.**
   Collection rate, outstanding, compliance, obligation windows, dashboards — these must remain correct per tenant. See §8.

---

## 8. Testing Strategy (mandatory gate)

Before scoping any financial/report logic by tenant:

1. Write **characterization / snapshot tests** that pin the **current Cantilan** report, dashboard, compliance, and obligation numbers (some already exist; expand coverage).
2. Make the tenant-scoping change.
3. Prove the change is **equivalent for the existing (Cantilan) tenant** — the snapshot numbers must not move unless intended.

This is the same discipline already applied to the dashboard consolidation and the NPM obligation-window characterization. **No financial refactor merges without this gate.**

---

## 9. Phased Migration Roadmap (additive-first; Cantilan never regresses)

> **Phase 0 (now): Planning/documentation only.** Finish & stabilize Cantilan. Write no multi-LGU code. (This document + the hardcoded UI mock are the only Phase-0 outputs.)

**Phase 1 — Tenant scaffold (additive, zero behavior change).**
- Add a `Municipality` entity; seed Cantilan as `IsDefault`.
- Establish the LGU registry (code → connection + branding), with Cantilan as the only live entry.
- No data is scoped yet; the app still behaves as single-tenant Cantilan.

**Phase 2 — Branding/config separation.**
- Move municipality name, seal, address, and report headers into per-LGU config (seeded identically for Cantilan).
- Web + mobile read branding from config instead of hardcoded "EEMO/Cantilan".

**Phase 3 — Facility catalog (`FacilityType` archetype).**
- Introduce `FacilityType`; reframe `Facility` as per-LGU data referencing an archetype + carrying its own rates/cycle.
- Seed Cantilan's 8 facilities identically. **Gated behind snapshot tests** on reports/financials.

**Phase 4 — Tenant binding & accounts.**
- Per-LGU `DbContext` connection resolution (subdomain / selection / account home LGU).
- Assign admins/collectors to an LGU. Security-critical; isolate behind tests.

**Phase 5 — Per-LGU specifics.**
- Per-LGU OR series, online-payment account/config, per-LGU rates finalized.

**Phase 6 — Multi-LGU UX.**
- Real CARCANMADCARLAN selector wired to the registry (replaces the hardcoded mock).
- Optional **provincial / super-admin** cross-LGU dashboard.
- Single-LGU users see no added complexity.

---

## 10. What NOT To Do

- ❌ Do **not** duplicate the solution or fork application logic per municipality.
- ❌ Do **not** add LGU selection visually **without** securing the data boundary behind it (the mock UI in Phase 0 is explicitly **non-functional** branding only).
- ❌ Do **not** rename the project/solution away from Cantilan until requirements are firm.
- ❌ Do **not** refactor financial calculations without snapshot-equivalence tests.
- ❌ Do **not** make `FacilityCode` removal a "big bang" — migrate to `FacilityType` incrementally.

---

## 11. Open Questions for the Panel

1. **Hosting**: one server hosting five databases, or separate hosting per LGU? (Affects connection-registry + deployment.)
2. **Onboarding order & timeline**: which LGU (if any) is the first real onboarding after Cantilan, and when?
3. **Cross-LGU reporting**: does the province need a consolidated, read-only view across all five? (Drives the Phase-6 super-admin role.)
4. **Facility confirmation**: do the other four LGUs' facilities all map to the five existing archetypes, or is there a collection model we haven't seen?
5. **Per-LGU rate authority**: who maintains each LGU's rates/ordinance values, and do we need an admin UI for it?

---

## 12. Summary

- **Architecture:** one codebase, **DB-per-LGU**, tenant-resolved by connection.
- **Facilities:** `FacilityType` archetype (engine) + per-LGU `Facility` data (identity/rates) — customization via configuration, never forks.
- **Cantilan:** the default, baseline tenant — stable and unchanged throughout.
- **Safety:** additive-first phases; snapshot-equivalence tests before any financial tenant-scoping; OR uniqueness and mobile cache namespaced per LGU.
- **Now (Phase 0):** documentation (this file) + a **hardcoded, non-functional** CARCANMADCARLAN selector mock to demonstrate the direction to the panel. No tenancy is wired.
