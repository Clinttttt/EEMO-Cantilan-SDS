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

    /// <summary>
    /// True only once an LGU's branding has actually come back. Distinct from "the load finished": every accessor below falls
    /// back to Cantilan's literals, which is right for the shell (Cantilan stays byte-for-byte unchanged) but wrong anywhere
    /// a screen would rather show nothing than the wrong municipality's identity — the change-password screen gates its brand
    /// panel on this.
    /// </summary>
    public bool Resolved => _branding is not null;

    /// <summary>What was loaded, if anything. Exposed so a page can carry it across the prerender boundary.</summary>
    public MunicipalityBrandingDto? Current => _branding;

    /// <summary>
    /// Accepts branding the caller already holds, instead of fetching it again.
    ///
    /// <para>
    /// Prerendering is enabled at the interactive root, so a component initialises TWICE: once for the static response and
    /// again when the circuit starts. A page that persists what the first pass fetched (see <c>[PersistentState]</c>, as
    /// <c>Routes.razor</c> does for the setup check) can hand it back here, which keeps the second pass from re-fetching and
    /// from flashing its fallbacks on screen before the answer returns.
    /// </para>
    ///
    /// <para>The load task is marked complete so a later <see cref="EnsureLoadedAsync"/> is a no-op.</para>
    /// </summary>
    public void Apply(MunicipalityBrandingDto branding)
    {
        _branding = branding;
        _loadTask = Task.CompletedTask;
    }

    // The default tenant is Cantilan; before load (null) we treat as default so Cantilan is byte-for-byte
    // unchanged and other LGUs only briefly show the default before their branding resolves.
    public bool IsDefaultTenant =>
        _branding is null || string.Equals(_branding.Code, "CANTILAN", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>The signed-in LGU's tenant code (empty until branding loads / for the default). Used to
    /// build this LGU's per-account webhook URL.</summary>
    public string TenantCode => _branding?.TenantCode ?? string.Empty;

    public string OfficeName => Nonempty(_branding?.OfficeName, DefaultOfficeName);    // A set acronym wins; Cantilan falls back to EEMO; any other LGU without an acronym falls back to its
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

    // ── Report signatories ────────────────────────────────────────────────────────────────────────────
    // The lines an official sheet carries at its foot. Stored per LGU as JSON; when unset (or unreadable)
    // the office's standard trio is used, so every sheet keeps exactly the footer it has today.

    /// <summary>One signatory line: the caption above the rule and the name beneath it.</summary>
    public record Signatory(string Caption, string Name);

    private IReadOnlyList<Signatory>? _signatories;

    /// <summary>The office's default trio, used until an LGU sets its own.</summary>
    public IReadOnlyList<Signatory> DefaultSignatories => new[]
    {
        new Signatory("Prepared by", "Administrative Staff"),
        new Signatory("Reviewed by", $"Head, {OfficeAcronym} Office"),
        new Signatory("Received by", "Authorized Representative"),
    };

    /// <summary>This LGU's signatory lines, falling back to the default trio.</summary>
    public IReadOnlyList<Signatory> Signatories
    {
        get
        {
            if (_signatories is not null) return _signatories;

            var json = _branding?.ReportSignatories;
            if (string.IsNullOrWhiteSpace(json)) return DefaultSignatories;

            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<Signatory>>(json!,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed is { Count: > 0 })
                    return _signatories = parsed;
            }
            catch
            {
                // A stored value we cannot read must never blank the footer of an official sheet.
            }

            return DefaultSignatories;
        }
    }

    /// <summary>
    /// Replaces this LGU's signatory lines. An empty list restores the office's default trio. The local copy
    /// is updated on success so every open document redraws with the new footer immediately.
    /// </summary>
    public async Task<string?> SaveSignatoriesAsync(
        ISettingsApiClient settingsApi, IReadOnlyList<Signatory> signatories)
    {
        var payload = signatories
            .Select(s => new Application.Command.Municipalities.SetReportSignatories.ReportSignatoryDto(s.Caption, s.Name))
            .ToList();

        var result = await settingsApi.SaveReportSignatoriesAsync(payload);
        if (!result.IsSuccess)
            return string.IsNullOrWhiteSpace(result.Error) ? "Couldn't save the signatories." : result.Error;

        _signatories = signatories.Count == 0 ? null : signatories;
        if (signatories.Count == 0 && _branding is not null)
            _branding = _branding with { ReportSignatories = null };

        return null;
    }
}
