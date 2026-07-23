using global::EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using global::EEMOCantilanSDS.HttpClients.ApiClients;
using EEMOCantilanSDS.Mobile.Security;
using EEMOCantilanSDS.Mobile.Services;
using Microsoft.Extensions.Logging;
using EEMOCantilanSDS.Mobile.Abstractions;

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
                    fonts.AddFont("EBGaramond-Regular.ttf",  "EBGaramondRegular");
                    fonts.AddFont("EBGaramond-SemiBold.ttf", "EBGaramondSemiBold");
                    fonts.AddFont("DMSans-Regular.ttf",   "DMSansRegular");
                    fonts.AddFont("DMSans-Medium.ttf",    "DMSansMedium");
                    fonts.AddFont("DMSans-SemiBold.ttf",  "DMSansSemiBold");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddSingleton<MobileTokenStore>();
            builder.Services.AddSingleton<MobileSessionService>();
            builder.Services.AddSingleton<MobilePaymentHubService>();
            builder.Services.AddSingleton<EEMOCantilanSDS.Mobile.Abstractions.IConnectivityMonitor, EEMOCantilanSDS.Mobile.Platform.MauiConnectivityMonitor>();
            builder.Services.AddSingleton<EEMOCantilanSDS.Mobile.Abstractions.IPendingOperationStore>(
                _ => new EEMOCantilanSDS.Mobile.Services.PendingOperationStore(FileSystem.AppDataDirectory));
            builder.Services.AddSingleton<EEMOCantilanSDS.Mobile.Abstractions.IOfflineReadCache>(
                _ => new EEMOCantilanSDS.Mobile.Services.JsonOfflineReadCache(FileSystem.AppDataDirectory));
            builder.Services.AddSingleton<EEMOCantilanSDS.Mobile.Abstractions.ICurrentCollectorProvider, EEMOCantilanSDS.Mobile.Platform.MauiCurrentCollectorProvider>();
            builder.Services.AddSingleton<EEMOCantilanSDS.Mobile.Services.MobileSyncService>();
            builder.Services.AddTransient<MobileLoopbackFallbackHandler>();
            builder.Services.AddTransient<MobileAuthorizationDelegatingHandler>();
            builder.Services.AddTransient<MobileRefreshTokenDelegatingHandler>();

            builder.Services.AddHttpClient<ICollectorAuthApiClient, CollectorAuthApiClient>(client =>
            {
                client.BaseAddress = new Uri(GetApiBaseUrl());
                // Login may need to wake the production database and run password verification.
                // Keep the timeout bounded, but do not fail a valid sign-in during a brief Azure delay.
                client.Timeout = TimeSpan.FromSeconds(30);
            }).AddHttpMessageHandler<MobileLoopbackFallbackHandler>();

            // Anonymous pre-login municipalities list — powers the collector login's municipality picker so
            // the login can be scoped to the right LGU. No auth handlers (the endpoint is anonymous).
            builder.Services.AddHttpClient<IMunicipalitiesApiClient, MunicipalitiesApiClient>(client =>
            {
                client.BaseAddress = new Uri(GetApiBaseUrl());
                client.Timeout = TimeSpan.FromSeconds(10);
            }).AddHttpMessageHandler<MobileLoopbackFallbackHandler>();

            // Inner HTTP client (concrete) — the real network calls. IMobileApiClient is exposed as the
            // caching decorator below, so every consumer transparently gets offline read-through caching.
            builder.Services.AddHttpClient<MobileApiClient>(client =>
            {
                client.BaseAddress = new Uri(GetApiBaseUrl());
                // Keep reads short so transient failures fall back to the offline cache promptly.
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddHttpMessageHandler<MobileLoopbackFallbackHandler>()
            .AddHttpMessageHandler<MobileRefreshTokenDelegatingHandler>()
            .AddHttpMessageHandler<MobileAuthorizationDelegatingHandler>();

            builder.Services.AddSingleton<IMobileApiClient>(sp =>
                new CachingMobileApiClient(
                    sp.GetRequiredService<MobileApiClient>(),
                    sp.GetRequiredService<IOfflineReadCache>(),
                    sp.GetRequiredService<IConnectivityMonitor>()));

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }

        internal static string GetApiBaseUrl()
        {
#if DEBUG
            // NOTE: DEBUG builds target the LOCAL dev API (below); only RELEASE builds — the ones
            // distributed to collectors — target production. Do NOT reintroduce a forced production
            // override here, or a Debug build would silently hit the live database.

#if ANDROID            // Android emulator uses 10.0.2.2 to reach the host machine (localhost on the dev PC).
            // USB-connected phone uses localhost via: adb reverse tcp:5117 tcp:5117
            var isEmulator = global::Android.OS.Build.Fingerprint?.Contains("generic") == true
                          || global::Android.OS.Build.Fingerprint?.Contains("emulator") == true
                          || global::Android.OS.Build.Model?.Contains("Emulator") == true
                          || global::Android.OS.Build.Model?.Contains("Android SDK") == true;
            return isEmulator ? "http://10.0.2.2:5117/" : "http://localhost:5117/";
#else
            return "http://localhost:5117/";
#endif
#else
            // ── Production (RELEASE) ── HTTPS only; no dev tunnel, no cleartext HTTP.
            return "https://api.stalltrack.site/";
#endif
        }
    }
}
