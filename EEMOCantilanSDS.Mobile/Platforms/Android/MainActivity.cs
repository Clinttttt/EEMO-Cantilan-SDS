using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using EEMOCantilanSDS.Mobile.Services;
using Firebase.Messaging;

namespace EEMOCantilanSDS.Mobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private const int PostNotificationsRequestCode = 9001;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            CreateNotificationChannel();
            RequestPostNotificationsIfNeeded();
            FetchFcmToken();
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
