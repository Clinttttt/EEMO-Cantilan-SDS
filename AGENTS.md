# AGENTS.md — entry point for coding agents

StallTrack — a multi-tenant revenue collection platform for LGU-managed economic enterprises. In production.
Reference tenant: Municipal Economic Enterprises Development Office (MEEDO), Municipality of Cantilan, Surigao del Sur. Historical filenames/namespaces may still use EEMO for compatibility.

## Authority and conflicts

- Explicit current task and business rulings govern the requested behaviour; they take precedence over general skill guidance.
- Accepted business and architecture documentation records intended semantics.
- Current code, migrations, tests, CI/workflows and verified production behaviour are evidence of what the system does now.
- When those disagree, surface the contradiction and determine which source is stale before changing behaviour. Never silently
  resolve a financial or business contradiction merely by choosing the implementation over the documentation.

## Read these before changing code, in this order

1. `docs/v2/STALLTRACK_V2_MASTER_SPECIFICATION.md` — first-stop product/architecture map. Its historical matrix is not the current work queue; follow its 2026-10-06 overlay.
2. `docs/planning/CURRENT_RELEASE_STATE.md` — current implementation/release checkpoint. Use this before interpreting any older phase note as live.
3. `docs/README.md` — canonical documentation map, authority order, and conflict handling.
4. For Terminal/Transportation, Fish/Meat/Weight & Measure, source-native collection, NPM Daily Collect All, Business Payor retirement, or complete Monthly Income, read `docs/planning/OFFICE_CLARIFICATION_REFACTOR_20261006.md`, IA-067/IA-068, ADR-007, and the 2026-10-06 office evidence.
5. Read the relevant authoritative business/architecture source — especially `docs/business/EEMO_OPERATIONAL_RULEBOOK.md`, `docs/business/EEMO_BUSINESS_RULES.md`, `docs/business/REVENUE_ARCHITECTURE.md`, `docs/architecture/ARCHITECTURE_RULES.md`, and `docs/architecture/APPLICATION_PATTERNS.md`.
6. `docs/decisions/DECISION_REGISTRY.md` — check supersession/status before relying on an older decision.
7. Inspect current Git branch/worktrees, code, migrations, tests, CI/workflows, and verified production behavior as implementation evidence.
8. Read relevant domain-specific documents under `docs/interface/`, `docs/security/`, and `docs/testing/` before changing those areas.
9. Read `docs/planning/ACTIVE_WORKSTREAMS.md` only when a current task explicitly reactivates linked-worktree coordination; it is now a historical coordination record, not default execution authority.

The repository knowledge base is tool-neutral. `.agents/skills/` contains repeatable review/runbook procedures; skills never override the canonical documents above.

## High-risk invariants

- **Cantilan is the accuracy baseline.** A change made for another municipality must never move a Cantilan figure.
- **Tenant scoping fails closed.** Tenant-owned reads use the global query filter. `IgnoreQueryFilters()` is limited to
  documented pre-tenant or deliberate cross-tenant paths; every use needs a reason, and cross-tenant reach requires the
  dedicated platform-operator guard.
- **Rates are data and dates matter.** Resolve through `IFeeRateResolver`; `FeeRates` constants are a fallback only.
  Transactions use their business date. A screen quoting a monthly fee uses `RatePeriod.AsOf` rather than inventing a date.
  A stall's daily fee comes from `Stall.ResolveDailyFee(resolvedRate)`, never the stored `MonthlyRate`.
- **An NPM market month follows its explicit `NpmMonthBasis`.** `RentGoal` settles daily collections against the
  tenant's fixed monthly obligation and may require a month-end top-up. `PureDays` has no fixed monthly rent: its
  obligation is the resolved daily fee × chargeable days, with no top-up. Both use the shared month rule/ledger
  (`DailyBilledMonthObligation` / `MonthCredit` / `MonthOutstanding`) so Expected − Collected − Credits = Outstanding;
  never infer the basis from a stored monthly amount or reproduce the arithmetic in a client.
- **Money belongs to the period and occupancy that incurred it.** Use `DomainRules.TermLastDay` for exact terms,
  `StallOccupancy.AnsweringForMonth` for the one monthly owner, and the past occupancy's contract rate. Do not infer
  historical liability from the stall's current holder or current rate.
- **Reconcile reports like for like.** Compare the same tenant, facility, occupancy scope, period, as-of date and money
  basis. Period, lifetime, assessment and receipt views may legitimately differ; when definitions match, they must share
  the same source or rule.
- **Uniqueness is per tenant.** A username, email, stall number or OR number is unique within a municipality,
  not globally, so a cross-tenant lookup must handle multiple matches.
- **Authentication uses the established boundaries.** Reuse the authorization guards. Authenticated endpoints use an
  `AddApiHttpClient` client; the anonymous `IAuthApiClient` contains anonymous endpoints only. MFA enrollment is currently
  optional; once enabled, the established two-step sign-in and recovery boundaries apply.
