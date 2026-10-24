# V14 post-release implementation progress — 2026-10-24

Working evidence, not release or policy authorization. This continues the complete V14 ten-issue handoff in `C:\dev\stalltrack\release-evidence\V14_POSTRELEASE_TEN_ISSUE_CODEX_HANDOFF_20261024.md` and the accompanying Carrascal branding audit.

## Isolation and boundaries

- Worktree: `C:\dev\stalltrack\codex-v14-postrelease-20261024`.
- Branch: `fix/v14-branding-weighing-20261024`; refreshed starting `origin/master`: `f5acf1bbf94b031949fa9ba4a6d26de3517adc40`.
- V13 is already merged/deployed per the V14 handoff. Its old branch is not being merged or replayed. The old 40-file dirty Fish/Meat checkout and all other worktrees are untouched.
- No production financial writes, migrations, tenant/policy activation, push, merge or deployment are authorized by this task.
- Reuse the existing sidebar and two Fish/Meat cards; no replacement UI system or parallel financial calculations.

## Progress by acceptance issue

| Issue | Current evidence/status | Next verification |
|---|---|---|
| 1. Seal | Fixed method-scoped authorization and authenticated municipality-ID lookup, failed-read recovery, stale-response protection and authoritative save refresh/notification. Existing sidebar/account chip update without a reload; neutral/Cantilan behavior retained. Component and real PostgreSQL API tests pass. | Live authenticated Carrascal rendering and image-network acceptance remain unverified; no seal re-upload or data rewrite. |
| 2. Registration requests | Operator-provided production read-only output confirms `stalltrack_demo` contains migration `20261020133853_AddFishMeatRegistrationRequestsWorkingCopy20261020` and public request table. No activation/municipality allow-list exists in the checked environment or appsettings files. Current source defaults requests off. No flags changed. | Activation gate identified; full mapped-column/query readiness and deployed binary identity remain unverified. Any enablement requires explicit municipality authorization and reviewed release configuration. |
| 3. Legacy/SRC | Existing source-authority map and V13 reconciliation safeguards retained. | Trace current online route read-only; no invented SRC or new canonical writer without separate review/authorization. |
| 4. Alleged missing weighing | Registered source and historical NPM remain distinct. | Reconcile actual authorized tenant/date/SRC evidence when available; do not infer missing cash from empty NPM figures. |
| 5. Follow-up tabs | V13 implementation retained. Relevant Head regressions and all 3 MonitoringTabs cases pass. | Rendered desktop/narrow QA unavailable; no speculative source change. |
| 6. Revenue chart | V13 implementation retained; RevenueTrendPanel regressions pass in the 129-case run. | Actual rendered sizing/responsiveness unverified; no speculative source change. |
| 7. Audit identities | V13 identity-safe resolution retained; tenant/unknown/System audit regressions pass in the 32-case unit run. | No invented historical initiating actors; live reported event identity remains unverified. |
| 8. Mobile identifier | V13 scoped fix retained; all 168 Mobile components and Windows Release build pass. Handoff reports signed 1.1.18/code20. | No device/rendered acceptance or APK publication in this task. |
| 9. Remittance | V13 modal and durable evidence retained; Head and PostgreSQL grouping regressions pass. | Rendered focus/evidence acceptance unavailable; submitted money unchanged. |
| 10. Duplicate weighing UI | Removed the standalone registered-vendor and historical summary panels. Registered rows and historical evidence now live in the existing Fish/Meat cards and report sections; original card proportions, hero, configuration and month/search controls retained. SRC links, kilos, frozen rates, net amounts and correction status remain distinct; no repeated registered header totals. | Final 129-case Head run passes, including both/one/neither configured, empty/no-NPM catalogs, search, frozen/net evidence and CSV. Rendered desktop/narrow/print acceptance remains unavailable. |

## Runtime access and limitations

- Computer-use inventory currently has no enabled applications/browsers; attempted in-app browser returned `Browser is not available: iab`. No rendered or device acceptance is claimed.
- Default AWS session is expired. The existing `stalltrack-audit` profile authenticates to account `934846852029` as root; this is not a restricted audit role. Only read-only operations were performed. Production was verified as Lightsail instance `stalltrack-demo-vm-20261017` in `ap-southeast-1`, address `54.169.168.202`; EC2 inventory alone was insufficient.
- Direct SSH with short-lived Lightsail credentials, strict host-key checking and a read-only command timed out. No remote command ran; temporary credentials were removed. The existing SSH allow-list includes the runner and Lightsail browser SSH. No firewall or credentials were changed. No SSM managed host is available. Production schema, activation flags and reported collection rows remain unverified.
- The user requested a browser login link. Opening it automatically was blocked by policy; the Lightsail instance Connect URL was supplied directly. Browser authentication is not terminal authorization.
- No credentials or token values are written to this document or tool output.

