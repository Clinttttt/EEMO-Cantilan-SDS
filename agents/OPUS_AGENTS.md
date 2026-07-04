# OPUS_AGENTS.md — Agent context & work log (EEMO Cantilan SDS / StallTrack)

> Persistent context for the "Opus" agent working on this repository. Read this first,
> then the steering files under `.kiro/steering/` and `.amazonq/context/knowledge/`.

## 1. What this system is
Government revenue-collection platform for EEMO, Municipality of Cantilan (Surigao del Sur).
Clean Architecture .NET solution (`.slnx`), Blazor Server admin portal + ASP.NET Core API +
.NET MAUI mobile, PostgreSQL 18 (Azure Flexible Server), deployed to Azure App Service (F1).
8 facilities (NPM, TCC, NCC, BBQ, ICE, SLH, TRM, TPM). Angular landing/admin migration lives
in a separate repo (`stalltrack-platform`).

## 2. Non-negotiable guardrails
- **Financial accuracy is #1.** Never change computed values, billing, migrations, or the
  Phase-0 financial GOLDEN tests. Presentation/display changes must not touch money math.
- **Commit only when explicitly asked.** Pushing to `master` = production deploy
  (`.github/workflows/deploy-production.yml`: tests → docker → ACR → Azure → health; migrations
  auto-apply in prod). `.github/**` changes do NOT trigger a deploy.
- **Verify every change:** solution build + full test suite must stay green (currently **462/462**).
- Build/test commands (Windows PowerShell):
  - Solution: `dotnet build (Get-ChildItem *.slnx).FullName --nologo`
  - Client: `dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj --nologo /p:UseAppHost=false`
  - Tests: `dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj --configuration Release --nologo`
  - Kill locking `dotnet.exe`/`EEMOCantilanSDS.*.exe` by PID before building.
- Key domain rule: use `Contract.IsCollectableOn` / `IsExpired` / `OverlapsPeriod` (term-aware),
  **never `IsActive` alone**, for collection/report eligibility (`IsActive` ignores lapsed terms).
- Reusable helper for UI/DTO expiry: `StallContractStatus.IsCurrentVendor(stallDto)`.
- Recorder attribution pattern (collector else admin/Head from audit actor):
  `DashboardRepository.ResolveRecorder` — mirror it, don't reinvent.

## 3. Concurrency note (IMPORTANT)
Another agent ("Codex") and the user edit this repo in parallel. As of this log, the following
files carry THEIR in-flight edits and must NOT be modified/committed by us without coordination:
`Report.razor`, `Report.razor.css`, `Command/Payments/RecordPayment/RecordPaymentCommandHandler.cs`,
`Command/DailyCollections/RecordDailyCollection/RecordDailyCollectionCommandHandler.cs`,
and (observed) `DelinquentStallDto.cs`, `FollowUpComposer.cs`,
`FacilityReportsRepository.Compliance.cs`, plus their tests. Also `MauiProgram.cs` (URL toggle)
stays uncommitted.

## 4. Work completed today (all currently UNCOMMITTED unless noted)

### A. Off-site database backups — SHIPPED to `master`
`.github/workflows/backup.yml` — scheduled (daily 17:00 UTC / 01:00 PH) + on-demand `pg_dump`
(`-Fc` custom format) → 90-day GitHub artifact. Runs under `environment: production` to reuse the
Azure OIDC federated credential. Opens a temporary single-IP firewall rule and always removes it.
- Azure: created least-privilege custom role **"StallTrack PG Firewall Manager"** (firewall-rule
  read/write/delete + server read only) and assigned it to the deploy SP scoped to
  `psql-stalltrack-clint-2026`.
- **Restore was TEST-VERIFIED**: `pg_restore` into a throwaway `postgres:18` container succeeded
  (46 tables, 79 constraints, 32 migrations, all financial data). Backups are genuinely restorable.
- Primary DB safety net remains Azure Postgres **7-day PITR** (independent of the F1 tier).
- ⚠️ A GitHub fine-grained PAT was exposed in a screenshot during setup — **ROTATE IT**
  (regenerate, update `GitHubBackup__Token` on the API App Service, revoke the old one).

