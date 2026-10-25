# ST-11 multi-tenant security and wiring audit

Branch `worktree-agent-aea01d01481cde297`, base HEAD `7ad13d54` (V17). Scope: API controllers, Application handlers/workflows, Infrastructure repositories, SignalR hubs, background services. Angular front ends and the Blazor Client are out of scope.

## Summary

| # | Severity | Finding | Status |
|---|----------|---------|--------|
| 1 | High | `GET api/vendors/registry` had no `[Authorize]`; an anonymous caller resolves to the default (Cantilan) tenant, so the internet could read Cantilan's vendor registry | FIXED a0e88f8d |
| 2 | Medium | A Collector could overwrite the OR number of any payment / slaughter / TPM / TRM record in the tenant (no facility-assignment check on the OR-number handlers) | FIXED df4adf4d |
| 3 | Low | Six sign-in actions were anonymous only by omission (no attribute) | FIXED a0e88f8d (explicit `[AllowAnonymous]`, behaviour unchanged) |
| 4 | Low | Any LGU Head can read platform-wide database stats (`api/database-health`) | OPEN |
| 5 | Low | `GetSystemSettingsQueryHandler` falls back to the default municipality / Cantilan constants if the tenant code does not resolve | OPEN |
| 6 | Low | `SyncRepository.IsOperationProcessedAsync` is global across tenants (comment in `CrossTenantReadsAreNamedTests` claims the municipality is re-applied by hand; it is not) | OPEN |
| 7 | Low | Device-token upsert/remove look across tenants by token only | OPEN |
| 8 | Info | Payor phone number is unique platform-wide by application rule, so issuing a code in LGU B reveals that a number is registered somewhere (in LGU A) | OPEN (design) |
| 9 | Info | `ClaimTenantContext` / `TokenService` fall back to `cantilan-sds` as cache namespace when the municipality code is blank | OPEN (latent) |

No Critical findings. No cross-tenant read or write path was found through the query filter itself.

## (a) Endpoint / authorization table

Mechanically enumerated from `EEMOCantilanSDS.Api/Controllers/**` (about 508 `[Http*]` actions). There is no fallback authorization policy (`AddAuthorization` defines only the `PlatformOperator` policy), so an action with no attribute is anonymous. After this audit `EveryEndpointStatesItsAuthorizationTests` enforces that every action states its audience and pins the anonymous set.

Class-level audience (method-level `[Authorize]` narrows it further where noted):

| Controller | Class-level | Notes |
|---|---|---|
| AdminAuth | mixed | login, console-login, refresh, logout, forgot/reset, verify-email, mfa/verify-login anonymous (rate-limited `auth`); `[Authorize]` for self-service; `mfa/enrolled-accounts`, `mfa/reset-user` SuperAdmin + operator guard in handler |
| Admins, Collectors, MunicipalityProfile, FacilityRates, RevenueClassifications, Audit, DirectFines | SuperAdmin | |
| BusinessPayors, CollectionActivity, CollectionComposer, EcfCollections, OfficeCashTurnovers, Transactions, Facilities, Stalls, Utilities, OrSeries, Settings, Dashboard, Reports, ContractsMonitoring, AccountableForms, AccountLifecycle, CanonicalReports, DirectCollectionReview, FishMeatPayorFollowUp, NpmDailyCollections, Obligations, OfficialReports, OperationSettings, Penalties, Remittances, SettlementCutovers, UtilityRegister, VehicleClasses, WeighingConfiguration, FineAssessments, Notifications | SuperAdmin,Admin | FineAssessments writes narrow to SuperAdmin |
| Vendors | SuperAdmin,Admin | was anonymous (finding 1) |
| DailyCollections, Payments, Slaughter, Tpm, Trm, GovernedServices, WcfCollections, TerminalSections, ActivationCodes, FineSettlements | SuperAdmin,Admin,Collector | per-method narrowing to office on settlement/OR/config actions; collector writes enforce facility assignment inside the handlers (RecordPayment, RecordDailyCollection, RecordSlaughter, RecordTrip, AddVendor, GenerateStallActivationCode) |
| OfficeSources, FishMeatRegistrationRequests | Authorize + per-action roles | |
| CollectionSession, FastCollections, Mobile, NpmDailyBatch, MobileCollectionCorrections, CollectorAuth (logout) | Collector | owner checks in workflows (see section c) |
| PayorPortal | Payor | every query keyed off the token's user id |
| OnlinePayments | per action | initiate/confirm Payor; reconcile/awaiting-or/dashboard SuperAdmin,Admin; or-number SuperAdmin,Admin,Collector; webhook anonymous, signature-verified |
| PayorAuth | anonymous login/activate/refresh (rate-limited); logout Payor | |
| Platform: Activation (SuperAdmin + `PlatformOperatorGuard` in handlers), Assessment, Onboarding (anonymous intake by single-use token, SuperAdmin review), TenantUsage (SuperAdmin, per-tenant backup/restore), DatabaseHealth (SuperAdmin), PlatformSetup / Setup (anonymous first-run) | | |
| Municipalities | anonymous list / branding / seal; `current/branding` Authorize | presentation fields only |
| MobileBind, MobileVersion | anonymous | bind token is 128-bit random; version is configuration |
| SignalR `/hubs/online-payments` | SuperAdmin,Admin,Collector | group `tenant:{code}` from the token's `municipality` claim |
| SignalR `/hubs/payor` | Payor | group `payor:{NameIdentifier}` |

