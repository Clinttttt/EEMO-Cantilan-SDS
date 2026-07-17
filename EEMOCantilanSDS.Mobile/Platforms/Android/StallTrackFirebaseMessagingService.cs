using Android.App;
using Android.Content;
using AndroidX.Core.App;
using EEMOCantilanSDS.Mobile.Services;
using Firebase.Messaging;

// NOTE: namespace is intentionally the app root (NOT "...Platforms.Android") to avoid clashing with the
// global "Android" namespace used throughout this file.
namespace EEMOCantilanSDS.Mobile;

/// <summary>
/// Android Firebase Cloud Messaging service. The OS instantiates this (outside DI) to hand off token
/// refreshes and incoming messages. Token changes are forwarded to <see cref="FcmTokenBridge"/>; incoming
/// messages are surfaced as a system notification (with sound) so they appear in the tray even when the app
/// is backgrounded or closed — the same behaviour as a normal messaging app.
/// </summary>
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public sealed class StallTrackFirebaseMessagingService : FirebaseMessagingService
{
    /// <summary>Notification channel id — must match the manifest default-channel meta-data and MainActivity's channel.</summary>
    public const string ChannelId = "stalltrack_default_channel";

    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        FcmTokenBridge.OnTokenRefreshed(token);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        // Prefer the notification payload; fall back to data payload (data-only messages).
        var title = message.GetNotification()?.Title;
        var body = message.GetNotification()?.Body;

        if (string.IsNullOrEmpty(title) && message.Data is not null)
        {
            message.Data.TryGetValue("title", out title);
            message.Data.TryGetValue("body", out body);
        }

        ShowNotification(
            string.IsNullOrWhiteSpace(title) ? "StallTrack" : title!,
            body ?? string.Empty);
    }

    private void ShowNotification(string title, string body)
    {
        var context = Android.App.Application.Context;

        // Tapping the notification opens (or brings forward) the app.
        PendingIntent? contentIntent = null;
        var launchIntent = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        if (launchIntent is not null)
        {
            launchIntent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            contentIntent = PendingIntent.GetActivity(
                context, 0, launchIntent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        }

        // Resolve the branded monochrome notification icon by name (safe fallback if not found).
#pragma warning disable CS0618 // GetIdentifier is the reliable cross-version lookup here.
        var smallIcon = context.Resources?.GetIdentifier("ic_stat_stalltrack", "drawable", context.PackageName) ?? 0;
#pragma warning restore CS0618

        var builder = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(title)
            .SetContentText(body)
            .SetSmallIcon(smallIcon != 0 ? smallIcon : Android.Resource.Drawable.SymDefAppIcon)
            .SetColor(unchecked((int)0xFFC8A84B)) // gold tint, consistent with the app brand
            .SetAutoCancel(true)
            .SetPriority((int)NotificationPriority.High);

        if (contentIntent is not null)
        {
            builder.SetContentIntent(contentIntent);
        }

        if (!string.IsNullOrEmpty(body))
        {
            builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(body));
        }

        var manager = NotificationManagerCompat.From(context);
        var notificationId = (int)(Java.Lang.JavaSystem.CurrentTimeMillis() & 0x0fffffff);
        manager.Notify(notificationId, builder.Build());
    }
}
