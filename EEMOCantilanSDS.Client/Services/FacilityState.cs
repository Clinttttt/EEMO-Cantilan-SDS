using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// Per-circuit cache of the signed-in LGU's ACTUAL facilities, loaded once from
/// GET /api/facilities/summaries (tenant-scoped). Facility selectors/tabs should render from this instead
/// of the full <see cref="FacilityCode"/> enum, so an LGU never sees tabs for facilities it doesn't operate.
///
/// The list of facilities is data-driven per tenant. The rental-vs-transaction split is a platform constant:
/// the eight <see cref="FacilityCode"/> values have fixed billing conventions (NPM/TCC/NCC/BBQ/ICE are
/// recurring per-payor rentals; SLH/TRM/TPM are per-service transaction facilities). Only WHICH of these an
/// LGU has varies — and that comes from the API.
///
/// Fallback (before load / on failure) is the full set, so Cantilan (which has all eight) is byte-for-byte
/// unchanged and a transient failure never hides Cantilan's tabs.
/// </summary>
public class FacilityState(IFacilitiesApiClient api)
{
    private static readonly FacilityCode[] AllCodes =
        { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE, FacilityCode.SLH, FacilityCode.TRM, FacilityCode.TPM };

    private static readonly HashSet<FacilityCode> RentalCodes =
        new() { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE,
                FacilityCode.Custom1, FacilityCode.Custom2, FacilityCode.Custom3, FacilityCode.Custom4, FacilityCode.Custom5 };

    private IReadOnlyList<FacilitySidebarSummaryDto>? _facilities;
    private Task? _loadTask;

