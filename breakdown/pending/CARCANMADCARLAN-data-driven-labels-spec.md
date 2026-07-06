# CARCANMADCARLAN — Data-Driven Labels & Branding Slice (Spec)

Status: **proposed** · Owner split: **backend = this repo (me)**, **Client UI = Codex**
Guiding rule: **Cantilan must stay byte-for-byte identical** (same visible text, same seal, same
sidebar items). Every binding falls back to the current hardcoded literal, so if branding is missing
or a call fails, the UI renders exactly what it does today. No financial goldens are touched — these
are presentation strings only.

---

## 1. Problem (what the user is seeing)

Logged in as the **Carmen** Head (`console.stalltrack.site`), the shell still shows Cantilan's identity:

- **Sidebar "Facilities" group lists all 8** facilities (Public Market, Tampak Comm., New Comm.
  Center, BBQ Stand, Iceplant, Slaughterhouse, Transport Terminal, Tabo-an) even though Carmen only
  operates 5 (NPM, TCC, SLH, TRM, TPM). The **dashboard "Revenue by Facility" cards are already
  correct** (they show 5) because they bind to the API; the sidebar does not.
- **Header/eyebrows/print headers** read `EEMO Admin`, `EEMO Office`,
  `Economic Enterprise & Management Office`, and the seal is `/images/LGU_CANTILAN_LOGO.jpg` —
  hardcoded across ~59 Client files.

### Root cause — sidebar
`EEMOCantilanSDS.Client/Components/Pages/Shared/Sidebar.razor` builds the facility nav from the
**enum**, not the tenant's facilities:

```razor
@foreach (var fc in Enum.GetValues<FacilityCode>())   // ← always all 8
{
    if (!Display.TryGetValue(fc, out var display)) { continue; }
    ...
}
```

It already loads the tenant's real facilities into `FacilitySummaries` (via
`FacilitiesApi.GetFacilitySummariesAsync`) but only uses them for the unpaid **badge counts**, not to
decide **which items to render**.

### Root cause — office/seal labels
Literal strings (`EEMO Admin`, `EEMO Office`, `Economic Enterprise & Management Office`,
`LGU_CANTILAN_LOGO.jpg`) are inlined in page markup across ~59 files (every `*Reports.razor` print
header, `Accounts.razor`, `ClosedAccounts.razor`, dashboard eyebrow, the sidebar seal, etc.).

---

## 2. Data contracts (what exists today)

- **`Municipality`** (Domain/Entities/Tenancy): `Code, TenantCode, Name, Province, Address?,
  SealPath?, OfficeName, Status, IsDefault, IsActive`. **No office acronym field.**
- **`MunicipalityBrandingDto`**: `Code, TenantCode, Name, Province, OfficeName, SealPath, Status,
  IsActive`.
- **Branding endpoint**: `GET /api/municipalities/{identifier}/branding` **[AllowAnonymous]**
  (pre-login theming by subdomain). No authenticated "current LGU" branding endpoint yet.
- **`FacilitySidebarSummaryDto(Code, Name, ShortName, UnpaidCount, …)`** — already returned by
  `GET /api/facilities/summaries` scoped to the caller's tenant. The dashboard cards use it.
- JWT carries `municipality` (TenantCode) and `municipality_id` claims.

**Gap:** (a) the "EEMO/LEEO" short acronym isn't stored anywhere; (b) there's no authenticated
endpoint for the Client to fetch its **own** LGU's branding post-login.

---

## 3. Backend changes (this repo — additive, safe, mine)

Each is independently shippable and leaves Cantilan visually unchanged.

### B1. Add `OfficeAcronym` to the Municipality branding data
- Add nullable `string? OfficeAcronym` to `Municipality` (additive EF migration — no data loss).
- Thread it through `Municipality.Create(...)` and the activation command
  (`ActivateMunicipalityCommand` → `branding.officeAcronym`), optional. Add it to
  `MunicipalityBrandingDto`.
