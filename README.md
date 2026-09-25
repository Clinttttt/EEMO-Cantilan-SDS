# StallTrack — EEMO Revenue Collection System

Multi-tenant revenue collection platform for LGU-managed economic enterprises. Each municipality is a tenant
with its own facilities, fee rates, users, branding and data, isolated inside one database and one deployment.

**Reference tenant:** Economic Enterprise & Management Office (EEMO), Municipality of Cantilan, Surigao del Sur.
**Status:** in production — web portal, API and Android collector app are live.

---

## What is here

| Project | Target | Role |
|---------|--------|------|
| `EEMOCantilanSDS.Domain` | net9.0 | Entities, enums, constants, `Result<T>`, `PhilippineTime`. No dependencies |
| `EEMOCantilanSDS.Application` | net9.0 | CQRS handlers (MediatR), DTOs, FluentValidation validators, interfaces |
| `EEMOCantilanSDS.Infrastructure` | net9.0 | EF Core + Npgsql, repositories, tenancy, caching, security, fee rates |
| `EEMOCantilanSDS.HttpClients` | net9.0 | Typed API clients shared by the portal and the collector app |
| `EEMOCantilanSDS.Api` | net9.0 | ASP.NET Core Web API — thin controllers, JWT auth, SignalR hubs |
| `EEMOCantilanSDS.Client` | net10.0 | Blazor Server portal, plus the public payor portal |
| `EEMOCantilanSDS.Mobile` | net10.0-android | .NET MAUI collector app (offline-tolerant field collection) |
| `EEMOCantilanSDS.Mobile.Core` | net9.0 | Platform-agnostic mobile services and models |
| `EEMOCantilanSDS.Testing` | net9.0 | xUnit unit/repository tests, plus one opt-in PostgreSQL tenant-restore verification |
| `EEMOCantilanSDS.ComponentTests` | net10.0 | bUnit render tests |
| `EEMOCantilanSDS.IntegrationTests` | net9.0 | PostgreSQL/Testcontainers integration tests |

Also in the root: `.github/workflows/` (CI, production deploy, signed-APK publish, backup, restore),
`mobile-app-site/` (the static site behind the collector-app download and bind links — **written to by
`publish-apk.yml`**), `scripts/`, `tools/postgres-dev-mcp/` (local development MCP server),
`docker-compose.yml`.

The Angular operator console (LGU assessment → validation → activation) lives in a **separate repository**.

---

## Getting started

Requirements: .NET SDK with the `net9.0` and `net10.0` targets, the MAUI Android workload for the collector
app, PostgreSQL, Node (for the portal's CSS tooling).

```bash
# Restore and build everything
dotnet build EEMOCantilanSDS.slnx

# Run the API and the portal (separate terminals)
cd EEMOCantilanSDS.Api    && dotnet run
cd EEMOCantilanSDS.Client && dotnet run

# Collector app (Android)
dotnet build EEMOCantilanSDS.Mobile/EEMOCantilanSDS.Mobile.csproj -f net10.0-android
```

Configuration comes from `appsettings.json` / `appsettings.Development.json`, with `.env` (see `.env.example`)
for Docker Compose. **Secrets never live in source** — connection string, JWT signing key, `Encryption:Key`,
PayMongo and Firebase credentials all come from environment configuration.

### Tests

Run the three normal suites **separately** — combining them causes a bUnit timing flake. The integration suite
requires Docker/Testcontainers.

```bash
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj
```

`TenantRestoreRoundTripTests` is a separate opt-in local PostgreSQL verification inside the unit-test project,
enabled with `KIRO_PG_RESTORE_TEST`; it is not part of the normal CI integration suite.

### Migrations

Additive only — production applies migrations at startup, so destructive DDL would break a running tenant.

```bash
dotnet ef migrations add {Name} --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api
dotnet ef migrations script --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api
```

---

## Deployment

A push to `master` builds both container images (tagged with the commit SHA), pushes them to Azure Container
Registry, and updates the two Azure Web App sitecontainers — portal and API. Roughly 10–13 minutes.
Documentation-only paths (`docs/**`, `.agents/**`, `README.md`, `AGENTS.md`, `CONTRIBUTING.md`) do not trigger it.

Verify rather than trust: the deployed image tag equals `HEAD`, API `/health` returns 200, portal `/login`
returns 200, and the scoped CSS bundle is brace-balanced. Collector-app changes additionally need a RELEASE APK
rebuild before collectors see them.

---

## Conventions and rules

The permanent, tool-neutral project knowledge base lives under `docs/`. Start with:

1. `docs/README.md` — documentation map, authority order and conflict handling
2. `docs/architecture/ARCHITECTURE_RULES.md` — implementation boundaries
3. `docs/architecture/APPLICATION_PATTERNS.md` — established code shapes
4. `docs/architecture/SYSTEM_ARCHITECTURE.md` — architectural rationale
5. `docs/business/EEMO_BUSINESS_RULES.md` — accepted business semantics
6. `docs/business/REVENUE_ARCHITECTURE.md` — approved target EEMO revenue architecture and migration direction

`AGENTS.md` is the concise root entry point for coding agents. `.agents/skills/` contains repeatable StallTrack-specific review and operational procedures. Skills do not override canonical documentation. Explicit current rulings, intended documentation, implementation/tests/workflows and verified production behaviour can disagree; surface the contradiction and determine which source is stale before changing behaviour.

Three rules worth stating on the front page:

- **Cantilan is the accuracy baseline.** A change made for another municipality must never move a Cantilan figure.
- **Rates are data.** Resolve through `IFeeRateResolver` as of a date; the `FeeRates` constants are a fallback.
- **Uniqueness is per tenant.** A username, email, stall number or OR number is unique within a municipality,
  not globally.
