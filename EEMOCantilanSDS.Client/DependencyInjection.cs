using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Extensions;
using EEMOCantilanSDS.Client.Securities;
using EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace EEMOCantilanSDS.Client
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddClient(this IServiceCollection service, IConfiguration configuration)
        {
            service.AddRazorComponents()
                .AddInteractiveServerComponents();

            AddPersistence(service, configuration);

            service.AddHttpContextAccessor();
            
            // Add Cookie Authentication
            service.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.Cookie.Name = ".AspNetCore.Cookies";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                    options.SlidingExpiration = true;
                    options.LoginPath = "/login";
                    options.AccessDeniedPath = "/login";
                    options.LogoutPath = "/api/authproxy/logout";
                });
            
            service.AddScoped<TokenService>();
            service.AddScoped<CircuitHandler, TokenCircuitHandler>();
            service.AddScoped<AuthService>();
            service.AddScoped<AuthorizationDelegatingHandler>();
            service.AddScoped<RefreshTokenDelegatingHandler>();
            service.AddScoped<AuthStateProvider>();

            service.AddScoped<AuthenticationStateProvider>(provider =>
                provider.GetRequiredService<AuthStateProvider>());

            service.AddControllers();

            return service;
        }

        public static IServiceCollection AddPersistence(this IServiceCollection service, IConfiguration configuration)
        {
            service.AddHttpClient<IAuthApiClient, AuthApiClient>("AuthClient", client =>
            {
                client.BaseAddress = new Uri(configuration["ApiBaseUrl"]!);
            });

            service.AddHttpClient("RefreshClient", client =>
            {
                client.BaseAddress = new Uri(configuration["ApiBaseUrl"]!);
            });



            service.AddApiHttpClient<ISetupApiClient, SetupApiClient>(configuration);
            service.AddApiHttpClient<IStallsApiClient, StallsApiClient>(configuration);
            service.AddApiHttpClient<ICollectorsApiClient, CollectorsApiClient>(configuration);
            service.AddApiHttpClient<IAdminsApiClient, AdminsApiClient>(configuration);
            service.AddApiHttpClient<IPaymentsApiClient, PaymentsApiClient>(configuration);
            service.AddApiHttpClient<IVendorsApiClient, VendorsApiClient>(configuration);
            service.AddApiHttpClient<ITpmApiClient, TpmApiClient>(configuration);
            service.AddApiHttpClient<ITrmApiClient, TrmApiClient>(configuration);
            service.AddApiHttpClient<ISlaughterApiClient, SlaughterApiClient>(configuration);
            service.AddApiHttpClient<IDailyCollectionApiClient, DailyCollectionApiClient>(configuration);
            service.AddApiHttpClient<IFacilitiesApiClient, FacilitiesApiClient>(configuration);
            service.AddApiHttpClient<IDashboardApiClient, DashboardApiClient>(configuration);
            service.AddApiHttpClient<ITransactionsApiClient, TransactionsApiClient>(configuration);

            return service;
        }
     

    }
}