### Operator-assisted production evidence

The user ran the supplied read-only commands through Lightsail browser SSH. The API container is `stalltrack-presentation-demo-api-1` (image ID shown as `64977481937c`), portal `stalltrack-presentation-demo-portal-1` (`0905b577234f`), database `stalltrack-presentation-demo-db-1` (`postgres:18-alpine`). These short image IDs alone do not establish deployed commit provenance.

- A `BEGIN READ ONLY` transaction returned database `stalltrack_demo`, the exact request migration above, and `public."FishMeatRegistrationRequests"`. This confirms the table/migration, not every mapped column or runtime query permission.
- Allow-listed environment inspection returned no `StallTrack__FishMeatRequests__Enabled`, `MunicipalityIds` or working-copy activation override.
- The API reported environment `Production`; `/appsettings` paths derived from the container working directory were read and JSON-parsed without exposing unrelated settings. Both `appsettings.json` and `appsettings.Production.json` reported request Enabled, MunicipalityIds and WorkingCopy Enabled as not set.
- `FishMeatRequestActivation.AddFishMeatRequestActivation` returns without installing request options when `StallTrack:FishMeatRequests:Enabled` is absent/false; the default options have `RequestsInstalled = false`. The observed normal configuration therefore leaves the nonfinancial request feature disabled. No migration, policy or runtime changes were performed. No conclusion is claimed about uninspected custom configuration providers or exact deployed assembly content.

## Validation and handoff

### Focused weighing changes

`WeightAndMeasure.razor` and its existing report reuse their Fish/Meat sections; local render fragments avoid duplicating the row markup by vendor type. Search applies to vendor/SRC and the existing historical occupant/stall rows; the hero continues to describe the full selected month. Frozen historical amounts are never recomputed from current rates, and the official Monthly Income component/classification is unchanged. Collection links use the existing `/collections/activity?src=` route for full correction evidence.

The read-only CSV now retains the existing registered rows and also exports the historical frozen Fish/Meat totals and unresolved Fish kilos already displayed on screen. No cash is created or combined by the export. A failed existing NPM report blocks printing/export rather than presenting an incomplete report as complete.

`FacilityState.HasDeclaredFacility` checks actual catalog records without changing the shared legacy `All` presentation fallback. These pages no longer fetch/show NPM history merely because an empty catalog falls back to all facility codes. Long historical tables use normal page height rather than a second constrained vertical scroller. No production data or money arithmetic changed.

The first weighing run had 15 passes/3 failures: two exposed the empty-catalog fallback; one exposed a misplaced Razor `else` during the move. Both were corrected before the broader component run (129 passed, 0 failed/skipped). The final cleanup retains original card proportions and removes duplicated registered header figures per the user's clarification.

Additional green results: 32 focused unit cases; 27 PostgreSQL regression cases (46 seconds), including branding, request lifecycle/default-off/tenant/concurrency safeguards, independent weighing frozen reporting, rate versions and remittance grouping; all 168 Mobile component cases (7 seconds). Zero failed/skipped in these runs. Counts overlap previous focused runs and must not be summed as unique cases. All 208 Client scoped CSS source files passed the brace scan; whitespace diff check passed. Exact commands and final build results will be recorded below.

Branding regression tests reproduced the defects before source changes: shared-state suite 7 cases (5 passed, 2 failed), focused profile/sidebar/recovery cases 5 (1 passed, 4 failed), real authenticated PostgreSQL API cases 6 (all failed). These overlap and are not summed as unique tests. Fixes now use authenticated municipality ID, retry failed reads, prevent stale reads replacing newer state, notify the existing sidebar/account chip, and refresh authoritative branding after Office Profile saves. Green verification is in progress.

Focused component green run: `dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj -c Release --filter "FullyQualifiedName~BrandingStatePrerenderTests|FullyQualifiedName~SharedBrandingUpdateRefreshes|FullyQualifiedName~OfficeProfileBrandingTests" --logger "console;verbosity=minimal"` passed all 11 cases (0 failed, 0 skipped). Existing project warnings remain. `git diff --check` passed.

Real authenticated PostgreSQL branding API green run: `dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~MunicipalityBrandingApiTests" --logger "console;verbosity=minimal"` passed all 6 cases (0 failed, 0 skipped; 22 seconds test duration). This exercises anonymous rejection, public branding/seal availability, authenticated own-tenant identity across roles, missing/unknown tenant rejection, Head profile save and other-tenant/financial-record preservation on disposable PostgreSQL. It is not a production or rendered-browser verification.

Authorization mutation check: the original controller-level `AllowAnonymous` was temporarily reintroduced in this isolated worktree only. `AnonymousCurrentBrandingIsUnauthorized_WhilePublicRegistryBrandingAndSealRemainAvailable` failed as expected (expected 401, actual 403 with the separately fixed fail-closed handler). The attribute was removed again automatically in a finally block. All 6 branding API cases then passed again (21 seconds). No unsafe source change was retained or deployed.

