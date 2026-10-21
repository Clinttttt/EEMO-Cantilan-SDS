using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

public sealed class PreviewMonthlyRentGateTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly PreviewMonthlyRentTarget Target = new("stalltrack_preview", 55489, Tenant, FacilityCode.NCC);
    private const string Good = "Host=localhost;Port=55489;Database=stalltrack_preview;Username=u;Password=p";

    [Fact]
    public void The_exact_loopback_preview_database_passes_and_enables_only_its_tenant_and_facility()
    {
        var options = PreviewMonthlyRentGate.OptionsFor(true, Target, Good, out var refusals);
        Assert.Empty(refusals);
        Assert.True(options.Allows(Tenant, FacilityCode.NCC));
        Assert.False(options.Allows(Tenant, FacilityCode.TCC));
        Assert.False(options.Allows(Guid.NewGuid(), FacilityCode.NCC));
    }

    [Fact]
    public void Nothing_is_enabled_unless_explicitly_requested()
    {
        var options = PreviewMonthlyRentGate.OptionsFor(false, Target, Good, out var refusals);
        Assert.Empty(refusals);
        Assert.False(options.OfficeEnabled);
        Assert.False(options.Allows(Tenant, FacilityCode.NCC));
    }

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=EEMOCantilanSDS.DB;Username=u;Password=p")]            // the municipal database
    [InlineData("Host=localhost;Port=55489;Database=EEMOCantilanSDS.DB;Username=u;Password=p")]           // right port, wrong database
    [InlineData("Host=localhost;Port=5432;Database=stalltrack_preview;Username=u;Password=p")]            // right name, wrong server port
    [InlineData("Host=db.example.org;Port=55489;Database=stalltrack_preview;Username=u;Password=p")]      // not on this machine
    [InlineData("Host=localhost;Database=stalltrack_preview;Username=u;Password=p")]                      // no port
    [InlineData("")]
    [InlineData("not a connection string")]
    public void A_wrong_database_keeps_everything_off(string connection)
    {
        var options = PreviewMonthlyRentGate.OptionsFor(true, Target, connection, out var refusals);
        Assert.NotEmpty(refusals);
        Assert.False(options.OfficeEnabled);
        Assert.False(options.Allows(Tenant, FacilityCode.NCC));
    }

    [Theory]
    [InlineData("EEMOCantilanSDS.DB", 55491)]    // the retained municipal database name, on any port
    [InlineData("stalltrack_workcopy_main", 5432)]    // the municipal server port, under any name
    public void The_retained_municipal_database_is_never_a_target(string database, int port)
    {
        var connection = $"Host=localhost;Port={port};Database={database};Username=u;Password=p";
        var refusals = PreviewMonthlyRentGate.DatabaseRefusals(connection, database, port);
        Assert.Contains(refusals, x => x.Contains("never"));
        Assert.False(PreviewMonthlyRentGate.OptionsFor(true, Target with { Database = database, Port = port }, connection, out _).OfficeEnabled);
    }

    [Fact]
    public void A_missing_tenant_or_non_rental_facility_keeps_everything_off()
    {
        foreach (var target in new[] { Target with { Tenant = Guid.Empty }, Target with { Facility = FacilityCode.SLH }, Target with { Facility = FacilityCode.TPM }, null })
        {
            var options = PreviewMonthlyRentGate.OptionsFor(true, target, Good, out var refusals);
            Assert.NotEmpty(refusals);
            Assert.False(options.OfficeEnabled);
        }
    }

    [Fact]
    public void A_public_market_can_be_named_but_only_its_permitted_synthetic_stalls_may_enter_cutover()
    {
        // The gate cannot see the archetype; PreviewRentActivation refuses a daily market. Here: NPM is nameable, and only PREVIEW-NPM-nn stalls pass.
        var named = Target with { Facility = FacilityCode.NPM };
        Assert.True(PreviewMonthlyRentGate.OptionsFor(true, named, Good, out var refusals).OfficeEnabled);
        Assert.Empty(refusals);
        Assert.Empty(PreviewMonthlyRentGate.SourceRefusals(named, Tenant, FacilityCode.NPM, "PREVIEW-NPM-01", SettlementAuthority.Legacy, PaymentStatus.Unpaid, 0m, false, false));
        Assert.NotEmpty(PreviewMonthlyRentGate.SourceRefusals(named, Tenant, FacilityCode.NPM, "A-1", SettlementAuthority.Legacy, PaymentStatus.Unpaid, 0m, false, false));
        Assert.NotEmpty(PreviewMonthlyRentGate.SourceRefusals(named, Tenant, FacilityCode.NPM, "PREVIEW-NPM-07", SettlementAuthority.Legacy, PaymentStatus.Unpaid, 0m, false, false));
    }

    private static IReadOnlyList<string> Source(
        Guid? tenant = null, FacilityCode facility = FacilityCode.NCC, string stall = "PREVIEW-NCC-01",
        SettlementAuthority authority = SettlementAuthority.Legacy, PaymentStatus status = PaymentStatus.Unpaid,
        decimal settled = 0m, bool cutover = false, bool collection = false) =>
        PreviewMonthlyRentGate.SourceRefusals(Target, tenant ?? Tenant, facility, stall, authority, status, settled, cutover, collection);

    [Fact]
    public void A_fresh_synthetic_unpaid_zero_cash_legacy_month_is_accepted() => Assert.Empty(Source());

    [Theory]
    [InlineData("PREVIEW-NCC-01", true)]
    [InlineData("PREVIEW-NCC-02", true)]
    [InlineData("PREVIEW-NCC-03", true)]
    [InlineData("PREVIEW-NCC-04", false)]   // synthetic-looking but not explicitly permitted
    [InlineData("preview-ncc-02", false)]
    [InlineData("PREVIEW-TCC-01", false)]
    public void Only_explicitly_permitted_synthetic_stall_identities_are_accepted(string stall, bool accepted) =>
        Assert.Equal(accepted, Source(stall: stall).Count == 0);

    [Fact]
    public void Wrong_tenant_wrong_facility_and_unreviewed_existing_stalls_are_refused()
    {
        Assert.Single(Source(tenant: Guid.NewGuid()));
        Assert.Single(Source(facility: FacilityCode.TCC));
        Assert.Single(Source(stall: "1")); // the preview's existing NCC stall with legacy reference 546564
    }

    [Theory]
    [InlineData(PaymentStatus.Paid, 1200)]
    [InlineData(PaymentStatus.Partial, 600)]
    [InlineData(PaymentStatus.Unpaid, 54)]
    public void A_positive_legacy_opening_is_refused(PaymentStatus status, double settled) =>
        Assert.Single(Source(status: status, settled: (decimal)settled));

    [Theory]
    [InlineData(SettlementAuthority.PendingCutover)]
    [InlineData(SettlementAuthority.Canonical)]
    public void Duplicate_or_mid_cutover_activation_is_refused(SettlementAuthority authority) =>
        Assert.Single(Source(authority: authority));

    [Fact]
    public void An_already_frozen_or_already_collected_source_is_refused()
    {
        Assert.Single(Source(cutover: true));
        Assert.Single(Source(collection: true));
    }
}
