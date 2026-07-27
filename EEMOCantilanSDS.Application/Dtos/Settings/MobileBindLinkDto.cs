namespace EEMOCantilanSDS.Application.Dtos.Settings;

/// <summary>
/// The LGU's collector-app links: the bind link that points a fresh install at this municipality, and the
/// app download link.
/// <para>
/// The bind link also carries a QR code as a self-contained <c>data:image/png;base64,…</c> URI so a collector
/// can scan it with their phone camera instead of typing a long URL. It is generated per request, never
/// stored: the bind link can be rotated at any moment, and a cached QR would send collectors to a dead token.
/// Optional so a client that does not know about it still binds correctly.
/// </para>
/// </summary>
public record MobileBindLinkDto(
    string BindToken,
    string BindUrl,
    string DownloadUrl,
    string? BindQrCodeDataUri = null);
