using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
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
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http.Headers;

namespace EEMOCantilanSDS.Infrastructure
{
    /// <summary>
    /// Infrastructure's composition root.
    ///
    /// <para>
    /// One entry point, <see cref="AddInfrastructureService"/>, over seven groups that each register ONE concern. It was a
    /// single 150-line method mixing persistence, caching, tenancy, repositories, security, payments and outbound HTTP, so
    /// nothing could be read or changed without reading all of it, and a dropped line looked like every other line.
    /// </para>
    ///
    /// <para>
    /// The groups are called in the order the registrations were originally written, and the resulting container is identical:
    /// same service types, same lifetimes, same implementations. <c>CompositionRootTests</c> holds that — it resolves every
    /// service this codebase registers, all 550 of them, including every MediatR handler and validator, so a group left
    /// uncalled is a failing test rather than a 500 on the one page that needed it.
    /// </para>
    /// </summary>
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructureService(this IServiceCollection service, IConfiguration configuration)
            => service
                .AddPersistence(configuration)
                .AddEemoCaching()
                .AddTenancyAndRates()
                .AddRepositories()
                .AddInfrastructureServices(configuration)
                .AddOnlinePayments(configuration)
                .AddBackupGateway(configuration);

        /// <summary>The database itself: the context, the interceptors that stamp every write, and the unit of work.</summary>
        private static IServiceCollection AddPersistence(this IServiceCollection service, IConfiguration configuration)
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
            return service;
        }

        /// <summary>The in-process report cache and the invalidator that clears it when money moves.</summary>
        private static IServiceCollection AddEemoCaching(this IServiceCollection service)
        {
            var eemoCacheOptions = new EemoCacheOptions();
            service.AddMemoryCache(options => options.SizeLimit = eemoCacheOptions.SizeLimit);
            service.AddSingleton(eemoCacheOptions);
            service.AddSingleton<MemoryEemoCacheInvalidator>();
            service.AddSingleton<IEemoCacheInvalidator>(sp => sp.GetRequiredService<MemoryEemoCacheInvalidator>());
            service.AddSingleton<IEemoAppCache, MemoryEemoAppCache>();
            return service;
        }

        /// <summary>
        /// Which LGU a request belongs to, and the figures that vary by LGU.
        ///
        /// <para>
        /// Grouped together because they answer the same question from different angles: the tenant context says WHOSE data
        /// this is, and the rate and market-day resolvers say what that LGU's own ordinance charges. Both read the tenant's
        /// records and both fall back to the constants so the default municipality is byte-for-byte unchanged.
        /// </para>
        /// </summary>
        private static IServiceCollection AddTenancyAndRates(this IServiceCollection service)
        {
            service.AddScoped<ITenantContext, ClaimTenantContext>();
            // Optional per-request tenant override (anonymous webhook settles under the transaction's LGU).
            // Empty by default, so ordinary requests resolve their tenant exactly as before.
            service.AddScoped<IRequestTenantScope, RequestTenantScope>();
            // Per-request tenant resolution: the default municipality lives in a process-wide singleton, populated once at
            // startup; the accessor resolves per-request off the authenticated user, falling back to that default. The stamp
            // interceptor stays a singleton — it reads the resolved id off the DbContext, not via DI.
            service.AddSingleton<DefaultMunicipalityStore>();
            service.AddScoped<ICurrentMunicipalityAccessor, CurrentMunicipalityAccessor>();
            // Per-LGU fixed-rate resolution: reads the current municipality's FacilityRate rows, falling back to the
            // FeeRates constants.
            service.AddScoped<IFeeRateResolver, FeeRateResolver>();
            // Per-LGU Tabo-an market weekday (defaults to Friday) — reads the tenant's Municipality record.
            service.AddScoped<ITpmMarketDayProvider, TpmMarketDayProvider>();
            return service;
        }

        /// <summary>
        /// The repositories, and the narrow query contracts served by the same instances.
        ///
        /// <para>
        /// Where a repository also answers a narrower contract, that contract is a FACTORY over the wide registration rather
        /// than a second <c>AddScoped</c> of the same type: one request then shares one instance and one change tracker, where
        /// a second registration would build two for no reason.
        /// </para>
        /// </summary>
        private static IServiceCollection AddRepositories(this IServiceCollection service)
        {
            service.AddScoped<IAuthRepository, AuthRepository>();
            service.AddScoped<ISetupRepository, SetupRepository>();
            service.AddScoped<IAdminRepository, AdminRepository>();

            service.AddScoped<ICollectorRepository, CollectorRepository>();
            service.AddScoped<ICollectorMobileQueries>(sp => (CollectorRepository)sp.GetRequiredService<ICollectorRepository>());
            service.AddScoped<ICollectorReportingQueries>(sp => (CollectorRepository)sp.GetRequiredService<ICollectorRepository>());
            service.AddScoped<ICollectorDeviceTokenRepository, CollectorDeviceTokenRepository>();
            service.AddScoped<IPushSender, EEMOCantilanSDS.Infrastructure.Services.FcmPushSender>();

            service.AddScoped<IStallRepository, StallRepository>();
            service.AddScoped<IStallMobileQueries>(sp => (StallRepository)sp.GetRequiredService<IStallRepository>());
            service.AddScoped<IClosedStallAccountQueries>(sp => (StallRepository)sp.GetRequiredService<IStallRepository>());
            service.AddScoped<IContractAttentionQueries>(sp => (StallRepository)sp.GetRequiredService<IStallRepository>());
            service.AddScoped<IStallRegisterQueries>(sp => (StallRepository)sp.GetRequiredService<IStallRepository>());

            // The OR rule is LGU-wide, so it is its own service rather than a method on whichever module repository
            // a handler happens to hold. Scoped: it shares the request's context and change tracker.
            service.AddScoped<IOrNumberRegistry, DbOrNumberRegistry>();

            // The clock is stateless, so a singleton: every caller reads the same real time.
            service.AddSingleton<IClock, SystemClock>();

            service.AddScoped<IFacilityRepository, FacilityRepository>();
            service.AddScoped<IMunicipalityRepository, MunicipalityRepository>();

            service.AddScoped<IPaymentRepository, PaymentRepository>();
            service.AddScoped<IStallLedgerQueries>(sp => (PaymentRepository)sp.GetRequiredService<IPaymentRepository>());
            service.AddScoped<IMissingReceiptQueries>(sp => (PaymentRepository)sp.GetRequiredService<IPaymentRepository>());

            service.AddScoped<IStallMonthlyExceptionRepository, StallMonthlyExceptionRepository>();
            service.AddScoped<INpmMarketClosureRepository, NpmMarketClosureRepository>();
            service.AddScoped<IVendorRepository, VendorRepository>();
            service.AddScoped<ITpmRepository, TpmRepository>();
            service.AddScoped<ITrmRepository, TrmRepository>();
            service.AddScoped<ISlaughterRepository, SlaughterRepository>();
            service.AddScoped<IDailyCollectionRepository, DailyCollectionRepository>();
        service.AddScoped<ICollectorReportQueries, CollectorReportQueries>();
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

            // One backup of every active municipality per day, so an office's records are recoverable without somebody remembering
            // to ask. Deliberately the LAST thing registered here and answerable to nothing: it holds no other component's
            // dependencies, and if it never runs - Always On is switched off from time to time to keep the bill down - the platform
            // is exactly what it was. See DailyTenantBackupService for why it asks "has today's been taken?" rather than sleeping
            // for a day, which is what makes a sleeping app's schedule self-healing.
            service.AddHostedService<EEMOCantilanSDS.Infrastructure.Services.DailyTenantBackupService>();

            return service;
        }

        /// <summary>Who the caller is, how a password is stored, how a session is issued, and how the office is emailed.</summary>
        private static IServiceCollection AddInfrastructureServices(this IServiceCollection service, IConfiguration configuration)
        {
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
            return service;
        }

        /// <summary>
        /// Online payments: each LGU's own gateway account, and the protector that keeps its secrets at rest.
        /// </summary>
        private static IServiceCollection AddOnlinePayments(this IServiceCollection service, IConfiguration configuration)
        {
            // Per-LGU payment credentials: AES-GCM protector for secrets at rest + a resolver that returns the current
            // tenant's PayMongo account, falling back to the global config (default LGU).
            service.AddSingleton<EEMOCantilanSDS.Application.Common.Interface.Services.ICredentialProtector,
                EEMOCantilanSDS.Infrastructure.Security.AesCredentialProtector>();
            service.AddScoped<EEMOCantilanSDS.Application.Common.Interface.Services.IPayMongoCredentialResolver,
                EEMOCantilanSDS.Infrastructure.Payments.PayMongoCredentialResolver>();

            // PayMongo hosted checkout. Auth is applied PER-REQUEST by the gateway from the tenant's resolved credentials,
            // so each LGU hits its own account and the default LGU uses the global config — no default Authorization here.
            //
            // The base address is read HERE, at startup, and a missing one stops the application.
            //
            // It used to be read inside the client-configuration callback, which runs the first time the gateway is resolved —
            // so a deployment without PayMongo:BaseUrl started perfectly and then failed on the three online-payment endpoints,
            // in front of whoever was on shift, with an exception rather than an explanation. A configuration fault should be
            // discovered by the deployment, not by a payor. Read once, checked once, and the callback is left with nothing that
            // can throw.
            var baseUrl = configuration.GetSection("PayMongo")["BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException(
                    "PayMongo:BaseUrl is not configured. Online payments cannot be served without it, so the application is " +
                    "refusing to start rather than failing later on the payment endpoints. Set PayMongo__BaseUrl (for example " +
                    "https://api.paymongo.com/v1/).");
            }

            // Ensure relative request paths (e.g. "checkout_sessions") resolve under the version segment.
            var gatewayBase = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

            service.AddHttpClient<IPaymentGateway, PayMongoPaymentGateway>(client =>
            {
                client.BaseAddress = gatewayBase;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });

            // The same base address, because it is the same provider - but its own client, because this one authenticates
            // with a key the office is in the middle of entering rather than with the current tenant's stored credentials.
            service.AddHttpClient<IPayMongoAccountVerifier, PayMongoAccountVerifier>(client =>
            {
                client.BaseAddress = gatewayBase;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });

            return service;
        }

        /// <summary>
        /// The GitHub Actions backup gateway. The token is bound from configuration and applied once here as a Bearer
        /// default header — it stays server-side and is never returned to the client.
        /// </summary>
        private static IServiceCollection AddBackupGateway(this IServiceCollection service, IConfiguration configuration)
        {
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
