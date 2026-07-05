# CARCANMADCARLAN — LGU Onboarding → Activation Flow

**Status:** Reference / explainer (companion to `CARCANMADCARLAN-production-roadmap.md`)
**Purpose:** Plain-language explanation of how a new municipality goes from a public assessment
request to a live, isolated portal — and where their custom configuration data lives at each step.
**Last updated:** reflects the presentation-only demo built in the `stalltrack-landing` monorepo
(`apps/landing` = public site + onboarding workspace; `apps/admin` = platform admin console).

---

## 0. The core mental model: one system, many isolated LGUs

There is **no separate "LGU system."** StallTrack works like Gmail:

- **One** StallTrack application, **one** database.
- Each LGU is an "account," identified by a `MunicipalityId`.
- Each LGU sees **only their own** facilities, rates, vendors, and reports — enforced centrally by an
  EF Core **global query filter** (Phase 3).

A municipality's "actual system" **is** StallTrack, filtered to that municipality. We never install or fork
a separate copy per LGU (see roadmap §5: *do not create per-LGU projects or fork the backend*).

**Each LGU has its own custom data.** Cantilan has 8 facilities and a ₱30/day market fee; another LGU might
have 2 facilities and a ₱25 fee. These are **data rows tagged to each LGU**, not hardcoded constants — that
is Phase 4 (facilities & rates become configurable data) sitting on Phase 3 (isolation by `MunicipalityId`).

---

## 1. The four stages (example: onboarding "Carmen")

### Stage 1 — Assessment  *(public site + admin console)*
- Carmen's LEEO submits an assessment request on `stalltrack.site` (public form), including the
  **facilities/revenue activities** they manage.
- The request lands in the platform-operator admin console (`admin.stalltrack.site`).
- Operator reviews → **Approve** (a branded letterhead message) → the system emails Carmen a
  **secure onboarding link** (`stalltrack.site/onboarding/<token>`).
- **Data state:** only a "request" record exists. No Carmen facilities/users/data yet.

### Stage 2 — Onboarding  *(secure workspace — THIS is where configuration happens)*
Reached via the emailed link. Carmen configures its **own setup** in a guided workspace. Facilities appear as
a **pipeline of cards** (pre-filled from what they selected in the assessment), each mapped to a StallTrack
billing **archetype** and marked *Done* once its rate is set:

- **Facilities & fees** — per facility:
  - **Billing type / archetype:** `DailyStall, MonthlyRental, WeeklyMarket, PerTrip, PerHead, Custom`.
  - **Base rate** (₱ + per day/month/trip/vendor) — *except* per-head facilities.
  - **Sections** (optional) — e.g. a Public Market split into **Fish / Meat / Vegetables**, each with its own
    stall count and optional **section fee** (e.g. Fish ₱1 **per kilo**).
  - **Per-head animal rates** — for a **Slaughterhouse**: animal type + ₱/head (e.g. Hog ₱250, Cattle/Carabao
    ₱365), with custom types.
  - **Optional add-on fees** — e.g. **Electricity, Water**, each either a **fixed amount** or **metered**
    (billed per consumption), and marked *Applies to all* or *Optional (per stall)*.
  - **Ordinance reference** (optional), unit label, notes.
- **Administrator (Super Admin)** — the single LGU owner is designated here (name, position, official email).
  *Only the Administrator* is set at onboarding; passwords are never entered here.
- **Branding & receipts** — office/report-header name, OR (Official Receipt) series prefix + start, and an
  optional **logo/seal** upload.
- **Data state:** everything is written to a **DRAFT / STAGING** area, tagged `Carmen`.
  ⚠ **Not live.** Cantilan's live data is never touched.
- **Model:** white-glove / hybrid — the LGU provides data in the secure workspace; the operator reviews and is
  the one who commits at activation. Provisioning a live government portal is not a public self-service action.

> **What is NOT entered at onboarding:** vendors/stallholders (payors), daily collections, and utility
> (metered) readings. A **stall count is the number of spaces, not vendors.** All of that operational data is
> entered inside the **live portal after activation** — exactly like Cantilan's day-to-day operations. The
> Administrator also creates their own **admins and collectors** in the portal after activation, not here.

