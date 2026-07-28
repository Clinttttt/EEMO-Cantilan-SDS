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
| `EEMOCantilanSDS.Testing` | net9.0 | xUnit unit + integration tests |
| `EEMOCantilanSDS.ComponentTests` | net10.0 | bUnit render tests |

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

Run the two suites **separately** — together they cause a bUnit timing flake.

```bash
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj
```

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
Documentation-only paths (`.kiro/**`, `README.md`, `AGENTS.md`) do not trigger it.

Verify rather than trust: the deployed image tag equals `HEAD`, API `/health` returns 200, portal `/login`
returns 200, and the scoped CSS bundle is brace-balanced. Collector-app changes additionally need a RELEASE APK
rebuild before collectors see them.

---

## Conventions and rules

Read these before changing code — they are the source of truth, in this order:

1. `.kiro/knowledge/arch-rules.md` — what is allowed and what is forbidden
2. `.kiro/knowledge/patterns.md` — the code shapes to copy
3. `.kiro/knowledge/ARCHITECTURE_DOCUMENTATION.md` — why the design is what it is
4. `.kiro/knowledge/EEMO_Complete_Documentation.md` — the business truth

Short versions of the same material live in `.kiro/steering/`. `AGENTS.md` is the root entry point for agents
that look there (Codex).

Three rules worth stating on the front page:

- **Cantilan is the accuracy baseline.** A change made for another municipality must never move a Cantilan figure.
- **Rates are data.** Resolve through `IFeeRateResolver` as of a date; the `FeeRates` constants are a fallback.
- **Uniqueness is per tenant.** A username, email, stall number or OR number is unique within a municipality,
  not globally.
