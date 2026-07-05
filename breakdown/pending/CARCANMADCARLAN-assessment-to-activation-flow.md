# CARCANMADCARLAN — LGU Assessment → Activation: Full Flow Reference

**Purpose:** the definitive, screen-by-screen description of how a new municipality goes from a public
assessment request to a live, isolated StallTrack portal — and **where each piece lives** (platform vs
backend) so we never lose the thread.
**Companion docs:**
- `CARCANMADCARLAN-onboarding-flow.md` (earlier explainer)
- `CARCANMADCARLAN-production-roadmap.md` (phase plan/status)
- `CARCANMADCARLAN-activation-api-integration.md` (exact activation API contract)

---

## 0. The one rule that governs everything

**Stages 1–3 are staged/draft data owned by `stalltrack-platform`. Only Stage 4 (Activation) writes to the
live StallTrack backend.** Nothing an LGU types during assessment/onboarding/validation can touch Cantilan or
any other live LGU. Activation is the single, atomic commit point.

```
[ stalltrack-platform : draft/staging ]            [ StallTrack backend : live, isolated by MunicipalityId ]
Assessment → Onboarding → Validation  ── Activate ──►  POST /api/activation/municipality  → LGU goes live
```

---

## 1. Actors

- **LGU focal person** (e.g. Madrid LEEO officer) — submits the assessment, later configures the onboarding
  workspace via a private link, submits for validation.
- **Platform Operator** (you — the Cantilan/system owner) — reviews requests, approves + issues the onboarding
  link, advances to validation, approves validation, and finally activates. Authenticated to the backend as
  the **default (Cantilan) SuperAdmin** (the only role allowed to call the activation API).
- **LGU Head/Administrator** — the single owner account provisioned at activation; sets their own password via
  a secure link on first sign-in, then runs their portal.

---

## 2. Stage 1 — Assessment (public site)

**Where:** `stalltrack.site` public form → stored as an **assessment request** in the platform.
**Screen:** "Request assessment — {Municipality}".

**Captured:**
- Municipality, Province (cluster context: CARCANMADCARLAN).
- **Requesting office** (e.g. Local Economic Enterprise Office / EEMO), **Focal person** (full name),
  **Position/designation**, **Official email**, **Contact number**.
- **Facilities & revenue activities managed** (multi-select, each maps to a collection model):
  Public Market — daily stalls · Commercial Center / monthly rental · Barbecue / food stalls ·
  Iceplant / cold storage · Slaughterhouse — per head · Transport / Bus Terminal — per trip ·
  Weekly / Tabo market — market day · Other.
- **Approximate scale:** vendors/stallholders, facilities to onboard, field collectors.
- Authorization status, acknowledgement, notes.

**Result:** a request appears in the operator console as **PENDING REVIEW**. No backend data yet.

---

## 3. Stage 2 — Onboarding (private workspace)

### 3a. Operator reviews & approves *(operator console → "Pending")*
- Console shows KPIs (Total requests · Pending review · Onboarding · Declined) and the submitted assessment
  detail (municipality, province, facilities managed, office, focal person, position, email, contact,
  approx. vendors, authorization status, acknowledged, notes).
- Operator clicks **Approve** → composes a **branded approval email** (To: official email; Subject:
  "StallTrack assessment — {Municipality}") with an **auto-generated secure onboarding link**
  (e.g. `https://stalltrack.site/onboarding/madrid-9271bc89`) → **Send approval & open onboarding room**.
- Request moves to **ONBOARDING**; the private onboarding room opens (4-step tracker:
  Assessment ✓ → Onboarding → Validation → Activation).

### 3b. LGU configures *(onboarding workspace via the private link)*
**Screen:** "Configure {Municipality}." with a configuration-progress bar (e.g. 0 of 3 sections).

**Sections:**
1. **Facilities & fees** — one card per facility selected in the assessment, each marked **Done** once its
   rate is set. Per facility:
   - **Billing archetype:** DailyStall · MonthlyRental · WeeklyMarket · PerTrip · PerHead · Custom.
   - **Base rate** (₱ + unit) — e.g. Public Market ₱30/day; Commercial Center ₱2,400/month.
   - **Unit count** — stalls/spaces (e.g. 120 stalls, 24 spaces).
   - **Sections** (optional) — e.g. Public Market → Fish (40 stalls, **₱1/kilo**), Meat (30), Vegetables (50).
   - **Per-head animal rates** (Slaughterhouse) — Hog ₱250, Cattle/Carabao ₱365, custom types.
   - **Optional add-on fees** — Electricity, Water; each **Fixed** or **Metered**; "Applies to all" or
     "Optional (per stall)".
   - Ordinance reference, unit label, notes.