Exact anonymous surface (31 actions, pinned by `TheAnonymousSurfaceIsExactlyTheDocumentedOne`): Activation.GetContext/SetPassword; AdminAuth.ConsoleLogin/ForgotPassword/GetResetContext/Login/Logout/Refresh/ResetPassword/VerifyEmail/VerifyMfaLogin; Assessment.Submit; CollectorAuth.Login/Refresh; MobileBind.Resolve; MobileVersion.GetVersion; Municipalities.GetBranding/GetMunicipalities/GetSeal; Onboarding.GetDraft/Submit/UpdateConfig; OnlinePayments.Webhook/WebhookForTenant; PayorAuth.Activate/Login/Refresh; PlatformSetup.CreateFirstOperator/GetStatus; Setup.CreateFirstAdmin/GetSetupStatus. Plus `/health` and `/health/ready` minimal endpoints.

The platform operator (`platform_operator` claim) is confined to `/api/activation`, `/api/adminauth`, `/api/assessment`, `/api/municipalities`, `/api/onboarding`, `/api/platform-setup`, `/health` by `PlatformOperatorBoundaryMiddleware` (path-boundary match), so it cannot reach hubs or any municipal endpoint.

## (b) Findings

### 1. High - anonymous vendor registry (FIXED)
- `EEMOCantilanSDS.Api/Controllers/Facilities/VendorsController.cs:8-17` had no `[Authorize]` and the class inherits none from `ApiBaseController`.
- Evidence: `CurrentMunicipalityAccessor.MunicipalityId` returns `store.Default` for a token-less caller (`EEMOCantilanSDS.Infrastructure/Tenancy/CurrentMunicipalityAccessor.cs:28-35`), so `GET /api/vendors/registry?year=&month=` returned the default tenant's vendor registry to anyone. The only caller (`Client/.../Menus/Vendor.razor:6`) is `SuperAdmin,Admin`.
- Proof: `EveryEndpointStatesItsAuthorizationTests.NoActionIsLeftWithoutAnAuthorizationStatement` failed listing `VendorsController.GetVendorRegistry` (and the six sign-in actions) before the change.
- Fix: `[Authorize(Roles = "SuperAdmin,Admin")]`. Commit `a0e88f8d`.

### 2. Medium - Collector can re-encode any OR number in the tenant (FIXED)
- `PaymentsController.SaveOrNumber` (`or-number`), `SlaughterController.SaveOrNumber` (`or`), `TpmController.SaveOrNumber`, `TrmController.SaveOrNumber` inherit class-level `SuperAdmin,Admin,Collector`. Handlers (`SaveOrNumberCommandHandler`, `SaveSlaughterOrNumberCommandHandler`, `SaveVendorOrNumberCommandHandler`, `SaveTripOrNumberCommandHandler`) load the record by id inside the tenant and call `SetOrNumber`, with no collector-assignment or ownership check (the daily-collection OR endpoints were already office-only).
- Impact: accountable-document tampering inside one tenant; not cross-tenant (the record lookup is tenant-filtered).
- Proof: `OrNumberCorrectionIsNotOpenToCollectors` (4 cases) failed before the change.
- Fix: `[Authorize(Roles = "SuperAdmin,Admin")]` on the four actions. Callers are web office pages only (`FacilityPaymentModal`, `FollowUpQueue`, `PastFollowUpQueue`, TPM/TRM/slaughter pages); the Mobile project does not reference these clients. Commit `df4adf4d`.

### 3. Low - sign-in actions anonymous by omission (FIXED)
`AdminAuthController` Login/ConsoleLogin/Refresh/Logout and `CollectorAuthController` Login/Refresh carried only `[EnableRateLimiting]`. Behaviour unchanged; they now say `[AllowAnonymous]` so the new test can distinguish intent from omission. Commit `a0e88f8d`.

