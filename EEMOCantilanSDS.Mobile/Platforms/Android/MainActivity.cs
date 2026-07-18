using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using EEMOCantilanSDS.Mobile.Services;
using Firebase.Messaging;

namespace EEMOCantilanSDS.Mobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    // Collector-app bind link (Android App Link). Opens the app on https://app.stalltrack.site/a/{token};
    // AutoVerify requires /.well-known/assetlinks.json on that host (Unit 3) to open without a chooser.
    [IntentFilter(new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = "https", DataHost = "app.stalltrack.site", DataPathPrefix = "/a/",
        AutoVerify = true)]
    // Custom-scheme fallback: stalltrack://a/{token} (no domain verification needed).
    [IntentFilter(new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = "stalltrack", DataHost = "a")]
    public class MainActivity : MauiAppCompatActivity
    {
        private const int PostNotificationsRequestCode = 9001;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            CreateNotificationChannel();
            RequestPostNotificationsIfNeeded();
            FetchFcmToken();

            // Cold start via a bind link — capture the token before the Blazor login page initializes.
            HandleBindIntent(Intent);
        }

        // App already running (SingleTop) — a tapped bind link arrives here instead of a fresh OnCreate.
        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            Intent = intent;
            HandleBindIntent(intent);
        }

        // Hands a bind deep-link URI to the managed bridge; the login flow resolves it against the API.
        private static void HandleBindIntent(Intent? intent)
        {
            if (intent?.Action == Intent.ActionView && !string.IsNullOrWhiteSpace(intent.DataString))
            {
                MobileBindBridge.ReceiveDeepLink(intent.DataString);
            }
        }

        // Android 8+ (API 26): notifications must belong to a channel. Safe to call repeatedly.
        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            {
                return;
            }

            var channel = new NotificationChannel(
                StallTrackFirebaseMessagingService.ChannelId,
                "StallTrack Alerts",
                NotificationImportance.High)
            {
                Description = "Collection reminders and office messages."
            };

            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.CreateNotificationChannel(channel);
        }

        // Android 13+ (API 33): POST_NOTIFICATIONS is a runtime permission. Older versions grant it at install.
        private void RequestPostNotificationsIfNeeded()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            {
                return;
            }

            const string permission = Android.Manifest.Permission.PostNotifications;
            if (ContextCompat.CheckSelfPermission(this, permission) != Permission.Granted)
            {
                ActivityCompat.RequestPermissions(this, new[] { permission }, PostNotificationsRequestCode);
            }
        }

        // Obtain the current FCM token on launch. Rotations also arrive via OnNewToken in the messaging service.
        private void FetchFcmToken()
        {
            try
            {
                FirebaseMessaging.Instance.GetToken().AddOnCompleteListener(new TokenCompleteListener());
            }
            catch
            {
                // Best-effort: a failure here just means the token arrives later via OnNewToken.
            }
        }

        private sealed class TokenCompleteListener : Java.Lang.Object, Android.Gms.Tasks.IOnCompleteListener
        {
            public void OnComplete(Android.Gms.Tasks.Task task)
            {
                if (task.IsSuccessful)
                {
                    FcmTokenBridge.OnTokenRefreshed(task.Result?.ToString());
                }
            }
        }
    }
}
