using global::EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using global::EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;
using EEMOCantilanSDS.Mobile.Security;
using EEMOCantilanSDS.Mobile.Services;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Mobile
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddSingleton<MobileTokenStore>();
            builder.Services.AddSingleton<MobileSessionService>();
            builder.Services.AddTransient<MobileAuthorizationDelegatingHandler>();

            builder.Services.AddHttpClient<ICollectorAuthApiClient, CollectorAuthApiClient>(client =>
            {
                client.BaseAddress = new Uri(GetApiBaseUrl());
                client.Timeout = TimeSpan.FromSeconds(10);
            });

            builder.Services.AddHttpClient<IMobileApiClient, MobileApiClient>(client =>
            {
                client.BaseAddress = new Uri(GetApiBaseUrl());
                client.Timeout = TimeSpan.FromSeconds(10);
            }).AddHttpMessageHandler<MobileAuthorizationDelegatingHandler>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }

        private static string GetApiBaseUrl()
        {
            // Connected phone development URL. Run: adb reverse tcp:5117 tcp:5117
            return "http://127.0.0.1:5117/";
        }
    }
}