- **Seed/patch Cantilan** so `OfficeAcronym = "EEMO"`, `OfficeName = "Economic Enterprise &
  Management Office"`, `SealPath = "/images/LGU_CANTILAN_LOGO.jpg"` — i.e. exactly the current
  literals. (Carmen's would be its own, e.g. acronym per its office.)
- Do **not** make it required in activation; when omitted the Client falls back (see C-guards).

### B2. Authenticated "current branding" endpoint
- Add `GET /api/municipalities/current/branding` **[Authorize]** → resolves the caller's
  municipality from `municipality_id` (fallback tenant code) and returns `MunicipalityBrandingDto`
  (now incl. `Address`, `OfficeAcronym`). Reuse `IMunicipalityRepository`; new query handler mirrors
  `GetMunicipalityBranding` but keyed on the current tenant instead of a route identifier.
- Anonymous by-identifier endpoint stays as-is for pre-login theming.

### B3. Tests (backend)
- New handler test: current-branding resolves the authenticated tenant; Cantilan returns the exact
  seeded strings; a second LGU returns its own.
- Extend the activation test to assert `OfficeAcronym` round-trips.
- Full suite stays green; **financial goldens untouched** (no fee/report logic changes).

---

## 4. Client changes (Codex — bind with fallback, never hardcode)

### C0. `BrandingState` (one load, cascaded)
- Add a scoped `BrandingState` service loaded once after auth (calls
  `GET /api/municipalities/current/branding`), exposing `OfficeName`, `OfficeAcronym`, `SealPath`,
  `Name`, `Province`, `Address`. Cascade it (or inject) into layouts/pages.
- **Fallback constants** = today's literals: acronym `"EEMO"`, office
  `"Economic Enterprise & Management Office"`, seal `"/images/LGU_CANTILAN_LOGO.jpg"`. Used when the
  call hasn't returned yet or fails. This is the "nothing breaks" guarantee.

### C1. Sidebar — the immediate fix (small, low-risk)
Replace the enum loop with the tenant's actual facilities (already loaded):

```razor
@foreach (var f in FacilitySummaries ?? new())
{
    if (!Display.TryGetValue(f.Code, out var display)) { continue; }
    var unpaid = _unpaidByCode.GetValueOrDefault(f.Code);
    var href = "/facility/" + f.Code.ToString().ToLower();
    <NavLink href="@href" class="@NavItemClass(href)" @onclick="() => SetPendingPath(href)">
        @((MarkupString)display.Icon)
        <span>@display.Label</span>
        @if (unpaid > 0) { <span class="nav-badge">@unpaid</span> }
    </NavLink>
}
```
- Keep the `Display` dict for **icon + short label** (presentation only). Optionally prefer
  `f.ShortName`/`f.Name` for the label so it reads the LGU's own naming.
- Preserve ordering (order `FacilitySummaries` by `Code` to match today's enum order).
- **Cantilan impact: none** — its summaries return all 8 in the same order.
- Seal `<img>` in the brand block → `BrandingState.SealPath` (fallback Cantilan logo).
- The brand seal `alt` and any "EEMO" text → `BrandingState.OfficeAcronym`.

### C2. Header / eyebrows / user chip
- `Dashboard` eyebrow `EEMO Admin · OVERVIEW`, breadcrumb `EEMO Admin`, user chip `EEMO Office`,
  `Accounts.razor` sub-labels → `@BrandingState.OfficeAcronym Admin` /
  `Head · @BrandingState.OfficeAcronym Office`, etc.

### C3. Print report headers (`*Reports.razor`, `ClosedAccounts.razor`)
- Replace `<h2>Economic Enterprise & Management Office</h2>`, `Prepared By EEMO Office`,
  `Head, EEMO Office`, and the print seal `src` with `BrandingState` values.
- **Careful:** keep the exact print layout/markup/classes; only swap the text/`src`. These are
  official documents — verify a print preview per facility after binding.

### C4. Do it in waves (each shippable, verify Cantilan parity each time)
1. Sidebar facility list (C1) — highest visible impact, smallest change.
2. `BrandingState` + header/seal/eyebrow (C0, C2).
3. Print report headers (C3) — one PR, all `*Reports.razor` together, with print-preview QA.

---

## 5. Non-breaking guardrails (must all hold)

- Every bound value has a **fallback to the current literal**; a failed/absent branding call renders
  today's Cantilan text exactly.
- Cantilan's branding row is **seeded to the exact current strings** before any Client binding ships,
  so Cantilan is visually identical.
- Sidebar order preserved (order by `Code`); active-state logic (`/facility/{code}`) unchanged.
- No change to routes, auth, fee math, or report aggregation → **financial goldens unaffected**.
- Backend endpoints are additive; the anonymous pre-login branding endpoint is untouched.
- `PageTitle` strings (e.g. `EEMO Admin — NPM Reports`) can stay last (cosmetic browser-tab text);
  bind opportunistically, not on the critical path.

## 6. Acceptance criteria

- Carmen Head: sidebar shows **exactly its 5** facilities; header/seal/print headers show Carmen's
  office + acronym + seal.
- Cantilan Head: sidebar shows **all 8**; every label/seal identical to pre-change (screenshot diff).
- `GET /api/municipalities/current/branding` returns the caller's LGU (200) and is `[Authorize]`.
- Solution builds; full suite green; goldens byte-for-byte.
- Post-deploy: `/health`+`/health/ready`=200; deployed image == HEAD.

## 7. Rollback
- Client waves are independent PRs — revert a wave without touching backend.
- Backend `OfficeAcronym` migration is additive (nullable); the `current/branding` endpoint is new
  and unreferenced by Cantilan's existing flows, so reverting the Client leaves it harmlessly unused.

## 8. Sequencing vs other work
- **Prereq for full binding:** B1 + B2 (backend) must ship first so the Client has data + acronym.
- **Sidebar C1 can ship immediately and alone** — it needs no backend change (uses existing
  `FacilitySummaries`). This resolves the "sidebar shows 8" issue on its own.
- Coordinate with Codex: backend PR (B1–B3) → then Client waves (C1 → C0/C2 → C3).