2. **Administrator (Super Admin)** — the single LGU owner: full name, position, official email. *No password.*
3. **Branding & receipts** — office/report-header name, **OR series** (prefix + start number), optional
   **logo/seal** upload.

**Not entered here:** vendors/stallholders (payors), daily collections, metered readings, additional admins,
collectors. Those are entered/created in the **live portal after activation**.

### 3c. LGU submits for validation
- LGU completes the onboarding checklist (Confirm facilities & scope · Authorized users · Validation dry-run ·
  Rates & ordinance references · Branding, seal & OR series) → **Submit for validation**.
- Operator console shows the LGU response timeline ("Onboarding link opened", "Onboarding submitted for
  validation — checklist complete") and, once complete, **"{Municipality} is ready to advance to Validation"**
  with an **Advance to Validation stage** button.

---

## 4. Stage 3 — Validation (dry-run, verify only)

**Where:** operator console → "Validation".
**Screen:** "Configured facilities" — the full staged config rendered for review, e.g.:
- **Public Market · Daily Stall** — ₱25/day · 120 stalls; Sections: Fish 40 (₱1/kilo), Meat 30, Vegetables 50;
  Additional fees: Electricity (Metered), Water (Metered).
- **Commercial Center · Monthly Rental** — ₱2,400/month · 24 spaces.
- **Slaughterhouse · Per Head** — Hog ₱250/head, Cattle/Carabao ₱365/head.
- **Transport Terminal · Per Trip** — …
- **Config-derived KPIs:** Facilities · Total units · Rates & fees · Collection models. *(No payor/revenue
  numbers — vendors don't exist yet.)*

**Actions:** operator reviews; if wrong → send back to Onboarding for corrections (loop). If correct →
**Approve validation & send to Activation** → request moves to **READY TO ACTIVATE**.

---

## 5. Stage 4 — Activation (go live — the ONLY backend write)

**Where:** operator console → "Activation" → then the **StallTrack backend**.
**Screen:** Activation queue ({Municipality} · READY) with a **Provision summary** (Facilities · Total units ·
Rates & fees · Collection models) and **Accounts to be provisioned** (the Head: name, official email,
Administrator/Super Admin). Note on screen: *"Activation commits {LGU}'s configuration under its own tenant,
sets the portal live, and provisions the accounts in an inactive state. The Head receives a secure link to set
their password — no password is ever set here."*

**Actions:**
1. Operator clicks **Activate {Municipality} portal** → composes the **activation email** (To: official email;
   Subject: "{Municipality} — Your StallTrack portal is live") with the **Head account-activation link**
   (e.g. `https://carmen.stalltrack.site/activate/2e4967fd`) → **Send & activate {Municipality}**.
2. On send, the platform calls the backend **once**:
   `POST /api/activation/municipality` (see `CARCANMADCARLAN-activation-api-integration.md`).
3. The backend **atomically**:
   - applies branding + flips `Municipality.Status` → **Active**,
   - creates the LGU's **facilities** and **fixed rates** under its own `MunicipalityId`,
   - provisions the **Head** (SuperAdmin) in a **must-set-password** state,
   - returns the result (incl. the one-time secret / triggers the set-password link).
4. Request moves to **ACTIVATED**.

**Post-activation:** the Head opens the secure link → sets their password → first sign-in lands in their
**scoped portal**, pre-populated with everything from onboarding as their **initial data**. They then create
their own admins/collectors and begin entering payors/stalls and day-to-day collections. Their fees compute
from the rates committed at activation. Cantilan and every other LGU are unaffected.

---

## 6. State lifecycle

Platform-side request status (owned by `stalltrack-platform`):
```
PendingReview → (Approve) → Onboarding → (Submit) → Validation → (Approve) → ReadyToActivate → (Activate) → Activated
                     └────────────────── Declined (any pre-activation point) ──────────────────┘
                     Validation ── (corrections) ──► back to Onboarding
```
Backend-side registry status (`Municipality.Status`, only two states matter): **Upcoming → Active** (flipped by
the activation commit). The fine-grained pipeline states above live in the platform, not the backend.

---

## 7. What the backend commits today vs. the full demo config

The activation API is **live** but commits a **subset** of the rich onboarding config. Map the config down to
the contract at activation; the rest is entered in the portal later or is a planned backend extension.

| Onboarding config item | Backend today (activation contract) | Notes / gap |
|---|---|---|
| Municipality identity + branding (office name, address, seal) | ✅ committed | `branding.*` |
| Head / Administrator account | ✅ committed (must-set-password) | via temp password today (see §8) |
| Facilities (code, name, short name, archetype) | ✅ committed | `facilities[]` |
| Fixed ordinance rates (NPM daily, NPM fish/kg, SLH hog, SLH large, TPM vendor, TRM trip) | ✅ committed + **drive the LGU's fee math** (Phase 4B-ii) | `rates[]` keyed by `FeeRateKey` |
| Monthly-rental base rate + space count (TCC/NCC/BBQ/ICE) | ⛔ not in activation | monthly rentals are **per-stall** (`Stall.MonthlyRate`), entered in the portal |
| Market **sections** (Fish/Meat/Vegetables) + section stall counts | ✅ committed (via `facilities[].stallGroups[].section` + `count`) | fish per-kilo rate via `NpmFishPerKilo` |
| **Metered** add-on fees (Electricity/Water) | ◑ fee **flags** committed on stalls (`ApplicableFees.Electricity/Water`); metered *readings* are portal ops | |
| **Unit counts** (stalls/spaces) | ✅ committed (via `stallGroups[].count`, per section/space) | stalls are the empty spaces; occupants added in the portal |
| **OR series** (prefix/start) | ⛔ not in activation | OR numbers are manually entered per record today |
| Custom animal types beyond hog/large | ⛔ not in activation | SLH custom rates are per-transaction today |
| Payors/stallholders, collectors, extra admins | ⛔ never at onboarding | created in the live portal after activation |

**Also demo-ahead-of-backend:**
- **Subdomains** (`carmen.stalltrack.site`) — pre-login branding/subdomain routing is Phase 5 (not started);
  the backend is a single host today. The activation email's subdomain link is presentational until then.
- **Head activation LINK** — ✅ implemented: activation provisions the Head **inactive** and returns a one-time
  `activationToken`; the Head sets their own password via `POST /api/activation/set-password` (anonymous,
  rate-limited, single-use, 7-day expiry). Build the link as `https://{lgu}.stalltrack.site/activate/{token}`.

---

## 8. Backend implementation status (as built)

- ✅ **Per-request tenant isolation** — reads/writes scoped by the logged-in user's `MunicipalityId`.
- ✅ **Per-LGU cache namespace** — distinct `TenantCode` per LGU (Cantilan = `cantilan-sds`).
- ✅ **Per-LGU fee math (Phase 4B-ii)** — NPM/SLH/TPM/TRM fees resolve from the LGU's `FacilityRate` rows
  (constant fallback → Cantilan byte-for-byte).
- ✅ **Activation API (Phase 6)** — `POST /api/activation/municipality`, platform-operator-authorized
  (default-LGU SuperAdmin), atomic commit of branding + facilities + rates + **stalls/units** + Head. Guards:
  default LGU and already-active are rejected. Head provisioned **inactive** with a one-time activation token;
  activated via `POST /api/activation/set-password` (anonymous, rate-limited, single-use).
- ✅ **Self-service editing** — Head edits own fees (`PUT /api/facility-rates`) + branding
  (`PUT /api/municipality-profile`), today-forward, per-tenant.
- ◻ **Platform wiring** — `stalltrack-platform` still needs to call the live endpoints. Contract:
  `CARCANMADCARLAN-activation-api-integration.md`.
- ◻ **Deferred backend extensions**: OR-series generation, custom SLH animal types at activation, metered
  add-on fee readings (portal ops), subdomain/pre-login branding (Phase 5), mobile offline-cache namespacing,
  and the Client Settings UI for the self-service endpoints.

---

## 9. One-line summary

**Assessment** (public request) → **Onboarding** (LGU configures facilities/rates/sections/fees + Head +
branding in a private room — all staged in the platform) → **Validation** (operator dry-run review) →
**Activation** (operator clicks Activate → backend atomically commits everything under the LGU's own
`MunicipalityId`, sets it live, provisions the must-set-password Head → LGU logs in to a scoped portal
pre-filled with its onboarding data). Stages 1–3 are platform-side drafts; only Activation writes to the live,
isolated backend; Cantilan is never affected.