- **Mobile writes are retry-safe.** Preserve the offline queue and `ClientOperationId` idempotency. The collector business
  date comes from the server-issued session date when available, with the device clock only as fallback.
- **Scoped CSS must stay brace-balanced.** One unbalanced brace in a `.razor.css` corrupts the whole bundle and
  breaks every page; neither `dotnet build` nor `/health` catches it.
- **Prerendering runs `OnInitializedAsync` twice.** Never consume a one-time token there.
- **Preserve proven behavior; V3 governs Web presentation.** Navigation/composition changes are not permission to alter money, routes, source authority, lifecycle or specialized workflow semantics. For visual presentation use `docs/interface/STALLTRACK_UI_V3_DIRECTION.md`; do not revive the old dark/heavy-navy baseline unless a specific screen requires it.
- **Source-native identity is the target.** Do not add new dependencies on a Business Payor master, manual Business Payor linking, or name matching. Use typed source-owned identity and only server-confirmed eligible operations. Equal names never establish identity.
- **Terminal is not Transportation/Parking.** Income From Terminal owns Comfort Room / Pull Pul Vans, Cargo Vans / Tricycad and prospective vehicle-class assistance. Transportation/Parking is a separate CT direct-amount source. Preserve historical TRM/Transportation rows without guessed reclassification.
- **Fish/Meat is independent from NPM.** Vendor Fee is direct amount under the independent Fish/Meat source; Weight & Measure requires a registered Fish/Meat vendor and keeps quantity × approved effective rate evidence.
- **SRC identifies canonical Collections.** A physical OR/CT serial is separate accountable-form evidence and does not gate, identify, or automatically get consumed by a canonical Collection (IA-062).
- **Official report adjustments never rewrite cash.** Head-only Monthly Income target/report adjustments preserve system basis and audit history; they do not edit Collections, source obligations, remittance, collector position, or accountable-form events.

## Commands

```bash
dotnet build EEMOCantilanSDS.slnx --configuration Release

# Run the suites SEPARATELY — combining them causes a bUnit timing flake.
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj --configuration Release
# Requires Docker/Testcontainers.
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj --configuration Release
```

Migrations are **additive only** (production applies them at startup).

## Working agreements

### Current repository / branch discipline

- The default primary working checkout is `C:\dev\stalltrack\eemo`. As of 2026-10-07 the accepted local integration checkpoint is `integration/report-governance-ui` at `6d3362f9`; always inspect `git status --branch`, `git log -1`, and `docs/planning/CURRENT_RELEASE_STATE.md` because the checkpoint may advance.
- For normal sequential work, create a short-lived feature branch in that same primary checkout, finish/commit it, then return to and integrate into the local integration branch. Do **not** create a new worktree for a small sequential tweak.
- Use an additional Git worktree only when two agents genuinely need different branches checked out concurrently. Worktrees are linked checkouts of the same repository, not separate project authorities; their accepted commits must be integrated back into the primary checkout before they are treated as the current local baseline.
- `docs/planning/ACTIVE_WORKSTREAMS.md` records this current coordination model and retains the older many-worktree map only as historical archive.
- Never reset, rebase, discard, or overwrite another session's uncommitted work. When the checkout is already dirty, understand the existing diff before changing shared files.
- Keep commits focused by task. Shared navigation, shell, global design-system files, source-authority code, reporting contracts, migrations and canonical documentation require cross-feature review.
- Multiple sessions do not have permission to invent multiple UI systems. Reuse the current V3 primitives and established density/hierarchy while preserving each source's business behavior.
- Browser review remains required for UI acceptance; build success alone is not visual approval. Use the configured local Client URL for the active checkout rather than changing committed launch settings just to allocate ports.
- No production deployment, source cutover, database migration, APK publication, or merge to `master` is implied by a local implementation task unless the task explicitly authorizes it.

- Use file editors, not scripted in-place edits: PowerShell string replacement has corrupted tracked files here
  (stripped a UTF-8 BOM, mangled `₱`/`—`/`…`, produced invalid YAML).
- Stage by explicit path and check `git diff --cached --name-only`. Never stage `.env`, keystores, database
  dumps, APKs or `artifacts/`.
- A push to `master` deploys to production (~10–13 min). Verify afterwards: deployed image tag equals `HEAD`,
  API `/health` 200, portal `/login` 200, scoped CSS bundle brace-balanced.
- Money, reporting, tenancy and auth fixes need a test that fails before the fix — prove it by reintroducing the defect once.
- Collector-app changes need a signed RELEASE APK before collectors see them. `ApplicationVersion` must increase; use the
  manual publish workflow and verify that the release asset and API-advertised version describe the same build.
