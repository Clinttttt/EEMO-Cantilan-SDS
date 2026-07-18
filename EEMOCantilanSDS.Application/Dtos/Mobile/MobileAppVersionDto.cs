namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// Latest published collector-app version (anonymous). The app compares its installed Android versionCode
/// against these: <c>LatestVersionCode</c> &gt; installed ⇒ an update is available; <c>MinSupportedVersionCode</c>
/// &gt; installed ⇒ the update is mandatory. Side-loaded APKs can't self-update silently, so the app prompts
/// and opens <c>ApkUrl</c> for the user to install.
/// </summary>
public record MobileAppVersionDto(
    int LatestVersionCode,
    string LatestVersion,
    int MinSupportedVersionCode,
    string ApkUrl,
    string? Notes);
