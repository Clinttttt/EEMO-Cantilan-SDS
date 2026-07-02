# Cantilan Production Hardening Runbook

**Goal:** the operational checklist required for **Production Checkpoint A (Cantilan live)**, on top of
Phases 0–2. Each item lists the current state, the concrete action, its risk, and how to verify it.

> Principle: items that change live behavior (HSTS, rate limiting, CSP) are documented here rather than
> auto-applied, because they must be tested against the real Blazor Server (SignalR) + API before rollout.

---

## 0. Already in place (verified in code)

- ✅ **Liveness endpoint** — `GET /health` (anonymous) returns `{ status: "ok" }`.
- ✅ **Readiness endpoint** — `GET /health/ready` (anonymous) returns 200 when the database is reachable, 503 otherwise. *(added in this pass)*
- ✅ **Strict cookie policy** — `SameSite=Strict`, `HttpOnly=Always`, `Secure=Always` (`Program.cs`).
- ✅ **HTTPS redirect** — enabled outside Development.
- ✅ **JWT secret externalized** — `Jwt:Key` is empty in `appsettings.json`; the real key is supplied via environment/user-secrets at runtime.
- ✅ **CI gate** — `.github/workflows/ci.yml` builds API + portal (Release) and runs the full test suite on push/PR.
- ✅ **Account lockout** — 5 failed logins → 15-minute lock (domain rule).
- ✅ **Refresh tokens hashed at rest**, single-source, revoked on logout.

---

## 1. Secrets management

**State:** `Jwt:Key` is externalized. Confirm the same for the database connection string and PayMongo keys.

**Actions**
1. Verify `appsettings.json` and `appsettings.Development.json` contain **no production** secrets — only empty values or local-dev placeholders.
2. Supply production secrets via environment variables (ASP.NET binds them automatically):
   - `Jwt__Key`, `ConnectionStrings__DefaultConnection`, and any `PayMongo__*` keys.
   - Never commit production values; keep them in the host's secret store (e.g. platform env vars / a vault).
3. Confirm `.gitignore` excludes any `appsettings.*.local.json` used for local secrets.

**Risk:** low (config only). **Verify:** app boots with secrets from env; `git grep` finds no live key/connection string.

---

## 2. Transport security (HTTPS / HSTS)

**State:** HTTPS redirect on in production; HSTS not yet enabled.

**Actions**
1. Ensure a valid TLS certificate terminates at the host/reverse proxy for `*.stalltrack.site`.
2. Add HSTS in the API and portal **only after** confirming all traffic is HTTPS:
   ```csharp
   if (!app.Environment.IsDevelopment()) { app.UseHsts(); }
   ```
**Risk:** medium — HSTS pins HTTPS in browsers for its max-age; enable only when certs are stable.
**Verify:** `Strict-Transport-Security` header present in prod responses; site loads over HTTPS with no mixed content.

---

## 3. Security headers

**State:** none set.

**Actions** — add a small middleware (API first; portal needs CSP care because of Blazor/SignalR):
- `X-Content-Type-Options: nosniff`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `X-Frame-Options: DENY` (portal is not intended to be framed)
- CSP: **draft and test against the Blazor Server circuit separately** — an over-strict CSP breaks SignalR and inline styles. Do not ship a blind CSP.

**Risk:** low for the first three; **high** for CSP (test in staging). **Verify:** headers present; portal + SignalR still work.

---

## 4. Rate limiting (auth endpoints)

**State:** account lockout exists; no request rate limiter.

**Actions** — add ASP.NET's rate limiter scoped to auth endpoints (login, refresh, setup), with a lenient limit (e.g. fixed window, 10/min/IP) so legitimate use and demos are unaffected.

**Risk:** medium — a too-tight limit can block real users/demos. Start lenient, monitor, tighten later.
**Verify:** rapid-fire login attempts get 429 after the threshold; normal login unaffected.

---

## 5. Database backups + restore

**State:** external to the app (hosting/DB provider).

**Actions**
1. Enable automated PostgreSQL backups (daily + point-in-time if available).
2. **Perform a test restore** into a scratch database and confirm data integrity — a backup is only real once a restore is proven.
3. Document the restore procedure and RPO/RTO.

**Risk:** none to the app; critical for government data. **Verify:** a restored copy opens and reconciles.

---

## 6. Migrations deployment

**State:** `Database:ApplyMigrationsAtStartup` toggles `MigrateAsync()` at boot.

**Actions**
1. Decide the production strategy: auto-migrate at startup (simple) **or** a gated `dotnet ef database update` step in the deploy pipeline (safer for review).
2. Always back up before applying a migration in production.

**Risk:** medium (schema changes). **Verify:** the pending `AddMunicipalityRegistry` (additive) applies cleanly to a staging copy first.

---

## 7. Observability

**State:** exception-handling middleware present; no structured logging/monitoring configured.

**Actions**
1. Configure structured logging (Serilog or built-in) with request correlation.
2. Wire error monitoring (e.g. Sentry/App Insights) for unhandled exceptions.
3. Point uptime monitoring at `/health` (liveness) and `/health/ready` (readiness).

**Risk:** low (additive). **Verify:** a forced error surfaces in the monitor; probes are green.

---

## Checkpoint A sign-off

- [ ] Secrets externalized and verified absent from source
- [ ] TLS valid; HTTPS enforced; HSTS decided
- [ ] Security headers set (CSP tested separately)
- [ ] Auth rate limiting live (lenient)
- [ ] Backups automated **and a restore tested**
- [ ] Migration-deploy strategy documented; `AddMunicipalityRegistry` applied to staging
- [ ] Logging + error monitoring + uptime probes live
- [ ] Cantilan verified end-to-end in production (login, record payment, daily collection, reports, mobile)

---

## Execution log

### 2026-07-02 — repo-side verification (what can be confirmed from source)

- ✅ **No secrets committed to source.** `EEMOCantilanSDS.Api/appsettings.json` has an **empty** `ConnectionStrings:DefaultConnection` and empty `Jwt` values. `**/appsettings.Development.json` and `/.env` are git-ignored; the only tracked `appsettings.Development.json` (Client) contains **logging config only** — no secrets. The API's dev settings file is **not** tracked.
- ✅ **Health probes wired** — `/health` (liveness) and `/health/ready` (DB readiness) present in `Program.cs`; build green.
- ✅ **Fixed (2026-07-02):** moved `Jwt` and `OnlinePayments` to **top-level** in `EEMOCantilanSDS.Api/appsettings.json` (where the code reads `Jwt:Key/Issuer/Audience` and `OnlinePayments:PortalBaseUrl`). Values unchanged (empty `Jwt`; localhost `OnlinePayments` dev default; real values still come from env/`appsettings.Development.json`). The mistyped **`Developmemt_Cors`** key is deliberately **kept as-is** because the CORS policy reads it with that exact spelling in `Api/DependencyInjection.cs`; correcting the spelling is a separate change that must update code + config together and be verified against dev CORS at runtime. Build + 441/441 tests green.

**Still pending (hosting environment — team action):** items §1 (confirm real secrets set in host env), §2 (TLS/HSTS), §3 (headers/CSP), §4 (rate limiting), §5 (backups + tested restore), §6 (migration-deploy strategy), §7 (logging/monitoring). Per guidance, HSTS/CSP/rate limiting are **staged** — apply only via an isolated change verified against Blazor Server + SignalR in staging.