### 4. Low - platform-wide DB stats for every LGU Head (OPEN)
`DatabaseHealthController.cs:20-21` is `SuperAdmin`; `DatabaseHealthRepository` reports whole-database size, connection counts, deadlocks, uptime. Aggregates only, no tenant rows, but a Head of one LGU learns the size/load of the shared platform. Decision needed: gate with the existing `PlatformOperator` policy (which is defined in `DependencyInjection.cs:29` but applied nowhere) or accept.

### 5. Low - default-tenant fallback in system settings (OPEN)
`Queries/Settings/GetSystemSettings/GetSystemSettingsQueryHandler.cs:40-41`: `GetByIdentifierAsync(tenantContext.TenantCode) ?? GetDefaultAsync(ct)` and then `OfficeProfile.*` Cantilan constants. Only reachable if the tenant code in the token does not resolve (not observed), but it contradicts "a failed tenant lookup shows an explicit error". Needs a decision on the error shape; not changed.

### 6. Low - cross-tenant "already processed" for offline operations (OPEN)
`Infrastructure/Repositories/SyncRepository.cs:14-25` uses `IgnoreQueryFilters()` and matches `ClientOperationId` only; `CrossTenantReadsAreNamedTests` lists it under "municipality re-applied by hand", which is inaccurate. Effect: a collector of LGU B who submits a ClientOperationId that exists in LGU A is told "already synced" and the operation is silently dropped. Needs a guessed/leaked random GUID, and it leaks only one bit (existence). The legacy `DailyCollections` and `SlaughterTransactions` still have a globally unique index on `ClientOperationId` (`DailyCollectionConfiguration.cs:89`, `SlaughterTransactionConfiguration.cs:19`); fixing the lookup to add `MunicipalityId` is trivial, but the index is a schema matter (no migrations allowed here).

### 7. Low - device tokens looked up across tenants by token only (OPEN)
`CollectorDeviceTokenRepository.UpsertAsync` / `RemoveByTokenAsync` (`:17-19, :43-45`). Another collector who knows a device's FCM token can re-point it to their own account, or delete it. Requires knowing a high-entropy secret. Fix = also match `CollectorId`/`MunicipalityId` on remove, and decide re-point semantics for shared devices.

### 8. Info - payor phone is global by rule (OPEN, design)
`PayorRepository.GetByContactNumberAsync` (`:30-37`) spans tenants and `GenerateStallActivationCodeCommandHandler` refuses a number registered anywhere, so a Head in LGU B can learn that a number is registered in LGU A. Consistent with unscoped payor login (which needs a globally unique number), so it is a product decision (scoped payor login by LGU code vs. global numbers), not a bug.

### 9. Info - cache namespace fallback (OPEN, latent)
`ClaimTenantContext.cs:27` and `TokenService.cs:43` default a blank code to `cantilan-sds` while the data filter uses `municipality_id`. Cache keys (`EemoCacheKeys.*(tenantContext.TenantCode, ...)`) could then mix a tenant's data into Cantilan's namespace if a token ever carried an id without a code. Tokens always carry both today (`TokenService.cs:57,61`).

## (c) Verified correct (evidence)

