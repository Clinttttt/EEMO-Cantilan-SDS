# AGENTS.md — entry point for coding agents

StallTrack — a multi-tenant revenue collection platform for LGU-managed economic enterprises. In production.
Reference tenant: EEMO, Municipality of Cantilan, Surigao del Sur.

## Read these before changing code, in this order

1. `.kiro/knowledge/arch-rules.md` — what is allowed and what is forbidden. **Source of truth.**
2. `.kiro/knowledge/patterns.md` — the code shapes to copy.
3. `.kiro/knowledge/ARCHITECTURE_DOCUMENTATION.md` — why the design is what it is.
4. `.kiro/knowledge/EEMO_Complete_Documentation.md` — the business truth.

Short versions of the same material live in `.kiro/steering/` (`product.md`, `tech.md`, `structure.md`,
`CONTEXT.md`). When files disagree, the order above decides.

## The five rules that break the most things

- **Cantilan is the accuracy baseline.** A change made for another municipality must never move a Cantilan figure.
- **Rates are data.** Resolve through `IFeeRateResolver` as of a date; `FeeRates` constants are a fallback only.
  A stall's daily fee comes from `Stall.ResolveDailyFee(resolvedRate)`, and a daily-billed facility's monthly
  figure is `ResolveDailyFee(...) * DomainRules.DailyBilledMonthDays` — never the stored `MonthlyRate`.
- **Uniqueness is per tenant.** A username, email, stall number or OR number is unique within a municipality,
  not globally, so a cross-tenant lookup must handle multiple matches.
- **Scoped CSS must stay brace-balanced.** One unbalanced brace in a `.razor.css` corrupts the whole bundle and
  breaks every page; neither `dotnet build` nor `/health` catches it.
- **Prerendering runs `OnInitializedAsync` twice.** Never consume a one-time token there.

## Commands

```bash
dotnet build EEMOCantilanSDS.slnx

# Run the suites SEPARATELY — together they cause a bUnit timing flake.
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj
```

Migrations are **additive only** (production applies them at startup).

## Working agreements

- Use file editors, not scripted in-place edits: PowerShell string replacement has corrupted tracked files here
  (stripped a UTF-8 BOM, mangled `₱`/`—`/`…`, produced invalid YAML).
- Stage by explicit path and check `git diff --cached --name-only`. Never stage `.env`, keystores, database
  dumps, APKs or `artifacts/`.
- A push to `master` deploys to production (~10–13 min). Verify afterwards: deployed image tag equals `HEAD`,
  API `/health` 200, portal `/login` 200, scoped CSS bundle brace-balanced.
- Money, tenancy and auth fixes need a test that fails before the fix — prove it by reintroducing the defect once.
- Collector-app changes need a RELEASE APK rebuild before collectors see them.
