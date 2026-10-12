using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// The one place the Head Portal asks "what does THIS municipality call this operation, and does it list it?".
/// </summary>
/// <remarks>
/// <para>
/// Scoped per circuit like <see cref="BrandingState"/>, and loaded once from the caller's own tenant configuration
/// (<c>GET /api/operation-settings</c> scopes it by token), so nothing can cross a municipality boundary: a different user is a
/// different circuit and a different read.
/// </para>
/// <para>
/// A name is PRESENTATION. It never decides which operation something is, which receipt it uses or whether money may be collected;
/// callers pass the stable operation code (or a source key that maps to one) and a fallback that is the screen's established
/// wording. Where tenant configuration does not apply - the municipality never adopted it, or it has not loaded - the fallback is
/// returned unchanged, which is exactly what a legacy municipality has always seen.
/// </para>
/// <para>
/// Precedence on screens that already honour a Head-set policy name (Revenue Setup): that more specific, effective-dated name wins;
/// this service supplies the municipality's operation name where the screen would otherwise print the platform's standard wording.
/// Official report captions, remittance and accountability views and the classification setup keep their canonical names and do not use it.
/// </para>
/// </remarks>
public sealed class OperationNameService(IOperationSettingsApiClient api)
{
    private TenantOperationSettingsDto? _settings;
    private Task? _loading;

    /// <summary>Raised after the configuration is (re)loaded, so headers and cards already on screen can redraw.</summary>
    public event Action? Changed;

    /// <summary>True once the municipality's configuration has loaded and it has adopted it.</summary>
    public bool Adopted => _settings?.UsesConfiguration == true;

    public bool Loaded => _settings is not null;

    /// <summary>Loads the configuration once; concurrent callers share one request. A failed load leaves fallbacks in place and may be retried.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_settings is not null) return;
        var load = _loading ??= LoadAsync();
        try { await load; }
        finally
        {
            // A load that did not produce a configuration is forgotten, so the next caller retries instead of inheriting the failure.
            if (_settings is null && ReferenceEquals(_loading, load)) _loading = null;
        }
    }

    /// <summary>Forgets the configuration and reads it again. Called after the Head changes a name or listing.</summary>
    public async Task InvalidateAsync()
    {
        _settings = null;
        _loading = null;
        await EnsureLoadedAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var result = await api.GetAsync();
            _settings = result.IsSuccess ? result.Value : null;
        }
        catch
        {
            _settings = null;
        }

        Changed?.Invoke();
    }

    /// <summary>The municipality's name for an operation, or <paramref name="fallback"/> when configuration does not apply.</summary>
    public string NameFor(string? operationCode, string fallback)
    {
        if (!Adopted || operationCode is null) return fallback;
        var item = _settings!.Items.FirstOrDefault(i => i.OperationCode == operationCode);
        return string.IsNullOrWhiteSpace(item?.DisplayName) ? fallback : item.DisplayName;
    }

    /// <summary>
    /// The name to show for a revenue source key (an operation code or its classification code).
    /// A Head-set <paramref name="policyName"/> that is a real name always wins. Otherwise a municipality that has adopted tenant
    /// configuration sees ITS name for an operation this catalog covers. In every other case - not adopted, or a source the catalog
    /// does not cover (facility rent, Tabo, Fines...) - the screen keeps its established wording: <paramref name="officialLabel"/> if the
    /// caller has one, else the platform's standard source name.
    /// </summary>
    public string NameForSource(string? sourceKey, string? policyName = null, string? officialLabel = null)
    {
        var established = officialLabel ?? SourceNames.For(sourceKey, policyName);
        if (!string.IsNullOrWhiteSpace(policyName) && !SourceNames.LooksLikeCode(policyName) && policyName.Trim() != sourceKey)
            return SourceNames.For(sourceKey, policyName);
        if (!Adopted) return established;
        var entry = TenantOperationCatalog.FindBySourceKey(sourceKey);
        return entry is null ? established : NameFor(entry.Code, established);
    }

    /// <summary>
    /// Whether a source belongs in the ordinary lists of this municipality. Only an adopted municipality ever hides anything, only
    /// operations the catalog covers are hideable, and a source with recorded activity is never hidden by listing alone.
    /// </summary>
    public bool IsListed(string? sourceKey, bool hasActivity = false)
    {
        if (!Adopted || hasActivity) return true;
        var entry = TenantOperationCatalog.FindBySourceKey(sourceKey);
        if (entry is null) return true;
        return _settings!.Items.FirstOrDefault(i => i.OperationCode == entry.Code)?.IsListed != false;
    }
}
