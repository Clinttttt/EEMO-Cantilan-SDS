using EEMOCantilanSDS.Application.Queries.Settings.GetSystemSettings;
using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Testing;

public class GetSystemSettingsQueryHandlerTests
{
    private static readonly GetSystemSettingsQueryHandler Handler = new();

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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Blank_environment_falls_back_to_Unknown(string? environment)
    {
        var result = await Handler.Handle(new GetSystemSettingsQuery(environment!), CancellationToken.None);

        Assert.Equal("Unknown", result.Value!.System.Environment);
    }
}
