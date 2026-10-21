namespace EEMOCantilanSDS.Mobile.Services;

// Unpackaged Windows builds share MAUI preferences even when ApplicationId differs.
internal static class LocalAcceptanceProfile
{
    internal static bool Enabled
    {
        get
        {
#if DEBUG && WINDOWS
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STALLTRACK_LOCAL_API_URL"));
#else
            return false;
#endif
        }
    }
    internal static string Key(string key) => Enabled ? "npm_amount_acceptance_" + key : key;
    internal static string DataDirectory => Enabled
        ? Path.Combine(FileSystem.AppDataDirectory, "npm-amount-acceptance") : FileSystem.AppDataDirectory;
}
