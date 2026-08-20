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

    /// <summary>
    /// What stands in the seal's place for an LGU that has not uploaded one yet.
    ///
    /// <para>
    /// This used to be StallTrack's own seal, and the consequence was worse than a login page looking odd: <c>SealPath</c> is
    /// rendered at 31 places, and most of them are PRINTED - official reports, the collection receipt, the stallholder list, the
    /// payor's history. A municipality with no seal on file was therefore issuing documents with the VENDOR's mark on them, labelled
    /// as its own seal. A private company's emblem does not belong on a government document.
    /// </para>
    ///
    /// <para>
    /// A faint outline of a municipal hall, inline as a data URI so no consumer changes and no asset is added. It reads plainly as
    /// "no seal yet", which is the truth until onboarding collects one, and it scales cleanly in print because it is vector.
    /// Cantilan is unaffected: it keeps <see cref="DefaultSealPath"/>.
    /// </para>
    /// </summary>
    public const string WaitingSealPath =
        "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0i" +
        "bm9uZSIgc3Ryb2tlPSIjNmE4YWEwIiBzdHJva2Utd2lkdGg9IjEuNCIgc3Ryb2tlLWxpbmVjYXA9InJvdW5kIiBzdHJva2UtbGluZWpvaW49InJvdW5k" +
        "IiBvcGFjaXR5PSIwLjQ1Ij48cGF0aCBkPSJNMyAyMWgxOCIvPjxwYXRoIGQ9Ik01IDIxVjEwbDctNSA3IDV2MTEiLz48cGF0aCBkPSJNOSAyMXYtNmg2" +
        "djYiLz48L3N2Zz4=";
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
    /// <summary>
    /// The platform's DEFAULT municipality, from the branding record itself.
    ///
    /// <para>
    /// It used to compare the code to the literal "CANTILAN" - a tenant code deciding behaviour. The municipality row already
    /// carries the fact, so it is read rather than inferred. Before branding loads the answer is unknown, and true is kept
    /// deliberately: that is what it answered before, and the default LGU is the one whose pages would otherwise flicker.
    /// </para>
    /// </summary>
    public bool IsDefaultTenant => _branding is null || _branding.IsDefault;

    /// <summary>The signed-in LGU's tenant code (empty until branding loads / for the default). Used to
    /// build this LGU's per-account webhook URL.</summary>
    public string TenantCode => _branding?.TenantCode ?? string.Empty;

    public string OfficeName => Nonempty(_branding?.OfficeName, DefaultOfficeName);    // A set acronym wins; Cantilan falls back to EEMO; any other LGU without an acronym falls back to its
    // own municipality name (never "EEMO").
    public string OfficeAcronym =>
        !string.IsNullOrWhiteSpace(_branding?.OfficeAcronym) ? _branding!.OfficeAcronym!
        : IsDefaultTenant ? DefaultOfficeAcronym : Municipality;
    // A set seal wins; otherwise Cantilan keeps its own logo and every other LGU gets the waiting slot - never StallTrack's mark,
    // which is what this used to hand to 31 render sites, most of them printed documents.
    public string SealPath =>
        !string.IsNullOrWhiteSpace(_branding?.SealPath) ? _branding!.SealPath!
        : IsDefaultTenant ? DefaultSealPath : WaitingSealPath;

    /// <summary>
    /// Whether the seal on screen actually belongs to the municipality named beside it. For a screen that would rather omit the seal
    /// entirely than show a placeholder - a printed document, say - this is the question to ask.
    /// </summary>
    public bool HasOwnSeal => SealPath != WaitingSealPath;
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
    private string? _alignment;

    /// <summary>The office's default trio, used until an LGU sets its own.</summary>
    public IReadOnlyList<Signatory> DefaultSignatories => new[]
    {
        new Signatory("Prepared by", "Administrative Staff"),
        new Signatory("Reviewed by", $"Head, {OfficeAcronym} Office"),
        new Signatory("Received by", "Authorized Representative"),
    };

    /// <summary>Where the strip sits on the sheet. Stored with the lines; "left" unless the office asks otherwise.</summary>
    public string SignatoryAlignment => _alignment ?? ParseStored().Alignment;

    /// <summary>True when the office has deliberately chosen to print no signatory lines at all.</summary>
    public bool HasNoSignatories => Signatories.Count == 0;

    /// <summary>
    /// True when this LGU has set nothing of its own, so its sheets carry the office's default trio. Distinct from
    /// having chosen no lines: that is a choice, this is the absence of one, and only the second can be "restored".
    /// </summary>
    public bool SignatoriesAreOfficeDefault => _signatories is null && ParseStored().Lines is null;

    /// <summary>This LGU's signatory lines. Null storage falls back to the default trio; an empty array means none.</summary>
    public IReadOnlyList<Signatory> Signatories
    {
        get
        {
            if (_signatories is not null) return _signatories;
            return ParseStored().Lines ?? DefaultSignatories;
        }
    }

    /// <summary>
    /// Reads the stored value, which may be either shape:
    ///
    /// <list type="bullet">
    ///   <item><c>null</c> or unreadable — the office's default trio, so a sheet never loses its footer to a bad value.</item>
    ///   <item>a bare ARRAY — the lines, left-aligned. This is what was stored before alignment existed, so it must keep
    ///         meaning exactly what it meant then.</item>
    ///   <item>an OBJECT <c>{ "align": "...", "lines": [...] }</c> — the lines and where they sit.</item>
    /// </list>
    ///
    /// An empty <c>lines</c> array is "no signatories", which is why this returns null-for-default separately from an
    /// empty list: the two used to be the same value and could not be told apart.
    /// </summary>
    private (IReadOnlyList<Signatory>? Lines, string Alignment) ParseStored()
    {
        var json = _branding?.ReportSignatories;
        if (string.IsNullOrWhiteSpace(json)) return (null, "left");

        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        try
        {
            var trimmed = json!.TrimStart();

            if (trimmed.StartsWith('{'))
            {
                var stored = System.Text.Json.JsonSerializer.Deserialize<StoredSignatories>(json!, options);
                if (stored is not null)
                    return (stored.Lines ?? new List<Signatory>(),
                            string.Equals(stored.Align, "center", StringComparison.OrdinalIgnoreCase) ? "center" : "left");
            }
            else
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<Signatory>>(json!, options);
                if (parsed is not null) return (parsed, "left");
            }
        }
        catch
        {
            // A stored value we cannot read must never blank the footer of an official sheet.
        }

        return (null, "left");
    }

    private sealed record StoredSignatories(string? Align, List<Signatory>? Lines);

    /// <summary>
    /// Replaces this LGU's signatory lines and where they sit. An EMPTY list means the office wants no signatory lines
    /// at all; to go back to the office's default trio call <see cref="RestoreDefaultSignatoriesAsync"/>. The local copy
    /// is updated on success so every open document redraws with the new footer immediately.
    /// </summary>
    public async Task<string?> SaveSignatoriesAsync(
        ISettingsApiClient settingsApi, IReadOnlyList<Signatory> signatories, string alignment = "left")
    {
        var payload = signatories
            .Select(s => new Application.Command.Municipalities.SetReportSignatories.ReportSignatoryDto(s.Caption, s.Name))
            .ToList();

        // Alignment rides with the lines rather than in a column of its own, so one save writes one value and the two
        // can never disagree about what the footer should look like.
        var result = await settingsApi.SaveReportSignatoriesAsync(payload, alignment);
        if (!result.IsSuccess)
            return string.IsNullOrWhiteSpace(result.Error) ? "Couldn't save the signatories." : result.Error;

        _signatories = signatories;
        _alignment = alignment;
        return null;
    }

    /// <summary>
    /// Clears this LGU's own lines so its sheets carry the office's default trio again. Distinct from saving an empty
    /// list, which means "print no signatory lines".
    /// </summary>
    public async Task<string?> RestoreDefaultSignatoriesAsync(ISettingsApiClient settingsApi)
    {
        var result = await settingsApi.SaveReportSignatoriesAsync(null, null);
        if (!result.IsSuccess)
            return string.IsNullOrWhiteSpace(result.Error) ? "Couldn't restore the default signatories." : result.Error;

        _signatories = null;
        _alignment = null;
        if (_branding is not null) _branding = _branding with { ReportSignatories = null };

        return null;
    }
}
