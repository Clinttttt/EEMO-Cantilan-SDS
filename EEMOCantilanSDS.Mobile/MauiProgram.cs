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
            builder.Services.AddSingleton<EEMOCantilanSDS.Mobile.Services.IConnectivityMonitor, EEMOCantilanSDS.Mobile.Platform.MauiConnectivityMonitor>();
            builder.Services.AddSingleton<EEMOCantilanSDS.Mobile.Services.IPendingOperationStore>(
                _ => new EEMOCantilanSDS.Mobile.Services.PendingOperationStore(FileSystem.AppDataDirectory));
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

            builder.Services.AddHttpClient<IMobileApiClient, MobileApiClient>(client =>
            {
                client.BaseAddress = new Uri(GetApiBaseUrl());
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
            })
            .AddHttpMessageHandler<MobileLoopbackFallbackHandler>()
            .AddHttpMessageHandler<MobileRefreshTokenDelegatingHandler>()
            .AddHttpMessageHandler<MobileAuthorizationDelegatingHandler>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }

        internal static string GetApiBaseUrl()
        {
            // ── Physical-device testing over the internet (ngrok / dev tunnel) ──────────────
            // Set this to your public HTTPS tunnel URL (WITH trailing slash) to run the app on a real
            // phone pointed at your local API. Leave it "" for normal localhost dev (Windows + emulator).
            // NOTE: the ngrok free plan gives a NEW URL every restart — update this each session.
            /*   const string ApiBaseUrlOverride = "https://unwound-urban-senate.ngrok-free.dev/";
               if (!string.IsNullOrWhiteSpace(ApiBaseUrlOverride))
                   return ApiBaseUrlOverride;*/

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
        }
    }
}
