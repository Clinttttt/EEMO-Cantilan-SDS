namespace EEMOCantilanSDS.Mobile.Presentation;

/// <summary>
/// What the launch screen says, and where it goes, decided by elapsed time and the restore result only. There is no
/// artificial minimum: a fast restore navigates at once. A status line appears only once the wait is noticeable, and
/// changes at most once, so a screen reader is not spammed and a normal start never looks slower than it is.
/// </summary>
public static class LaunchState
{
    /// <summary>Below this the brand alone is shown: most restores finish before anything needs saying.</summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(400);

    /// <summary>After this the line says what is actually happening.</summary>
    public static readonly TimeSpan LongWait = TimeSpan.FromMilliseconds(2500);

    public const string Starting = "Starting securely…";
    public const string Restoring = "Restoring your session…";

    public static string? StatusFor(TimeSpan elapsed) =>
        elapsed < QuietPeriod ? null : elapsed < LongWait ? Starting : Restoring;

    /// <summary>A restored session opens Today's Work; anything else — including a failed or expired restore — signs in.</summary>
    public static string RouteFor(bool restored) => restored ? "/menu" : "/login";
}
