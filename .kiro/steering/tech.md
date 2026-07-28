# Technology Stack

## Frameworks and runtimes

Mixed on purpose — the presentation apps run ahead of the shared libraries:

| Project | Target |
|---------|--------|
| Domain, Application, Infrastructure, HttpClients, Api, Testing | `net9.0` |
| Client (Blazor Server portal), ComponentTests | `net10.0` |
| Mobile (MAUI collector app) | `net10.0-android` |

- C# with nullable reference types enabled.
- **PostgreSQL** (Npgsql) is the only database.

## Backend

- ASP.NET Core Web API, JWT bearer authentication (custom — no ASP.NET Identity framework; the
  `PasswordHasher<T>` library type is used on its own).
- Entity Framework Core 9 + Npgsql 9.
- MediatR 12 for CQRS, FluentValidation 12 executed by a pipeline behaviour.
- Swashbuckle for API docs; SignalR hubs for live updates.
- `QRCoder` 1.6.0 (pinned, fully managed) for QR images — authenticator enrolment and the collector-app bind link.
- Hand-written RFC 6238 TOTP over the BCL's `HMACSHA1`; secrets sealed with AES-256-GCM via `ICredentialProtector`.
- PayMongo for online payments, with per-LGU credentials.

## Frontend

- **Blazor Server** portal (`@rendermode InteractiveServer`), plus the public payor portal in the same app.
- Custom CSS with design tokens in `app.css`; component-scoped `.razor.css`. **No Tailwind in components**, no
  inline styles.
- Static assets are fingerprinted through `@Assets[...]`, so a deployment cannot serve stale CSS.
- Angular (Nx) operator console lives in a **separate repository** (`stalltrack-platform`, admin.stalltrack.site).

## Testing

- xUnit. Unit and integration tests: `EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj`.
- bUnit 1.40 render tests: `EEMOCantilanSDS.ComponentTests`. Moq for test doubles.
- **Run the two suites in separate commands** — together they cause a bUnit timing flake.

## Common commands

```bash
# Build everything
dotnet build EEMOCantilanSDS.slnx

# Tests — separately, always
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj

# Run locally
cd EEMOCantilanSDS.Api && dotnet run          # API
cd EEMOCantilanSDS.Client && dotnet run       # portal
cd EEMOCantilanSDS.Client && npm run watch    # CSS watch

# Migrations (from the solution root) — additive only
dotnet ef migrations add {Name} --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api
dotnet ef migrations script --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api

# Collector app
dotnet build EEMOCantilanSDS.Mobile/EEMOCantilanSDS.Mobile.csproj -f net10.0-android
```

## Database conventions

Postgres-native types only: `text`, `character varying(n)`, `boolean`, `uuid`,
`timestamp with time zone`, `numeric(18,2)`, `integer`, `jsonb`. Money is always `decimal`.

Production applies migrations at startup (`Database__ApplyMigrationsAtStartup=true`), so **every migration must
be additive** — new nullable columns or new tables, never destructive DDL.

## Deployment

- GitHub Actions on a push to `master`: build → container images tagged with the commit SHA → Azure Container
  Registry → two Azure Web App sitecontainers (portal + API). Roughly 10–13 minutes.
- Workflows: `ci.yml`, `deploy-production.yml`, `publish-apk.yml` (builds the signed APK and publishes it to the
  download site), `backup.yml`, `restore.yml`.
- Documentation-only paths (`.amazonq/**`, `.kiro/**`, `README.md`) do not trigger a deployment.
- `mobile-app-site/` is the static site behind the collector-app download and bind links; `publish-apk.yml`
  writes the APK into it. **Do not delete it.**
- Verify a deployment rather than trusting it: image tag equals `HEAD`, API `/health` 200, portal `/login` 200,
  and the scoped CSS bundle brace-balanced.

## Local configuration

`appsettings.json` + `appsettings.Development.json`; `.env` / `.env.example` for Docker Compose. Secrets
(`Encryption:Key`, JWT signing, connection strings, PayMongo, Firebase) come from environment configuration —
never from source.