### Stage 3 — Validation  *(dry-run — VERIFY the config, do not enter data here)*
- The admin console generates a **dry-run** from Carmen's staged config:
  - **Config-derived KPIs:** Facilities · Total units (stall/space capacity) · Rates & fees · Collection models.
    *(No payor or projected-revenue figures — those depend on vendors that don't exist yet.)*
  - **Per-facility detail:** base rate (or per-head animal rates), sections + section fees, and add-on fees
    shown as fixed (₱) or **Metered**.
- Operator + Carmen review: *do the facilities, rates, and numbers look right?*
- If something is wrong, send it **back to Onboarding** for corrections (loop).
- **Data state:** still draft. Still not live.

### Stage 4 — Activation  *(go live — provision + account activation)*
- Operator clicks **Activate** and composes a **branded letterhead message** (To: the Administrator, subject:
  "*<LGU> — Your StallTrack portal is live*") containing the **Head account-activation link**, then sends it.
- On send, the system:
  - **commits** the staged config into the shared DB, every row tagged `MunicipalityId = Carmen`,
  - flips `Municipality.Status → Active`,
  - **provisions the Administrator account in an inactive / "must set password" state** (no password is ever
    set by the operator).
- The **Administrator receives the private activation link** → sets their own password → first login → lands in
  their scoped portal, then invites/activates their own staff (admins, collectors).
- Now Carmen's users log in and see **only Carmen's** facilities and data; Cantilan users still see only
  Cantilan. Same system, isolated by `MunicipalityId`.
- **Security note:** we email an activation **link**, never a password. This reuses the existing
  `MustChangePassword` / first-admin setup pattern, applied per LGU.

---

## 2. Two principles that must always hold (protecting Cantilan)

1. **Staged, not live.** Onboarding/Validation data lives in a draft/pending area and is **never** written to
   live operational tables as it is entered. Only **Activation** commits it, under the new `MunicipalityId`.
2. **Behind the goldens.** The commit path runs behind the Phase 0 snapshot tests, so provisioning a second
   LGU can never move Cantilan's financial numbers (roadmap Guiding Rule #1).
3. **Backup / restore / DB-health are platform-operator, whole-database operations.** The in-app backup,
   restore, and database-health tools (shipped for Cantilan in the hardening pass) act on the **entire shared
   database** — a restore rolls back *every* LGU, a backup dump holds *all* LGUs' data, and health metrics are
   DB-wide. In the multi-LGU world they must be exposed to a **platform operator**, never a per-LGU Head
   (Phase 5). They are correct and safe as-is while Cantilan is the only LGU.

---

## 3. API-ready config shape

The onboarding workspace produces a single object shaped to match the future API contract, so wiring later is
essentially `POST /api/onboarding/{token}` (and load it back for editing):

```
config = {
  facilities: [
    {
      name, type, archetype, rateAmount, rateUnit, unitLabel, units, ordinance, notes,
      sections: [ { name, units, fees: [ { label, amount, unit } ] } ],   // e.g. Fish → per-kilo
      rateItems: [ { label, amount } ],                                    // per-head animal rates
      addOns:   [ { label, basis, amount, unit, mode } ],                  // basis: Fixed amount | Per consumption
    }
  ],
  administrator: { name, position, email },     // Super Admin / Head — only this account is provisioned
  branding: { officeName, orPrefix, orStart, logoName },
}
```

The admin **Validation dry-run consumes the same shape**, so onboarding → validation flows with no reshaping
once the API exists.

---

## 4. What the demo shows vs. what the real version needs

| Stage | Demo (front-end only, built) | Real version (backend phases) |
|---|---|---|
| Assessment | Admin review / approve (branded) / issue link | Same, over the real API |
| Onboarding | **Full configuration workspace** (facilities pipeline, sections, animal rates, fixed/metered fees, Administrator, branding + logo) → local state | Writes the same config to **staging** (Phase 4/6) |
| Validation | **Dry-run** with config-derived KPIs + per-facility detail | Dry-run generated from staged config (Phase 6) |
| Activation | **Branded activation letterhead** + Head link + "live" state | **Commit to shared DB** under `MunicipalityId` + real account-activation email (Phase 3/6) |

**Backend prerequisites for the real flow:**
- **Phase 3** — `IMunicipalityOwned` + `MunicipalityId` + global query filters (isolation).
- **Phase 4** — facility archetypes + per-LGU `Rate` table (custom facilities/rates as data).
- **Phase 5** — subdomain/scoped login; token carries the municipality.
- **Phase 6** — the onboarding workspace + activation commit + account provisioning.

Per the roadmap and standing direction, these are deferred until a **real** second LGU, and always executed
behind the Cantilan safety tests. The current admin + landing demo is a faithful, **presentation-only** preview
of this flow, **decoupled** from the live system (two apps, two domains, no shared state); the surfaces connect
for real only once the API exists.

---

## 5. One-line summary

**Assessment** (request → approve) → **Onboarding** (configure facilities/sections/fees + Administrator +
branding into staging) → **Validation** (config-derived dry-run to verify) → **Activation** (commit under
`MunicipalityId`, go live, Administrator gets a branded account-activation link). One shared system; each LGU
isolated by `MunicipalityId`; payors/collections happen in the live portal later; Cantilan never affected.
