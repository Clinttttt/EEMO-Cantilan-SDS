using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Extensions;
using EEMOCantilanSDS.Client.Securities;
using EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;
using Microsoft.AspNetCore.Components.Authorization;

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
            service.AddScoped<AuthService>();
            service.AddScoped<AuthorizationDelegatingHandler>();
            service.AddScoped<RefreshTokenDelegatingHandler>();
            service.AddScoped<AuthStateProvider>();
            service.AddScoped<AuthenticationStateProvider>(provider =>
                provider.GetRequiredService<AuthStateProvider>());
            return service;
        }

        public static IServiceCollection AddPersistence(this IServiceCollection service, IConfiguration configuration)
        {
            service.AddHttpClient<IAuthApiClient, AuthApiClient>("AuthClient", client =>
            {
                client.BaseAddress = new Uri(configuration["ApiBaseUrl"]!);
            });

            service.AddApiHttpClient<ISetupApiClient, SetupApiClient>(configuration);
            service.AddApiHttpClient<IStallsApiClient, StallsApiClient>(configuration);
   

            return service;
        }
     

    }
}
