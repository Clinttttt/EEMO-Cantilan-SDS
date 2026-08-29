using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Queries.Settings.GetSystemSettings;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

public class GetSystemSettingsQueryHandlerTests
{
    // Office identity (name + province) is now sourced from the default Municipality record. The seeded
    // Cantilan values equal the OfficeProfile constants, so the assertions below still hold — proving the
    // record-sourcing changes nothing that is displayed.
    // Office identity (label, name, province, receipts) is now sourced from the CURRENT tenant's Municipality
    // record (falling back to the default LGU). The seeded Cantilan values equal the OfficeProfile constants,
    // so the assertions below still hold. Facilities are filtered to the tenant's actual facilities; the fake
    // facility repo returns all eight so Cantilan's list is unchanged.
    private static readonly GetSystemSettingsQueryHandler Handler =
        new(new FakeMunicipalityRepository(Municipality.Create(
                "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true)),
            new FakeFacilityRepository(FacilityCatalog.AllCodes),
            CacheTestDoubles.Tenant,
            CacheTestDoubles.FeeRateResolver,
            new FixedClock(DateTime.UtcNow),
            CacheTestDoubles.TpmMarketDay);

    [Fact]
    public async Task Returns_values_sourced_from_the_live_domain_constants()
    {
        var result = await Handler.Handle(new GetSystemSettingsQuery("Production"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;

        // Security policy mirrors DomainRules — guards against drift.
        Assert.Equal(DomainRules.AccessTokenMinutes, dto.Security.AccessTokenMinutes);
        Assert.Equal(DomainRules.RefreshTokenDays, dto.Security.RefreshTokenDays);
        Assert.Equal(DomainRules.MaxFailedLoginAttempts, dto.Security.MaxFailedLoginAttempts);
        Assert.Equal(DomainRules.LockoutMinutes, dto.Security.LockoutMinutes);
        Assert.Equal(new[] { "Head", "Admin", "Collector", "Payor" }, dto.Security.Roles);

        // Collection rules mirror DomainRules.
        Assert.Equal(DomainRules.DelinquentThresholdMonths, dto.Collection.DelinquentThresholdMonths);
        Assert.Equal(1, dto.Collection.ArrearsMinMonths);
        Assert.Equal(DomainRules.DelinquentThresholdMonths - 1, dto.Collection.ArrearsMaxMonths);
        Assert.Equal(DomainRules.PaymentHistoryMonths, dto.Collection.DelinquencyWindowMonths);
        Assert.Equal(DomainRules.ExpiringSoonMonths, dto.Collection.ContractExpiryWarningMonths);

        // Office + system identity.
        Assert.Equal(OfficeProfile.Municipality, dto.Office.Municipality);
        Assert.Equal(OfficeProfile.Province, dto.Office.Province);
        Assert.Equal(AppInfo.Name, dto.System.ApplicationName);
        Assert.Equal(AppInfo.Version, dto.System.Version);
        Assert.Equal("Production", dto.System.Environment);
    }

    [Fact]
    public async Task Returns_all_eight_facilities_with_fixed_rates_from_FeeRates()
    {
        var result = await Handler.Handle(new GetSystemSettingsQuery("Development"), CancellationToken.None);

        var dto = result.Value!;
        Assert.Equal(8, dto.Facilities.Count);

        var npm = Assert.Single(dto.Facilities, f => f.Code == "NPM");
        Assert.Contains($"₱{FeeRates.NpmDailyFee:0}/day", npm.Rate);
        Assert.Contains($"₱{FeeRates.NpmFishFeePerKilo:0}/kg", npm.Rate);

        var slh = Assert.Single(dto.Facilities, f => f.Code == "SLH");
        Assert.Contains($"₱{FeeRates.SlhHogTotalPerHead:0}", slh.Rate);
        Assert.Contains($"₱{FeeRates.SlhLargeTotalPerHead:0}", slh.Rate);

        var tpm = Assert.Single(dto.Facilities, f => f.Code == "TPM");
        Assert.Equal($"₱{FeeRates.TpmVendorFee:0}/vendor", tpm.Rate);

        var trm = Assert.Single(dto.Facilities, f => f.Code == "TRM");
        Assert.Equal($"₱{FeeRates.TrmTripFee:0}/trip", trm.Rate);
    }

    [Fact]
    public async Task Includes_head_added_custom_facilities_as_monthly_rental()
    {
        // A tenant with the 8 canonical facilities PLUS one Head-added custom facility (Custom1).
        var handler = new GetSystemSettingsQueryHandler(
            new FakeMunicipalityRepository(Municipality.Create(
                "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true)),
            new FakeFacilityRepository(FacilityCatalog.AllCodes.Append(FacilityCode.Custom1)),
            CacheTestDoubles.Tenant,
            CacheTestDoubles.FeeRateResolver,
            new FixedClock(DateTime.UtcNow),
            CacheTestDoubles.TpmMarketDay);

        var result = await handler.Handle(new GetSystemSettingsQuery("Development"), CancellationToken.None);
        var dto = result.Value!;

        Assert.Equal(9, dto.Facilities.Count);                                  // 8 canonical + 1 custom
        var custom = Assert.Single(dto.Facilities, f => f.Code == "Custom1");    // code = enum name (client resolves the acronym)
        Assert.Equal("Monthly rental", custom.Model);
        Assert.Equal("Monthly", custom.Cadence);
    }

    // ── What the page states once an office prices part of its market apart ───────────────────────
    //
    // The page stated one line: the month, the daily fee it is collected in, and the per-kilo fee. An office that priced an
    // AREA apart, or one of its OWN sections, read that line and saw nothing of it — the figure it had entered on another
    // screen was absent from the page that states its rules. Recorded as a gap for the areas on 2026-08-23; the section
    // fees joined it on 2026-08-29. Appended, never rewritten, so an office that prices nothing apart reads what it always
    // read, which the first test below holds.

    private static async Task<string> NpmRateLineAsync(
        IFeeRateResolver rates, Facility? npm = null, IReadOnlyList<NpmCustomSectionDto>? sections = null)
    {
        var handler = new GetSystemSettingsQueryHandler(
            new FakeMunicipalityRepository(Municipality.Create(
                "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "cantilan-sds", isDefault: true)),
            new FakeFacilityRepository(FacilityCatalog.AllCodes)
            {
                Npm = npm,
                Sections = sections ?? new List<NpmCustomSectionDto>(),
            },
            CacheTestDoubles.Tenant,
            rates,
            new FixedClock(DateTime.UtcNow),
            CacheTestDoubles.TpmMarketDay);

        var result = await handler.Handle(new GetSystemSettingsQuery("Test"), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!.Facilities.Single(f => f.Code == "NPM").Rate;
    }

    /// <summary>A market whose three areas the office has named in its own words.</summary>
    private static Facility MarketNaming(string vegetable, string fish, string meat)
    {
        var npm = Facility.Create(FacilityCode.NPM, "Madrid Public Market", "MPM");
        npm.SetSectionLabels(vegetable, fish, meat);
        return npm;
    }

    private static IFeeRateResolver Rates(params FeeRateEntry[] entries) => new StubRateResolver(new FeeRateSnapshot(entries));

    private sealed class StubRateResolver(FeeRateSnapshot snapshot) : IFeeRateResolver
    {
        public Task<FeeRateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    [Fact]
    public async Task An_office_that_prices_nothing_apart_reads_what_it_always_read()
    {
        var line = await NpmRateLineAsync(CacheTestDoubles.FeeRateResolver);

        Assert.Contains("/month, collected at", line);
        // Nothing is appended after the daily (and fish) figures.
        Assert.DoesNotContain("/day ·", line);
    }

    [Fact]
    public async Task An_area_priced_apart_is_stated_in_the_offices_own_word_for_it()
    {
        var line = await NpmRateLineAsync(
            Rates(
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new DateOnly(2026, 1, 1)),
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStallMeat, 35m, new DateOnly(2026, 1, 1))),
            MarketNaming("Gulayan", "Isda", "Karne"));

        Assert.Contains("Karne ₱35/day", line);
        // The areas it prices no differently say nothing: they are billed the market's rate, which the line already states.
        Assert.DoesNotContain("Gulayan", line);
        Assert.DoesNotContain("Isda", line);
    }

    [Fact]
    public async Task A_section_of_the_offices_own_is_stated_too()
    {
        var line = await NpmRateLineAsync(
            Rates(new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new DateOnly(2026, 1, 1))),
            MarketNaming("Gulayan", "Isda", "Karne"),
            new List<NpmCustomSectionDto>
            {
                new("Sari-sari Area", 4, 25m),
                new("Bakery Area", 2, null),   // unpriced, so billed the market's rate and nothing to state
            });

        Assert.Contains("Sari-sari Area ₱25/day", line);
        Assert.DoesNotContain("Bakery Area", line);
    }

    [Fact]
    public async Task The_markets_own_figure_still_comes_first()
    {
        // The market's rate is what a stall is billed wherever the office prices nothing apart, so it leads and the
        // exceptions follow it.
        var line = await NpmRateLineAsync(
            Rates(
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new DateOnly(2026, 1, 1)),
                new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStallFish, 32m, new DateOnly(2026, 1, 1))),
            MarketNaming("Gulayan", "Isda", "Karne"));

        Assert.True(line.IndexOf("₱30/day", StringComparison.Ordinal) < line.IndexOf("Isda", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Blank_environment_falls_back_to_Unknown(string? environment)
    {
        var result = await Handler.Handle(new GetSystemSettingsQuery(environment!), CancellationToken.None);

        Assert.Equal("Unknown", result.Value!.System.Environment);
    }

    // Minimal fake — the handler only calls GetDefaultAsync to source the LGU name/province.
    private sealed class FakeMunicipalityRepository(Municipality? def) : IMunicipalityRepository
    {
        public Task<IReadOnlyList<Municipality>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Municipality>>(def is null ? Array.Empty<Municipality>() : new[] { def });

        public Task<Municipality?> GetDefaultAsync(CancellationToken ct) => Task.FromResult(def);

        public Task<Municipality?> GetByIdentifierAsync(string identifier, CancellationToken ct) => Task.FromResult(def);

        public Task<Municipality?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(def);

        public Task<Municipality?> GetByBindTokenAsync(string bindToken, CancellationToken ct) => Task.FromResult(def);
    }

    // Minimal fake — the handler only calls GetFacilityNamesAsync to learn which facilities the tenant has.
    private sealed class FakeFacilityRepository(IEnumerable<FacilityCode> codes) : IFacilityRepository
    {
        private readonly IReadOnlyDictionary<FacilityCode, string> _names =
            codes.ToDictionary(c => c, c => c.ToString());

        /// <summary>The market, where a test needs the office's own area names. Null by default, as before.</summary>
        public Facility? Npm { get; init; }

        /// <summary>The office's own sections and their fees, where a test states them. Empty by default, as before.</summary>
        public IReadOnlyList<NpmCustomSectionDto> Sections { get; init; } = new List<NpmCustomSectionDto>();

        public Task<IReadOnlyDictionary<FacilityCode, string>> GetFacilityNamesAsync(CancellationToken ct) =>
            Task.FromResult(_names);

        public Task<Facility?> GetByCodeAsync(FacilityCode facilityCode, CancellationToken ct) =>
            Task.FromResult(facilityCode == FacilityCode.NPM ? Npm : null);

        public Task<IReadOnlyList<ConfiguredFacilityDto>> GetConfiguredFacilitiesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ConfiguredFacilityDto>>(new List<ConfiguredFacilityDto>());

        public Task AddFacilityAsync(Facility facility, CancellationToken ct) => Task.CompletedTask;

        public Task<FacilitySummaryDto> GetSummaryAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<FacilitySidebarSummaryDto>> GetSidebarSummariesAsync(int year, int month, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<NpmCustomSectionDto>> GetNpmCustomSectionsAsync(CancellationToken ct) =>
            Task.FromResult(Sections);
    }
}
