# Verification

## Local Checks

Use the smallest check that covers the change, then widen for shared behavior.

Common commands:

```powershell
dotnet test EEMOCantilanSDS.Testing\EEMOCantilanSDS.UnitTest.csproj --artifacts-path artifacts\verify-test
dotnet build EEMOCantilanSDS.Api\EEMOCantilanSDS.Api.csproj --artifacts-path artifacts\verify-api /p:UseAppHost=false
dotnet build EEMOCantilanSDS.Client\EEMOCantilanSDS.Client.csproj --artifacts-path artifacts\verify-client /p:UseAppHost=false
dotnet build EEMOCantilanSDS.Mobile.Core\EEMOCantilanSDS.Mobile.Core.csproj --artifacts-path artifacts\verify-mobile-core /p:UseAppHost=false
dotnet build EEMOCantilanSDS.Mobile\EEMOCantilanSDS.Mobile.csproj -f net10.0-windows10.0.19041.0 --artifacts-path artifacts\verify-mobile /p:UseAppHost=false
```

The MAUI app targets multiple TFMs. On Windows, a targeted
`net10.0-windows10.0.19041.0` build is the practical local smoke check. Build
Android/iOS/MacCatalyst when the task affects platform glue or release targets.

Clean artifact folders only after resolving absolute paths and confirming they
are inside `C:\dev\EEMOCantilanSDS`.

## Test Expectations

Add or update tests for:

- Report and dashboard calculations
- Payment status, OR handling, and collector attribution
- NPM daily collection behavior
- Monthly exceptions, closures, absent/excused logic
- Online payment settlement/idempotency
- Cache key/region invalidation
- Mobile offline sync mapping, ownership, idempotency, and per-item outcomes
- Validators and auth/role-sensitive flows

## Migration Checks

When domain/config changes require schema changes:

```powershell
dotnet ef migrations add MigrationName --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api
dotnet ef database update --project EEMOCantilanSDS.Infrastructure --startup-project EEMOCantilanSDS.Api
```

Review generated migrations and snapshots. Do not hand-edit the model snapshot.
Do not run database updates against production unless the task is explicitly a
release/deployment task.

## Deployment Support

Production deploys API first, then portal. GitHub Actions/ACR use immutable
image tags. Do not include secrets in logs, screenshots, docs, or final
responses. Mobile app releases are separate from Azure container deployment.

Before release guidance, read:

- `docs/Production-Deployment-Runbook.md`
- `docs/GitHub-Actions-CI-CD-Setup.md`

## Final Self-Review

Before finishing:

- Check `git diff --check`.
- Check `git status --short`.
- Verify no unintended unrelated files were edited.
- Verify new caches are invalidated by every successful write path that affects them.
- Verify UI changes still have loading, empty, error, and success states.
- State exactly what was verified and any residual warnings.
