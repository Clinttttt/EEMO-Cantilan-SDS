namespace EEMOCantilanSDS.Application.Dtos.Settings;

/// <summary>The Head's collector-app links: the LGU-scoped bind link (to share with collectors) and the
/// generic app download link.</summary>
public record MobileBindLinkDto(string BindToken, string BindUrl, string DownloadUrl);
