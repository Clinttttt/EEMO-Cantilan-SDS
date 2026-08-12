using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Infrastructure.Caching;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Payments;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Security;
using EEMOCantilanSDS.Infrastructure.Services;
using EEMOCantilanSDS.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructureService(this IServiceCollection service, IConfiguration configuration)
        {
            service.AddScoped<AuditSaveChangesInterceptor>();
            service.AddSingleton<MunicipalityStampInterceptor>();
            service.AddDbContext<AppDbContext>((sp, options) =>
            {
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
                options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
                options.AddInterceptors(sp.GetRequiredService<MunicipalityStampInterceptor>());
            });
            service.AddScoped<IAppDbContext, AppDbContext>();
            service.AddScoped<IUnitOfWork, UnitOfWork>();
            var eemoCacheOptions = new EemoCacheOptions();
            service.AddMemoryCache(options => options.SizeLimit = eemoCacheOptions.SizeLimit);
            service.AddSingleton(eemoCacheOptions);
            service.AddSingleton<MemoryEemoCacheInvalidator>();
            service.AddSingleton<IEemoCacheInvalidator>(sp => sp.GetRequiredService<MemoryEemoCacheInvalidator>());
            service.AddSingleton<IEemoAppCache, MemoryEemoAppCache>();
            service.AddScoped<ITenantContext, ClaimTenantContext>();
            // Optional per-request tenant override (anonymous webhook settles under the transaction's LGU).
            // Empty by default, so ordinary requests resolve their tenant exactly as before.
            service.AddScoped<IRequestTenantScope, RequestTenantScope>();
            // Per-request tenant resolution (Phase 5): the default municipality (Cantilan) lives in a
            // process-wide singleton, populated once at startup; the accessor resolves per-request off the
            // authenticated user, falling back to that default. The stamp interceptor stays a singleton —
            // it reads the resolved id off the DbContext, not via DI.
            service.AddSingleton<DefaultMunicipalityStore>();
            service.AddScoped<ICurrentMunicipalityAccessor, CurrentMunicipalityAccessor>();
            // Per-LGU fixed-rate resolution (Phase 4B): reads the current municipality's FacilityRate rows,
            // falling back to the FeeRates constants so Cantilan is byte-for-byte unchanged.
            service.AddScoped<IFeeRateResolver, FeeRateResolver>();
            // Per-LGU Tabo-an market weekday (defaults to Friday) — reads the tenant's Municipality record.
            service.AddScoped<ITpmMarketDayProvider, TpmMarketDayProvider>();
 
            
            // Repositories
            service.AddScoped<IAuthRepository, AuthRepository>();
            service.AddScoped<ISetupRepository, SetupRepository>();
            service.AddScoped<IAdminRepository, AdminRepository>();
            service.AddScoped<ICollectorRepository, CollectorRepository>();
            // Same instance, one change tracker per request — see the IStallLedgerQueries registration below.
            service.AddScoped<ICollectorMobileQueries>(sp => (CollectorRepository)sp.GetRequiredService<ICollectorRepository>());
            service.AddScoped<ICollectorReportingQueries>(sp => (CollectorRepository)sp.GetRequiredService<ICollectorRepository>());
            service.AddScoped<ICollectorDeviceTokenRepository, CollectorDeviceTokenRepository>();
            service.AddScoped<IPushSender, EEMOCantilanSDS.Infrastructure.Services.FcmPushSender>();
            service.AddScoped<IStallRepository, StallRepository>();
            // Same instance, one change tracker per request. See the IStallLedgerQueries registration below for why this
            // is a factory over the existing registration rather than a second AddScoped of the same type.
            service.AddScoped<IStallMobileQueries>(sp => (StallRepository)sp.GetRequiredService<IStallRepository>());
            service.AddScoped<IClosedStallAccountQueries>(sp => (StallRepository)sp.GetRequiredService<IStallRepository>());
            service.AddScoped<IContractAttentionQueries>(sp => (StallRepository)sp.GetRequiredService<IStallRepository>());
            service.AddScoped<IStallRegisterQueries>(sp => (StallRepository)sp.GetRequiredService<IStallRepository>());
            service.AddScoped<IFacilityRepository, FacilityRepository>();
            service.AddScoped<IMunicipalityRepository, MunicipalityRepository>();
            service.AddScoped<IPaymentRepository, PaymentRepository>();
            // The same instance answers the narrow per-stall ledger reads, so one request shares one repository and one
            // change tracker. Registered as a factory over the wide registration rather than as a second AddScoped of
            // the same type, which would build two instances per request for no reason.
            service.AddScoped<IStallLedgerQueries>(sp => (PaymentRepository)sp.GetRequiredService<IPaymentRepository>());
            service.AddScoped<IMissingReceiptQueries>(sp => (PaymentRepository)sp.GetRequiredService<IPaymentRepository>());
            service.AddScoped<IStallMonthlyExceptionRepository, StallMonthlyExceptionRepository>();
            service.AddScoped<INpmMarketClosureRepository, NpmMarketClosureRepository>();
            service.AddScoped<IVendorRepository, VendorRepository>();
            service.AddScoped<ITpmRepository, TpmRepository>();
            service.AddScoped<ITrmRepository, TrmRepository>();
            service.AddScoped<ISlaughterRepository, SlaughterRepository>();
            service.AddScoped<IDailyCollectionRepository, DailyCollectionRepository>();
            service.AddScoped<IUtilityBillRepository, EEMOCantilanSDS.Infrastructure.Repositories.Payments.UtilityBillRepository>();
            service.AddScoped<IFacilityReportsRepository, FacilityReportsRepository>();
            service.AddScoped<IDashboardRepository, DashboardRepository>();
            service.AddScoped<ITransactionFeedRepository, TransactionFeedRepository>();
            service.AddScoped<ISuggestionRepository, SuggestionRepository>();
            service.AddScoped<IPayorRepository, PayorRepository>();
            service.AddScoped<IOnlinePaymentRepository, OnlinePaymentRepository>();
            service.AddScoped<ISyncRepository, SyncRepository>();
            service.AddScoped<IAuditRepository, AuditRepository>();
            service.AddScoped<IDatabaseHealthRepository, EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth.DatabaseHealthRepository>();
            service.AddHttpClient<EEMOCantilanSDS.Application.Common.Interface.Services.IComputeMetricsProvider,
                EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth.AzureComputeMetricsProvider>();
            service.AddScoped<ITenantUsageRepository, EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth.TenantUsageRepository>();
            service.AddScoped<ITenantExportRepository, EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth.TenantExportRepository>();
            service.AddScoped<ITenantRestoreRepository, EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth.TenantRestoreRepository>();
            service.AddScoped<ITenantBackupRepository, EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth.TenantBackupRepository>();


            // Services
            service.AddScoped<ICurrentUserService, CurrentUserService>();
            // The one place that knows how a password is stored. Singleton: it holds no state beyond the hasher itself,
            // and the format must be identical everywhere or existing accounts stop verifying.
            service.AddSingleton<Application.Common.Interface.Security.IPasswordHasher, IdentityPasswordHasher>();
            service.AddScoped<ITokenService, TokenService>();
            service.AddScoped<IOnlinePaymentUrlBuilder, OnlinePaymentUrlBuilder>();

            // Transactional email (SMTP). Bound once; a no-op until configured (Email__Host / Email__FromEmail).
            var emailOptions = new EEMOCantilanSDS.Infrastructure.Services.EmailOptions();
            configuration.GetSection("Email").Bind(emailOptions);
            service.AddSingleton(emailOptions);
            service.AddScoped<IEmailSender, EEMOCantilanSDS.Infrastructure.Services.SmtpEmailSender>();
            // Two-factor: RFC 6238 TOTP + QR rendering. Stateless, so singletons are fine.
            service.AddSingleton<EEMOCantilanSDS.Application.Common.Interface.Services.ITotpService,
                EEMOCantilanSDS.Infrastructure.Security.TotpService>();
            service.AddSingleton<EEMOCantilanSDS.Application.Common.Interface.Services.IQrCodeGenerator,
                EEMOCantilanSDS.Infrastructure.Security.QrCodeGenerator>();
            // Issues + emails one-time email-confirmation links (used on admin create, email change, and
            // Head-triggered resend). Lives in Application; registered here beside the SMTP sender it uses.
            service.AddScoped<EEMOCantilanSDS.Application.Common.Interface.Services.IEmailVerificationSender,
                EEMOCantilanSDS.Application.Common.Services.EmailVerificationSender>();

            // Per-LGU payment credentials (Option A): AES-GCM protector for secrets at rest + a resolver that
            // returns the current tenant's PayMongo account, falling back to the global config (Cantilan default).
            service.AddSingleton<EEMOCantilanSDS.Application.Common.Interface.Services.ICredentialProtector,
                EEMOCantilanSDS.Infrastructure.Security.AesCredentialProtector>();
            service.AddScoped<EEMOCantilanSDS.Application.Common.Interface.Services.IPayMongoCredentialResolver,
                EEMOCantilanSDS.Infrastructure.Payments.PayMongoCredentialResolver>();

            // Online payment gateway (PayMongo hosted checkout). Auth is applied PER-REQUEST by the gateway
            // from the tenant's resolved credentials (IPayMongoCredentialResolver), so each LGU hits its own
            // account and the default LGU (Cantilan) uses the global config — no default Authorization here.
            var payMongo = configuration.GetSection("PayMongo");
            service.AddHttpClient<IPaymentGateway, PayMongoPaymentGateway>(client =>
            {
                var baseUrl = payMongo["BaseUrl"] ?? throw new InvalidOperationException("PayMongo BaseUrl not configured");

                // Ensure relative request paths (e.g. "checkout_sessions") resolve under the version segment.
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });

            // GitHub Actions backup gateway. The token is bound from configuration and applied once here
            // as a Bearer default header — it stays server-side and is never returned to the client.
            var gitHubBackup = new GitHubBackupOptions();
            configuration.GetSection("GitHubBackup").Bind(gitHubBackup);
            service.AddSingleton(gitHubBackup);
            service.AddHttpClient<IBackupService, GitHubActionsBackupService>(client =>
            {
                client.BaseAddress = new Uri("https://api.github.com/");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("StallTrack-Backup");
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                if (!string.IsNullOrWhiteSpace(gitHubBackup.Token))
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", gitHubBackup.Token);
            });

            return service;

        }
    }
}