### Final commands and results

Each suite ran separately. Logs are local `%TEMP%` evidence, not production observations.

```powershell
dotnet test EEMOCantilanSDS.Testing/EEMOCantilanSDS.UnitTest.csproj -c Release --filter "FullyQualifiedName~GetCurrentMunicipalityBrandingTests|FullyQualifiedName~FishMeatRequestActivationTests|FullyQualifiedName~AuditTrailScopeTests|FullyQualifiedName~IndependentWeighingRateTests|FullyQualifiedName~FishWeighingEvidenceTests" --logger "console;verbosity=minimal"
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj -c Release --no-restore --filter "FullyQualifiedName~FishMeatWorkspaceTests|FullyQualifiedName~Branding|FullyQualifiedName~SidebarNavigationTests|FullyQualifiedName~OfficeProfileBrandingTests|FullyQualifiedName~ContractsMonitoringTests|FullyQualifiedName~FollowUpQueueCompositionTests|FullyQualifiedName~RevenueTrendPanelTests|FullyQualifiedName~RemittanceWorkspaceTests" --logger "console;verbosity=minimal"
dotnet test EEMOCantilanSDS.IntegrationTests/EEMOCantilanSDS.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~MunicipalityBrandingApiTests|FullyQualifiedName~Independent_fish_only|FullyQualifiedName~Vendor_request|FullyQualifiedName~Vendor_requests|FullyQualifiedName~Vendor_registration_policy|FullyQualifiedName~WeighingRateVersionPostgresTests|FullyQualifiedName~RemittanceGroupingTests|FullyQualifiedName~NpmWeighingShadowReconciliationTests" --logger "console;verbosity=minimal"
dotnet test EEMOCantilanSDS.Mobile.ComponentTests/EEMOCantilanSDS.Mobile.ComponentTests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test EEMOCantilanSDS.ComponentTests/EEMOCantilanSDS.ComponentTests.csproj -c Release --no-build --filter "FullyQualifiedName~MonitoringTabs" --logger "console;verbosity=minimal"
dotnet build EEMOCantilanSDS.Api/EEMOCantilanSDS.Api.csproj -c Release --no-restore
dotnet build EEMOCantilanSDS.Client/EEMOCantilanSDS.Client.csproj -c Release --no-restore
dotnet build EEMOCantilanSDS.Mobile/EEMOCantilanSDS.Mobile.csproj -c Release -f net10.0-windows10.0.19041.0
```

- Unit: 32 passed, 0 failed/skipped (`v14-unit.log`).
- Final Head components after cleanup: 129 passed, 0 failed/skipped, 3 seconds (`v14-final-components.log`).
- PostgreSQL regressions: 27 passed, 0 failed/skipped (`v14-postgres-regression.log`).
- Mobile components: 168 passed, 0 failed/skipped (`v14-mobile-components.log`). This is not Windows/Android device interaction.
- Monitoring tabs: 3 passed, 0 failed/skipped in a separate final run.
- Client Release: 0 errors, 94 warnings (`v14-client-build.log`). Existing compiler/analyzer warnings and the test-project AngleSharp advisory were not addressed by this focused change.
- API Release: 0 errors, 0 warnings (`v14-api-build.log`).
- Windows Mobile Release: 0 errors, 18 warnings (`v14-windows-mobile-build.log`). Existing AndroidX dependency constraints and Mobile compiler warnings remain; no Mobile source or package changes were made.
- Source CSS 208/208 brace-balanced; both generated Client/Mobile scoped bundles brace-balanced; `git diff --check` passed. Final diff was inspected for tenant scope, source separation, SRC navigation, history preservation and unrelated edits. No new migration, package, financial writer or municipality policy was added.

### Remaining acceptance/release gates

- No browser/application surface is enabled in computer-use inventory, including after a fresh check. Authenticated live Carrascal seal/network/prerender acceptance, desktop/narrow overflow and print/device acceptance are unverified.
- No actual reported missing weighing SRC/date was supplied; the user instead reiterated the in-place Fish/Meat presentation requirement, which is implemented. A specific live cash discrepancy is not established or claimed resolved.
- Request feature activation requires an operator-approved exact municipality allow-list, deployed schema/query readiness and reviewed configuration/restart authorization. Keep mandatory-registration collection policy independent. No activation is performed by these changes.
- Canonical online settlement remains a separate financial design/authorization decision. Genuine legacy payments keep their evidence and missing SRC; existing V13 captured-provider reconciliation is retained without a new writer.
- No push, commit, merge, deployment, migration, tenant activation or production financial mutation was performed. Changes remain local/uncommitted on the V14 branch based on `f5acf1bb`.
