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
                client.Timeout = TimeSpan.FromSeconds(10);
                // ngrok free tier shows an HTML interstitial to browser-like requests; this header skips it
                // so JSON responses come through clean. Harmless on localhost (unknown header is ignored).
                client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
            }).AddHttpMessageHandler<MobileLoopbackFallbackHandler>();

            // Inner HTTP client (concrete) — the real network calls. IMobileApiClient is exposed as the
            // caching decorator below, so every consumer transparently gets offline read-through caching.
            builder.Services.AddHttpClient<MobileApiClient>(client =>
            {
                client.BaseAddress = new Uri(GetApiBaseUrl());
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
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
            // ── Development only ──────────────────────────────────────────────────────────────────────
            // Optional tunnel override so a physical device can reach the dev machine's API (ngrok is HTTPS).
            // Set to empty to fall back to the emulator/localhost hosts below.
            const string ApiBaseUrlOverride = "https://unwound-urban-senate.ngrok-free.dev/";
            if (!string.IsNullOrWhiteSpace(ApiBaseUrlOverride))
                return ApiBaseUrlOverride;

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
