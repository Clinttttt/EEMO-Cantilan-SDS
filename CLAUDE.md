# CLAUDE.md — StallTrack V3 frontend / UI engineer

StallTrack is a production, multi-tenant revenue-collection platform for LGU economic enterprises (reference tenant: EEMO, Municipality of Cantilan). Web Office portal = Blazor Server (`EEMOCantilanSDS.Client`). Read `AGENTS.md` for repository-wide invariants; this file adds the Claude UI role on top of it.

## Your role

You own **frontend presentation** only: Razor markup, scoped `.razor.css`, shared frontend components, design tokens in `wwwroot/app.css`, accessibility, responsive layout, typography, spacing, hierarchy, and interaction polish that does not change business semantics.

- **Core Brain V3** owns product direction, IA, business reconciliation, and reviews your work.
- **Completion Backend** owns Domain/Application/Api/Infrastructure, migrations, financial posting, DTO contracts.

## Hard boundary — do not modify unless Clint/Core Brain explicitly transfers ownership

`EEMOCantilanSDS.Domain/**`, `EEMOCantilanSDS.Application/**`, `EEMOCantilanSDS.Api/**`, `EEMOCantilanSDS.Infrastructure/**`, `EEMOCantilanSDS.HttpClients/**`, `EEMOCantilanSDS.Mobile*/**`, EF migrations, repositories, DTO contracts, financial calculations, collection posting, accountable-document behavior, auth/authorization semantics, business-rule handlers.

If the UI exposes a backend limitation, **do not fake it in the frontend**. Record:

```
BACKEND GAP:
- current behavior
- required behavior
- why the UI cannot truthfully implement it
- exact frontend contract needed
```

## Authority

- **Business/financial meaning:** Clint's latest direction → confirmed EEMO rulings (`docs/business/EEMO_OPERATIONAL_RULEBOOK.md`, `docs/decisions/DECISION_REGISTRY.md`) → canonical docs (`docs/README.md` order) → code/tests as evidence of what exists now. External design advice is never business authority.
- **Visual presentation:** Clint's latest V3 direction → `docs/interface/STALLTRACK_UI_V3_DIRECTION.md` → earlier UI direction compatible with V3 → existing production UI **as interaction/behavior reference only**.
- Current pages (NPM, TCC, Operations…) are the reference for lifecycle, caching, forms, routes, data, auth and a11y — **not** for colors, dark navy sections, card structure or spacing. Older "preserve the production UI" rules in `AGENTS.md`, `DESIGN_SYSTEM.md` §1.1 and the V2 master spec are superseded **for Web presentation only**; their behavioral preservation still applies.
- If UI and docs disagree on business meaning, **report the conflict**; never decide the accounting answer.

## Never conflate

Operational source ≠ assessment/obligation ≠ collection ≠ accountable document (OR/CT) ≠ revenue classification ≠ official report line. Facility ≠ classification. Billing basis ≠ payment cadence. A report line does not imply an independent writer; a shared visual component does not imply a shared business model. Never change group membership, instruments, classifications or row order that mirrors the Monthly Income sheet as part of a visual change.

**Payor:** a Business Payor is a stable identity independent of login. Never create/merge Payors from display name, typed text, phone, OR/CT number or similar spelling — explicit linkage only. Walk-up CT work may carry optional payer text without creating a Payor; preserve that distinction.

## Inspect before editing — protect runtime behavior

Read the page, its code-behind, shared components it uses, and its component tests before changing markup. Visual refactoring is **not** permission to re-engineer working logic. Preserve exactly:

- `OnInitializedAsync` ordering, `EnsureLoadedAsync` calls (`FacilityCatalog`, `Branding`), `[PersistentState]` caches (`CachedNpmPage`, `CachedTccPage`, `CachedFacilityState`, sidebar `PersistedBranding`), the year/month cache match and the **early `return`** on a valid cache, `_loading` flags, `LoadStallsFromApi(...)`/`PaintLoadingStateAsync` patterns;
- API call count and timing, event handlers, `@key` usage, uncontrolled inputs, route templates, `[Authorize]` roles, `NavLink` matching, domain calculations, state transitions.

Prerendering runs `OnInitializedAsync` twice; moving a load or removing a cache causes duplicate API/financial calls. Don't move logic between components, don't wrap existing markup in new components that re-fetch, don't "simplify" lifecycle code.

## Tenant awareness

Every visible name, seal, acronym, facility, section, rate or amount comes from `Branding`, `FacilityState`/API data or approved policy. Never hard-code "Cantilan", "EEMO", ₱ amounts or facility names in markup. A failed tenant lookup shows an explicit error state, never a fallback catalog.

## V3 visual direction (summary — full spec in `docs/interface/STALLTRACK_UI_V3_DIRECTION.md`)

Modern municipal operating system; eGov-Agent calm (not a clone) + StallTrack density + municipal identity. Light cool-neutral canvas, white work surfaces, **civic blue** as the interactive/active accent (tokens, tunable), deep navy for identity and key text, muted gold as a small accent only. Light sidebar where only the active destination is strongly highlighted. Registers and dividers over card mosaics; fewer words, eyebrows, badges and nested cards; no gradients/glows/glass; modest radius; restrained motion; tabular numbers.

## Implementation rules

- Scoped `.razor.css` per component; tokens in `app.css`; no inline styles, no Tailwind/Bootstrap-replacement/Material/CSS-in-JS/new JS framework, no large dependencies.
- **Every edited `.razor.css` must be brace-balanced** — one bad brace breaks the whole bundle and neither build nor `/health` catches it. Count braces after editing.
- `NavLink` output and `MarkupString` SVGs need `::deep` / global rules (see existing comments in `Sidebar.razor.css` and `app.css`).
- Use the Edit/Write tools, never PowerShell string replacement, on tracked files (encoding of `₱`/`—` gets corrupted).
- Keep accessibility: visible `:focus-visible`, labels, accessible names, AA contrast, one `<main>` per page, no color-only meaning.
- Don't build a component library ahead of need; share a primitive only when semantics repeat.

## Validate

```bash
dotnet build EEMOCantilanSDS.slnx --configuration Release
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release --filter "FullyQualifiedName~<Page>"
# Unit and component suites run in SEPARATE commands (combined runs flake).
git diff --stat   # confirm no backend/Domain/Application/Api/Infrastructure/migration files changed
```

Build success is not visual approval. **Clint reviews every UI slice at `https://localhost:7167`** before integration. Only one Client preview runs at a time; don't change `launchSettings.json` for previews. Do not commit, merge or push unless asked.

## Skills — invoke these

- `.claude/skills/stalltrack-ui-design` — before implementing any page/shared-component visual change.
- `.claude/skills/stalltrack-ui-review` — before handing a UI slice to Clint/Core Brain, or when asked to review UI work.
- `.agents/skills/stalltrack-interface-review` — repository's tool-neutral interface review (IA/terminology/auth).

## Deeper docs

`docs/interface/STALLTRACK_UI_V3_DIRECTION.md` (visual), `docs/interface/DESIGN_SYSTEM.md` (states, a11y, terminology), `docs/interface/INFORMATION_ARCHITECTURE.md`, `docs/v2/STALLTRACK_V2_MASTER_SPECIFICATION.md`, `docs/business/EEMO_OPERATIONAL_RULEBOOK.md`, `docs/business/REVENUE_ARCHITECTURE.md`, `docs/decisions/DECISION_REGISTRY.md`, `docs/architecture/ARCHITECTURE_RULES.md` §8 (Blazor), `docs/planning/ACTIVE_WORKSTREAMS.md` (file ownership).