    /// <summary>Loads the tenant's facilities once per circuit; concurrent callers share the in-flight task.</summary>
    public Task EnsureLoadedAsync() => _loadTask ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var now = PhilippineTime.Now;
            var result = await api.GetFacilitySummariesAsync(now.Year, now.Month);
            if (result.IsSuccess && result.Value is not null)
                _facilities = result.Value;
        }
        catch
        {
            // Presentation-only; leave the fallback in place and never break the page.
        }
    }

    /// <summary>The LGU's facilities in canonical order (fallback = all eight until loaded).</summary>
    public IReadOnlyList<FacilityCode> All =>
        _facilities is { Count: > 0 }
            ? _facilities.Select(f => f.Code).OrderBy(c => (int)c).ToList()
            : AllCodes;

    /// <summary>The LGU's recurring-rental facilities (managed per-payor: collection, closed accounts, vendors).</summary>
    public IReadOnlyList<FacilityCode> Rental =>
        All.Where(RentalCodes.Contains).ToList();

    /// <summary>The LGU's per-service transaction facilities (SLH/TRM/TPM).</summary>
    public IReadOnlyList<FacilityCode> Transaction =>
        All.Where(c => !RentalCodes.Contains(c)).ToList();

    public static bool IsRental(FacilityCode code) => RentalCodes.Contains(code);

    // Canonical Cantilan display names/acronyms — used as the fallback before the tenant catalog loads (and
    // for any facility not in the catalog), so the default LGU renders byte-for-byte identically to the old
    // hardcoded values and never flickers a bare code. A loaded tenant catalog always wins.
    private static readonly Dictionary<FacilityCode, (string Name, string ShortName)> Canonical = new()
    {
        [FacilityCode.NPM] = ("New Public Market", "NPM"),
        [FacilityCode.TCC] = ("Tampak Commercial Center", "TCC"),
        [FacilityCode.NCC] = ("New Commercial Center", "NCC"),
        [FacilityCode.BBQ] = ("Barbecue Stand", "BBQ"),
        [FacilityCode.ICE] = ("Iceplant", "ICE"),
        [FacilityCode.SLH] = ("Slaughterhouse", "SLH"),
        [FacilityCode.TRM] = ("Transport Terminal", "TRM"),
        [FacilityCode.TPM] = ("Tabo-an Public Market", "TPM"),
    };

    /// <summary>The tenant's own name for a facility (fallback = the canonical Cantilan name, then the code).</summary>
    public string NameOf(FacilityCode code) =>
        _facilities?.FirstOrDefault(f => f.Code == code)?.Name
        ?? (Canonical.TryGetValue(code, out var c) ? c.Name : code.ToString());

    /// <summary>The tenant's own short acronym for a facility (fallback = the canonical Cantilan acronym, then the code).</summary>
    public string ShortNameOf(FacilityCode code) =>
        _facilities?.FirstOrDefault(f => f.Code == code)?.ShortName
        ?? (Canonical.TryGetValue(code, out var c) ? c.ShortName : code.ToString());

    // ── URL slugs ────────────────────────────────────────────────────────────────────────────────────
    // Custom facilities expose a friendly acronym slug (/facility/fe) instead of the internal slot code
    // (/facility/custom1). Canonical facilities keep their fixed code as the slug, so Cantilan's URLs are
    // byte-for-byte unchanged. A custom acronym can NEVER shadow a canonical route or another custom slot,
    // and it always has a stable fallback (the slot code) that still resolves — so nothing breaks even when
    // the acronym is blank, duplicated, or renamed.

    // Slugs a custom acronym must not take (canonical codes + the reserved slot literals).
    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "npm", "tcc", "ncc", "bbq", "ice", "slh", "trm", "tpm",
        "custom1", "custom2", "custom3", "custom4", "custom5",
    };

    private static bool IsCustom(FacilityCode code) => (int)code >= (int)FacilityCode.Custom1;

    // Lowercase, alphanumeric only ("FE" → "fe", "F-E 2" → "fe2"); empty when the acronym has no usable chars.
    private static string Sanitize(string? s) =>
        new string((s ?? string.Empty).ToLowerInvariant().Where(ch => (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')).ToArray());

    /// <summary>
    /// URL slug for a facility from a raw (code, shortName) pair plus its peers — for components that already
    /// hold the tenant summaries (sidebar, dashboard) without touching the per-circuit cache. Canonical → the
    /// fixed code; custom → the sanitized acronym, falling back to the slot code when the acronym is empty,
    /// reserved, or already claimed by a lower-numbered custom slot (guarantees a unique, stable slug).
    /// </summary>
    public static string SlugFor(FacilityCode code, string? shortName, IEnumerable<(FacilityCode Code, string? ShortName)>? peers = null)
    {
        if (!IsCustom(code)) return code.ToString().ToLowerInvariant();
        var slug = Sanitize(shortName);
        if (slug.Length == 0 || ReservedSlugs.Contains(slug)) return code.ToString().ToLowerInvariant();
        if (peers is not null)
            foreach (var p in peers)
                if (IsCustom(p.Code) && (int)p.Code < (int)code && Sanitize(p.ShortName) == slug)
                    return code.ToString().ToLowerInvariant();
        return slug;
    }

    /// <summary>URL slug for a facility using the loaded tenant catalog.</summary>
    public string SlugOf(FacilityCode code) =>
        SlugFor(code, ShortNameOf(code), All.Select(c => (Code: c, ShortName: (string?)ShortNameOf(c))));

    /// <summary>
    /// Resolves a URL slug back to a facility code. Accepts the literal enum name (custom1/npm — so old
    /// bookmarks keep working) AND a custom facility's acronym (fe). Returns false when nothing matches.
    /// </summary>
    public bool TryResolveSlug(string? slug, out FacilityCode code)
    {
        code = default;
        if (string.IsNullOrWhiteSpace(slug)) return false;
        if (Enum.TryParse(slug, ignoreCase: true, out FacilityCode parsed) && Enum.IsDefined(parsed))
        {
            code = parsed;
            return true;
        }
        var norm = Sanitize(slug);
        foreach (var c in All.Where(IsCustom))
            if (string.Equals(SlugOf(c), norm, StringComparison.OrdinalIgnoreCase))
            {
                code = c;
                return true;
            }
        return false;
    }
}