- **Tenant resolution fails closed.** `CurrentMunicipalityAccessor` (authenticated => own id or `Guid.Empty`, never default) and `AppDbContext.SetOwnedAuditableFilter/SetOwnedFilter` (`Guid.Empty` matches nothing). `TenantFilterCoverageTests` (every `IMunicipalityOwned` entity is filtered) and new `TenantlessEntitiesArePinnedTests` (only `Municipality`, `AssessmentRequest`, `OnboardingDraft` carry no tenant). Run: pass.
- **No client-supplied tenant is trusted for data access.** grep for headers/`tenant=` finds only the PayMongo signature, forwarded-proto/for, and the allow-listed `Origin` for checkout return URLs (`OnlinePaymentUrlBuilder.PayorReturnBaseUrl` matches `AllowedReturnOrigins`). Login `MunicipalityCode` only narrows the username lookup; password and a post-check `boundaryId != collector.MunicipalityId` still apply (`CollectorLoginCommandHandler`).
- **`IgnoreQueryFilters` inventory (94 sites).** Read all repository/handler sites: pre-auth identity lookups by globally unique secret then pin the tenant; `OrNumberRegistry`, `AdminRepository`, `CollectorRepository` re-apply `MunicipalityId == mid`; Municipality handlers load the caller's own id; export/restore/audit are scoped (`TenantRestoreRepository` uses `CurrentMunicipalityId`, rejects a snapshot whose `MunicipalityId` differs, restore SQL has `WHERE "MunicipalityId" = @mid`). Exceptions are findings 6 and 7. `CrossTenantReadsAreNamedTests` pins the file list.
- **Raw SQL.** All `ExecuteSql*`/`CommandText` sites are advisory locks, DB-health/usage counters, or the tenant restore/snapshot (parameterised `@mid`, table names from `TenantDataTables.Restorable` allow-list). No `FromSql`.
- **`Find/FindAsync` by key.** None in Application/Infrastructure (only catalog `Find` helpers and `store.FindAsync(tenantId, sessionId)` with the tenant in the key).
- **Caches.** Every `IEemoAppCache.GetOrCreateAsync` caller builds its key and invalidation regions from `tenantContext.TenantCode` (Dashboard, FacilityReports, FacilitySummaries, MonthEnd, FinancialReport, FollowUpHistory, ClosedAccounts, StallHolders).
- **Mobile collector.** `CollectionSessionWorkflow.ResolveAsync` refuses a session owned by another collector (`prior.CollectorId != user.CollectorId`), `GetAsync` returns NotFound; all idempotency lookups include `MunicipalityId`; `MobileCollectionCorrectionWorkflow` requires `c.CollectorId == user.CollectorId` and `Authorized()` checks tenant and active collector; sync handler requires `CollectorId` and dispatches commands that enforce facility assignment.
- **Payor portal.** History, initiate and confirm check `LinkExistsAsync(payorId, stallId)` / `transaction.PayorUserId == currentUser.UserId`; balances and payable items are keyed on the token user; payor token holds only the Payor role, so staff controllers reject it.
- **PayMongo.** `HandlePaymentWebhookCommandHandler` pins the LGU from the URL, verifies the signature against that LGU's secret (empty secret => `false`, 12h timestamp window, constant-time compare), refuses a transaction whose municipality differs from the signed one, and authorises the tenant-less legacy webhook for the default municipality only. `PayMongoCredentialResolver` gives a non-default LGU with no own account `PayMongoCredentials.None` (no fall-through to Cantilan's merchant).
- **SignalR.** `OnlinePaymentHub` groups by the token's tenant claim; `SignalROnlinePaymentNotifier` addresses the transaction's tenant group and drops (not broadcasts) a notification with no tenant. `PayorNotificationHub` group is the payor's own id.
- **Background jobs.** `DailyTenantBackupService` is the only hosted service; it lists active municipalities, creates a scope per tenant and calls `IRequestTenantScope.Use(id, code)` before resolving the DbContext, so a Madrid pass cannot read or stamp Cantilan rows.
- **Platform operator isolation.** `PlatformOperatorBoundaryMiddleware` (path-boundary match) + `PlatformOperatorGuard` in the assessment/onboarding/activation handlers.
- **Push.** `FcmPushSender.SendToCollectorAsync` reads tokens through the tenant-filtered set, so an Admin of LGU B cannot push to LGU A's collector.

## (d) Not audited

- The Blazor Client (`EEMOCantilanSDS.Client`) and Mobile apps' own authorisation; `HttpClients` request building.
- Every one of the ~508 actions' handlers individually: I traced the roles for all controllers, and read handlers for payments/payor/mobile/sync/backup/webhook/auth/municipality paths. The long tail of office workflows (Obligations, Remittances, OfficialReports, Penalties, AccountableForms, Lifecycle, canonical reports) was not read handler by handler; they sit behind `SuperAdmin,Admin` and the tenant filter.
- Cross-tenant FK injection through a client-supplied foreign key that is attached without being loaded (the stamp interceptor only fills an empty `MunicipalityId`, and the database FKs are single-column). I found no command accepting a tenant id from the client except the operator-guarded `ActivateMunicipality`, but did not prove every create path loads its parents through the filter.
- JWT lifetime / revocation, refresh-token rotation, MFA, password hashing, lockout, rate-limit tuning, CORS, `Jwt:Key` management, security headers beyond reading them.
- Whether a suspended / inactive municipality is blocked at login and token refresh.
- Integration tests on Testcontainers (no integration project was affected by the changes: only attribute changes on controllers, no integration test references those controllers).
- Production data and configuration (nothing was run against any database).

## Tests

- New: `EEMOCantilanSDS.Testing/Api/EveryEndpointStatesItsAuthorizationTests.cs` (7 cases: no action without a statement; anonymous surface pinned; OR correction not open to collectors x4), `EEMOCantilanSDS.Testing/Infrastructure/Persistence/TenantlessEntitiesArePinnedTests.cs` (1 case).
