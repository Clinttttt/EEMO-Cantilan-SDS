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

    /// <summary>
    /// Raised when the office's facility record has been re-read, so anything already on screen can show the
    /// new wording without the page being reloaded. Renaming a facility, or naming its market areas, used to
    /// reach only the screens rendered AFTER the save: the catalogue is cached once per circuit, so the Public
    /// Market page and the sidebar kept the old name until the office reloaded the browser.
    /// </summary>
    public event Action? Changed;

    /// <summary>Loads the tenant's facilities once per circuit; concurrent callers share the in-flight task.</summary>
    public Task EnsureLoadedAsync() => _loadTask ??= LoadAsync();

    /// <summary>
    /// Re-reads the office's facility record and tells the screen. Call this after the office changes a
    /// facility's name, acronym or market-area labels. A failed re-read leaves the previous names in place
    /// rather than emptying the catalogue, which is what <see cref="LoadAsync"/> already guarantees.
    /// </summary>
    public async Task ReloadAsync()
    {
        var refreshed = LoadAsync();
        _loadTask = refreshed;
        await refreshed;
        Changed?.Invoke();
    }

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

    // Cantilan's names are NOT kept here any more. They were the display fallback for every tenant, which is how another
    // municipality's office name reached Madrid's reports; the fallback is now the facility CODE, and Cantilan's own names
    // reach its pages the same way every other LGU's do - from its catalog. The canonical defaults still exist once, in
    // the Domain's FacilityCatalog, where seeding uses them.

    /// <summary>
    /// The tenant's own name for a facility.
    ///
    /// <para>
    /// Falls back to the CODE when this LGU has no record for it - not to Cantilan's name. Madrid's market is the Madrid
    /// Public Market; calling it the New Public Market on Madrid's own reports states something untrue about their office.
    /// The one exception is the LGU those names belong to, whose catalog carries them anyway.
    /// </para>
    /// </summary>
    public string NameOf(FacilityCode code)
    {
        var own = _facilities?.FirstOrDefault(f => f.Code == code)?.Name;
        return string.IsNullOrWhiteSpace(own) ? code.ToString() : own!;
    }

    /// <summary>
    /// The tenant's own acronym for a facility.
    ///
    /// <para>
    /// When the LGU has a facility but no acronym recorded, one is DERIVED from its own name - "Madrid Public Market"
    /// becomes MPM - by the same rule the activation console uses, so a derived value agrees with what activation would
    /// have stored. Only a facility this LGU has no record of falls back to the bare code.
    /// </para>
    /// </summary>
    public string ShortNameOf(FacilityCode code)
    {
        var facility = _facilities?.FirstOrDefault(f => f.Code == code);
        if (facility is null) return code.ToString();

        if (!string.IsNullOrWhiteSpace(facility.ShortName)) return facility.ShortName;

        var derived = DeriveAcronym(facility.Name);
        return string.IsNullOrWhiteSpace(derived) ? code.ToString() : derived;
    }

    /// <summary>
    /// An acronym from a facility's own name: initials of its significant words, or the first three letters of a
    /// single-word name. Mirrors the activation console's own derivation so the two cannot disagree.
    /// </summary>
    private static string DeriveAcronym(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "of", "the", "and", "for", "a", "an", "de", "del", "y" };

        var words = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => new string(w.Where(char.IsLetter).ToArray()))
            .Where(w => w.Length > 0 && !stop.Contains(w))
            .ToList();

        if (words.Count >= 2)
        {
            var initials = string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
            return initials.Length > 4 ? initials[..4] : initials;
        }

        return words.Count == 1
            ? words[0][..Math.Min(3, words[0].Length)].ToUpperInvariant()
            : string.Empty;
    }

    // Canonical market-section labels — the logical key used across the app. Custom tenant labels only
    // change what is DISPLAYED (via SectionLabelOf); the enum + these canonical strings remain the key.
    private static readonly Dictionary<MarketSection, string> CanonicalSection = new()
    {
        [MarketSection.VegetableArea] = "Vegetable Area",
        [MarketSection.FishSection] = "Fish Area",
        [MarketSection.MeatSection] = "Meat Area",
    };

    /// <summary>Canonical (enum-keyed) section label — always the same string used for grouping/filtering.</summary>
    public static string CanonicalSectionLabel(MarketSection section) =>
        CanonicalSection.TryGetValue(section, out var c) ? c : section.ToString();

    /// <summary>
    /// The tenant's DISPLAY label for a market section (e.g. "Gulayan"), or the canonical label when none
    /// is configured. Purely presentational — never use this as a key for grouping/filtering/fish-fee logic.
    /// </summary>
    public string SectionLabelOf(FacilityCode code, MarketSection section)
    {
        var f = _facilities?.FirstOrDefault(x => x.Code == code);
        var custom = section switch
        {
            MarketSection.VegetableArea => f?.VegetableSectionLabel,
            MarketSection.FishSection => f?.FishSectionLabel,
            MarketSection.MeatSection => f?.MeatSectionLabel,
            _ => null
        };
        return !string.IsNullOrWhiteSpace(custom) ? custom! : CanonicalSectionLabel(section);
    }

    /// <summary>Maps a canonical section label back to its enum (for resolving a display label from a key string).</summary>
    public static MarketSection? SectionFromCanonical(string? canonical) => canonical switch
    {
        "Vegetable Area" => MarketSection.VegetableArea,
        "Fish Area" => MarketSection.FishSection,
        "Meat Area" => MarketSection.MeatSection,
        _ => null
    };

    /// <summary>Display label for a facility+canonical-section-key string (keeps the key, shows the tenant name).</summary>
    public string SectionLabelOf(FacilityCode code, string canonicalKey)
        => SectionFromCanonical(canonicalKey) is { } s ? SectionLabelOf(code, s) : canonicalKey;

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
