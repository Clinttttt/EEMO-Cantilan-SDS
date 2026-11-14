using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// IA-080: the Monthly Income sheet is Cantilan's confirmed sheet for Cantilan, and every other office's own lines under its own
/// names. A line with money is never hidden.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OfficeMonthlyIncomeLinesTests(PostgresFixture db)
{
    private static readonly DateOnly Today = PhilippineTime.Today;

    // A standalone deployment makes its only office the sign-in default (IsDefault); that never gives it Cantilan's sheet.
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Another_office_sees_only_its_own_lines_under_its_own_names_and_no_money_is_hidden(bool signInDefault)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var tenant = await TenantAsync("RIVERSIDE", signInDefault);
        await using (var ctx = db.CreateContext(tenant))
        {
            var hall = Facility.Create(FacilityCode.TCC, "Riverside Trade Hall", "RTH", municipalityId: tenant);
            var stands = Facility.Create(FacilityCode.BBQ, "Old Food Stands", "OFS", municipalityId: tenant);
            stands.Deactivate(); // archived, but its rent was received this year
            var hallStall = Stall.Create(hall.Id, "R-01", 900m, ApplicableFees.BaseRental, municipalityId: tenant);
            var standStall = Stall.Create(stands.Id, "S-01", 500m, ApplicableFees.BaseRental, municipalityId: tenant);
            var hallRent = PaymentRecord.Create(hallStall.Id, Today.Year, Today.Month, 900m, "test");
            hallRent.RecordPayment("R-OR-1", Guid.NewGuid(), PaymentStatus.Paid, updatedBy: "test");
            var standRent = PaymentRecord.Create(standStall.Id, Today.Year, Today.Month, 500m, "test");
            standRent.RecordPayment("R-OR-2", Guid.NewGuid(), PaymentStatus.Paid, updatedBy: "test");
            var fines = RevenueClassification.Create(RevenueClassificationCodes.PenaltiesAndFines, tenant);
            ctx.AddRange(hall, stands, hallStall, standStall, hallRent, standRent, fines,
                RevenueClassificationPolicy.Create(fines.Id, new DateOnly(2000, 1, 1), "Fines", RevenueInstrumentType.OfficialReceipt, tenant));
            foreach (var entry in TenantOperationCatalog.All)
                ctx.Add(TenantOperationSetting.Create(entry.Code, entry.Code == CollectorOperationCodes.MarketFees,
                    entry.Code == CollectorOperationCodes.MarketFees ? "Palengke Fees" : null, "Onboarding", tenant, "test"));
            await ctx.SaveChangesAsync();
        }

        var sheet = await StatementAsync(tenant);
        var rows = sheet.Groups.SelectMany(g => g.Rows).ToDictionary(r => r.Key);

        // Every official line stays in the response for reconciliation; the sheet shows the lines this office holds.
        Assert.Equal(OfficialMonthlyIncomeStructure.Rows.Where(r => r.GroupKey != OfficialMonthlyIncomeStructure.Pending).Select(r => r.Key),
            rows.Keys);
        Assert.Equal(["MARKET_FEES", "RENT_TCC", "RENT_BBQ", "ARREARS", "PENALTIES_AND_FINES"],
            rows.Values.Where(r => r.ShowOnOfficialReport).Select(r => r.Key).ToArray());
        Assert.Equal("Palengke Fees", rows["MARKET_FEES"].Label);
        Assert.Equal("Riverside Trade Hall (RTH)", rows["RENT_TCC"].Label);
        Assert.Equal("Old Food Stands (OFS)", rows["RENT_BBQ"].Label);
        Assert.Equal(500m, rows["RENT_BBQ"].Total.Total);
        Assert.Equal("Others", sheet.Groups.Single(g => g.Rows.Any(r => r.Key == "PENALTIES_AND_FINES")).Label);
        Assert.Equal(1400m, sheet.GrandTotal.Total);
        Assert.DoesNotContain(rows.Values, r => r.Label.Contains("Tampak") || r.Label.Contains("Barbecue") || r.Label.Contains("Kanmanggay"));
    }

    [SkippableFact]
    public async Task Cantilan_keeps_its_confirmed_sheet_exactly()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var tenant = await TenantAsync(OfficialMonthlyIncomeStructure.SheetOfficeCode);

        var sheet = await StatementAsync(tenant);

        var expected = OfficialMonthlyIncomeStructure.Rows.Where(r => r.GroupKey != OfficialMonthlyIncomeStructure.Pending).ToList();
        var rows = sheet.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Equal(expected.Select(r => r.Key), rows.Select(r => r.Key));
        Assert.All(rows, r => Assert.True(r.ShowOnOfficialReport));
        // A line without a facility keeps the sheet's name; a facility line is named from Cantilan's own facility register.
        Assert.Equal(expected.Where(r => r.Facility is null).Select(r => (r.Key, r.Label)),
            rows.Where(r => expected.Single(e => e.Key == r.Key).Facility is null).Select(r => (r.Key, r.Label)));
        Assert.Equal(OfficialMonthlyIncomeStructure.Groups.Where(g => g.Key != OfficialMonthlyIncomeStructure.Pending).Select(g => g.Label),
            sheet.Groups.Select(g => g.Label));
        Assert.Equal(["A", "B", "C"], sheet.Sections!.Select(s => s.Key).ToArray());
    }

    private async Task<Guid> TenantAsync(string code, bool isDefault = false)
    {
        var tenant = Municipality.Create(code, code, "Province", MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant(), isDefault: isDefault);
        await using var setup = db.CreateContext(Guid.Empty);
        setup.Municipalities.Add(tenant);
        await setup.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task<OfficialMonthlyIncomeDto> StatementAsync(Guid tenant)
    {
        await using var ctx = db.CreateContext(tenant);
        var result = await new GetOfficialMonthlyIncomeQueryHandler(ctx, new LegacyMonthlyIncomeReader(ctx), new Head(tenant),
            new FixedTenant(tenant), new Clock()).Handle(new(Today.Year, null), default);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Head(Guid tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Username => "head";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }
}
