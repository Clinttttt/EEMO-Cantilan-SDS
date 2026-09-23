# Read this first

The rules and approved target architecture for this repository live in five documents under `.kiro/knowledge/`. Read them before
generating, modifying, reviewing or refactoring code.

| Order | File | Purpose |
|-------|------|---------|
| 1 | `.kiro/knowledge/arch-rules.md` | **Implementation boundaries.** Layering, DI, CQRS, multi-tenancy, money rules, EF Core, auth, Blazor, testing, and how to work in this repo. |
| 2 | `.kiro/knowledge/patterns.md` | **Shapes to copy** — command, query with caching, repository, controller, typed API client, Blazor page, shared component, guard, tests, naming. |
| 3 | `.kiro/knowledge/ARCHITECTURE_DOCUMENTATION.md` | **Why** the architecture is the way it is, including the trade-offs behind multi-tenancy, `Result<T>`, rate resolution and the two-factor design. |
| 4 | `.kiro/knowledge/EEMO_Complete_Documentation.md` | **Business truth** — facilities, billing models, delinquency, roles, onboarding, reporting invariants, vocabulary. |
| 5 | `.kiro/knowledge/EEMO_REVENUE_ARCHITECTURE.md` | **Approved target** — broader revenue semantics, domain boundaries, compatibility, migration phases, decision gates and UI rules. It describes intended evolution, not current runtime behavior. |

Use `arch-rules.md` for implementation boundaries, accepted business documents and current rulings for intended semantics, and the new architecture document for the approved target expansion.
Current code, migrations, tests, workflows and verified production behaviour are evidence of current behaviour, not an
automatic override of intended semantics. When sources disagree, surface the contradiction and determine which is stale.
The `.kiro/steering/` files (`product.md`, `tech.md`, `structure.md`) are short navigation aids. `AGENTS.md` in the
repository root is the concise Codex entry point and states the full authority rule.

Follow the existing patterns instead of inventing new ones. Consistency is preferred over creativity.

---

## Before writing code

1. Read the file you are about to change, and the nearest existing example of the same kind.
2. Reuse the abstractions that are already there (`Result<T>`, repositories, `IFeeRateResolver`, the guards).
3. Ask what the change does to **another municipality** and to **Cantilan's figures**. Cantilan is the baseline.
4. Money, tenancy and auth changes need a test that fails before the fix.

## When reviewing existing code

Look for: layer violations, an unscoped tenant query, `FeeRates` read directly instead of resolved, a stored
`MonthlyRate` used for a daily-billed facility, a one-time token consumed in `OnInitializedAsync`, an
`[Authorize]` endpoint on the anonymous HTTP client, N+1 reads, a mutation that bypasses `SaveChangesAsync`,
missing validation, missing tests.

## When refactoring

Reduce duplication and preserve behaviour. Do not introduce abstractions the code does not need, and do not
rewrite working code without a reason you can state.

## Verification is part of the work

Build, run the unit, component and PostgreSQL/Testcontainers integration suites in separate commands, and after a push
to `master` verify the deployment (image tag
equals HEAD, API `/health` 200, portal `/login` 200, scoped CSS bundle brace-balanced). Mobile changes need a
RELEASE APK rebuild before collectors see them.

If uncertain, consult the four core rule documents and the target architecture rather than assuming.
