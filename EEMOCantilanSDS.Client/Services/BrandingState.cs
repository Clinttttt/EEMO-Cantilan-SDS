using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Tenancy;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// Per-circuit cache of the signed-in LGU's branding (office name/acronym, seal), loaded once from
/// GET /api/municipalities/current/branding. Every accessor falls back to the current Cantilan literal,
/// so before the load returns, on a failed load, or for an LGU that hasn't set a value, the shell renders
/// exactly what it does today — Cantilan is byte-for-byte unchanged.
/// </summary>
public class BrandingState(IMunicipalitiesApiClient api)
{
    // Fallback defaults == the strings the UI currently hardcodes.
    public const string DefaultOfficeName = "Economic Enterprise & Management Office";
    public const string DefaultOfficeAcronym = "EEMO";
    public const string DefaultSealPath = "/images/LGU_CANTILAN_LOGO.jpg";
    // Neutral platform seal for a non-default LGU that hasn't uploaded its own — never Cantilan's.
    public const string NeutralSealPath = "/images/stalltrack-seal.png";
    public const string DefaultMunicipality = "Cantilan";
    public const string DefaultProvince = "Surigao del Sur";

    private MunicipalityBrandingDto? _branding;
    private Task? _loadTask;

    // The default tenant is Cantilan; before load (null) we treat as default so Cantilan is byte-for-byte
    // unchanged and other LGUs only briefly show the default before their branding resolves.
    public bool IsDefaultTenant =>
        _branding is null || string.Equals(_branding.Code, "CANTILAN", System.StringComparison.OrdinalIgnoreCase);

    public string OfficeName => Nonempty(_branding?.OfficeName, DefaultOfficeName);
    // A set acronym wins; Cantilan falls back to EEMO; any other LGU without an acronym falls back to its
    // own municipality name (never "EEMO").
    public string OfficeAcronym =>
        !string.IsNullOrWhiteSpace(_branding?.OfficeAcronym) ? _branding!.OfficeAcronym!
        : IsDefaultTenant ? DefaultOfficeAcronym : Municipality;
    // A set seal wins; otherwise Cantilan keeps its own logo and every other LGU gets the neutral seal.
    public string SealPath =>
        !string.IsNullOrWhiteSpace(_branding?.SealPath) ? _branding!.SealPath!
        : IsDefaultTenant ? DefaultSealPath : NeutralSealPath;
    public string Municipality => Nonempty(_branding?.Name, DefaultMunicipality);
    public string Province => Nonempty(_branding?.Province, DefaultProvince);

    /// <summary>Loads branding once per circuit; concurrent callers share the same in-flight task. A failed
    /// load leaves the fallbacks in place (never throws to the UI).</summary>
    public Task EnsureLoadedAsync() => _loadTask ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var result = await api.GetCurrentBrandingAsync();
            if (result.IsSuccess && result.Value is not null)
                _branding = result.Value;
        }
        catch
        {
            // Swallow — fallbacks remain; branding is presentation-only and must never break the shell.
        }
    }

    private static string Nonempty(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value!;
}
