using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Infrastructure.Caching;
using EEMOCantilanSDS.Infrastructure.Payments;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Repositories;
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
            service.AddSingleton<ICurrentMunicipalityAccessor, CurrentMunicipalityAccessor>();
 
            
            // Repositories
            service.AddScoped<IAuthRepository, AuthRepository>();
            service.AddScoped<ISetupRepository, SetupRepository>();
            service.AddScoped<IAdminRepository, AdminRepository>();
            service.AddScoped<ICollectorRepository, CollectorRepository>();
            service.AddScoped<IStallRepository, StallRepository>();
            service.AddScoped<IFacilityRepository, FacilityRepository>();
            service.AddScoped<IMunicipalityRepository, MunicipalityRepository>();
            service.AddScoped<IPaymentRepository, PaymentRepository>();
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


            // Services
            service.AddScoped<ICurrentUserService, CurrentUserService>();
            service.AddScoped<ITokenService, TokenService>();
            service.AddScoped<IOnlinePaymentUrlBuilder, OnlinePaymentUrlBuilder>();

            // Online payment gateway (PayMongo hosted checkout, GCash). Secret-key Basic auth is
            // applied once here; the key is the username with an empty password, base64-encoded.
            var payMongo = configuration.GetSection("PayMongo");
            service.AddHttpClient<IPaymentGateway, PayMongoPaymentGateway>(client =>
            {
                var baseUrl = payMongo["BaseUrl"] ?? throw new InvalidOperationException("PayMongo BaseUrl not configured");

                // Ensure relative request paths (e.g. "checkout_sessions") resolve under the version segment.
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

                var secretKey = payMongo["SecretKey"] ?? string.Empty;
                var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secretKey}:"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
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
