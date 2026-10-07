namespace Microsoft.Maui.ApplicationModel;

// Menu's update download is a native boundary; these tests never launch an APK/browser.
public sealed class Launcher
{
    public static Launcher Default { get; } = new();
    public Task OpenAsync(string uri) => throw new NotSupportedException("Native launch is outside component tests.");
}