### B. In-app "Backups" feature — built, verified, UNCOMMITTED
Head-only (`SuperAdmin`) surface, Clean-Architecture routed (token stays server-side):
- Settings → "Backups & Restore" card → `/settings/backups` page.
- Run backup now (GitHub `workflow_dispatch`), recent runs list, download latest artifact.
- **No in-app restore/upload** by design (destructive; break-glass runbook + PITR instead).
- New: `Application/{Dtos/Backup,Command/Backup,Queries/Backup, Common/Interface/Services/IBackupService.cs,
  Common/Interface/ApiClients/IBackupApiClient.cs}`, `Infrastructure/Services/{GitHubBackupOptions,
  GitHubActionsBackupService}.cs`, `Api/Controllers/BackupController.cs`,
  `HttpClients/ApiClients/BackupApiClient.cs`, `Client/Components/Pages/Menus/Backups.razor(+css)`,
  `Client/wwwroot/js/download.js`.
- Modified: `Api/appsettings.json` (GitHubBackup section, empty Token), `Infrastructure/DependencyInjection.cs`,
  `Client/DependencyInjection.cs`, `Client/Components/App.razor` (download.js), `Settings.razor` (card),
  `.gitignore` (re-include the `Backup` source folders the VS `Backup*/` rule was hiding).
- Requires `GitHubBackup__Token` env var on `stalltrack-api-clint-2026` (already set in prod).

### C. Fixes — verified 462/462, UNCOMMITTED
1. **Sidebar facility counts** exclude expired-contract payors — `FacilityRepository.GetSidebarSummariesAsync`
   now uses term-aware `OverlapsPeriod` (in-memory) instead of `IsActive` alone.
2. **Settings "Facility Collection Rules" card** spacing — `.set-grid { margin-bottom:18px }`.
3. **Follow-up Queue excludes EXPIRED contracts** from the live queue (`GetFollowUpQueueQueryHandler`
   filters `!IsExpired`); they remain in **Past follow-up** (separate history handler). Fixes the
   count(29)/empty-list mismatch at the source.
4. **Collection Manager** (`CollectionExceptions.razor`) excludes expired payors via
   `StallContractStatus.IsCurrentVendor`.
5. **Stall Profile Collection History** now shows WHO recorded a payment — collector else admin/Head
   (from audit actor), via new `RecordedByName` on `PaymentHistoryDto` + `StallCollectionHistoryRowDto`
   and `PaymentRepository` resolution (mirrors `DashboardRepository.ResolveRecorder`; batched). Column
   header "Collector" → "Recorded By". No money values changed.
6. **Past follow-up "Add OR"** opens the same interactive add-OR modal as the live queue
   (`PastFollowUpQueue.razor`), scoped to the selected past period.
7. **Follow-up "Daily receipt · OR" amount** excludes the ₱1/kg fish surcharge (daily fee only),
   consistent with the NPM/Export reports keeping fish fees separate.
- Test update: `GetFollowUpQueueQueryHandlerTests` expired-contract assertion → `DoesNotContain`
  (reflects fix #3). No financial/golden test weakened.

### D. Earlier admin UI polish — UNCOMMITTED
`Sidebar.razor` (Online-Payments awaiting-OR badge), `AuditTrail.razor(+css)` (icon avatar + KPI
toggle-filter), `ExportData.razor` (Save-as-PDF/Print reorder + tooltips).

## 5. Pre-commit review findings (this session)
- ✅ Solution + Client build clean; **462/462** tests pass with all changes combined.
- ✅ Backup token is server-side only (Infrastructure + `appsettings`); never sent to the browser;
  all backup endpoints + page are `[Authorize(Roles="SuperAdmin")]`; no in-app restore.
- ✅ `#3` recorder resolution matches the proven `DashboardRepository` pattern (admin by `Username`,
  safe null/raw-actor fallbacks, no N+1); changes no money math.
- ✅ `FollowUpComposer` contract section intact → expired still render in history after fix #3.
- Minor / non-blocking:
  - Follow-up Queue filter-chip counts (`CountFor`) don't subtract per-browser manually-hidden items;
    rarely relevant now that expired contracts are excluded server-side. Left as-is.
  - `dailyRaw` still projects `FishKilos` though it's now unused after the fish-fee fix (harmless).
  - `appsettings.json` ships `GitHubBackup:Token=""`; prod relies on the env override — backups fail
    gracefully locally without the token (expected).
- **Action required before relying on backups:** rotate the exposed PAT (see §4A).

## 6. How to commit (when the user says go)
Stage ONLY our files (§4 B/C/D + `FacilityRepository.cs`, `Settings.razor.css`, the fix files);
EXCLUDE the §3 concurrency files and `MauiProgram.cs`. Then build + full test verify, and — only if
asked — `git push origin master` and confirm deploy health + image tag.
