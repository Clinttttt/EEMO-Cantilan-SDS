using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;
using EEMOCantilanSDS.Application.Command.Facilities.SetNpmMonthBasis;
using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Testing.Support;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Domain.Entities.Users;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Facilities;

/// <summary>
/// The Public Market's rental model is the archetype its LGU declared at onboarding: a daily-collected market (the default, Cantilan's) or
/// STANDARD MONTHLY RENTAL, one monthly obligation per stall collected like TCC/NCC. It is read only from that stored declaration, never from a
/// rate, an amount, a collection frequency or a name; a market that declared nothing stays daily.
/// </summary>
public class PublicMarketRentalModelTests
{
    private static Facility Market(BillingArchetype? archetype) => Facility.Create(FacilityCode.NPM, "Madrid Public Market", "MPM", archetype: archetype);

    private static Stall StallOf(Facility facility)
    {
        var stall = Stall.Create(facility.Id, "A-1", 1500m, ApplicableFees.BaseRental, MarketSection.VegetableArea);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(stall, facility);
        return stall;
    }

    // ── what the stored declaration means ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, false)]                                     // nothing declared: the default for NPM is the daily market
    [InlineData(BillingArchetype.DailyStall, false)]               // Cantilan
    [InlineData(BillingArchetype.MonthlyRental, true)]             // standard monthly rental
    [InlineData(BillingArchetype.Custom, false)]
    public void Only_a_Public_Market_that_declared_monthly_rental_is_a_monthly_rent_market(BillingArchetype? declared, bool expected)
    {
        var facility = Market(declared);
        Assert.Equal(expected, facility.IsMonthlyRentMarket);
        Assert.Equal(expected, MonthlyRentalFacilities.Bills(facility));
    }

    [Fact]
    public void The_billing_model_is_not_inferred_from_the_name_or_from_another_facilitys_declaration()
    {
        Assert.False(Facility.Create(FacilityCode.TCC, "Monthly Public Market", "MPM").IsMonthlyRentMarket);   // a name says nothing
        Assert.True(MonthlyRentalFacilities.Bills(Facility.Create(FacilityCode.TCC, "x", "x")));                // TCC stays a monthly rental on its own account
        Assert.DoesNotContain(FacilityCode.NPM, MonthlyRentalFacilities.Codes);                                  // NPM is never monthly by code alone
        Assert.False(MonthlyRentalFacilities.Bills(FacilityCode.SLH, BillingArchetype.MonthlyRental));          // a per-head facility is not made monthly by the archetype
    }

    [Fact]
    public void The_declaration_cannot_be_changed_after_the_market_exists()
    {
        var archetype = typeof(Facility).GetProperty(nameof(Facility.Archetype))!;
        Assert.True(archetype.SetMethod is null || !archetype.SetMethod.IsPublic);
        var market = Market(BillingArchetype.MonthlyRental);
        market.UpdateProfile("Renamed", "RN", null);
        Assert.Equal(BillingArchetype.MonthlyRental, market.Archetype);                                         // renaming never converts the model
        Assert.True(market.IsMonthlyRentMarket);
    }

    // ── onboarding → activation ────────────────────────────────────────────────────────────────────────

    private static ActivateMunicipalityCommand Activation(BillingArchetype archetype, NpmMonthBasis basis = NpmMonthBasis.RentGoal,
        IReadOnlyList<ActivationRate>? rates = null, IReadOnlyList<string>? sections = null, FacilityCode code = FacilityCode.NPM) =>
        new("MADRID", new ActivationBranding("Madrid Economic Enterprise Office", null, null),
            new ActivationAdministrator("Ana Cruz", "madrid.head", "ana@madrid.gov.ph"),
            [new ActivationFacility(code, "Madrid Public Market", "MPM", archetype, null, null, sections, basis)],
            rates ?? []);

    private static (bool Valid, string Errors) Check(ActivateMunicipalityCommand command)
    {
        var result = new ActivateMunicipalityCommandValidator().Validate(command);
        return (result.IsValid, string.Join(" | ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void A_monthly_only_Public_Market_activates_with_no_daily_rate_and_may_declare_its_own_sections()
    {
        var (valid, errors) = Check(Activation(BillingArchetype.MonthlyRental, sections: ["Rice Section", "Dry Goods"],
            rates: [new(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, 5m)]));                                   // weighing is its own source and stays allowed
        Assert.True(valid, errors);
    }

    [Theory]
    [InlineData(FeeRateKey.NpmDailyStall)]
    [InlineData(FeeRateKey.NpmDailyStallVegetable)]
    [InlineData(FeeRateKey.NpmDailyStallFish)]
    [InlineData(FeeRateKey.NpmDailyStallMeat)]
    [InlineData(FeeRateKey.NpmMonthlyStall)]
    public void A_monthly_only_market_is_refused_a_daily_or_market_wide_monthly_rate(FeeRateKey key)
    {
        var (valid, errors) = Check(Activation(BillingArchetype.MonthlyRental, rates: [new(FacilityCode.NPM, key, 30m)]));
        Assert.False(valid);
        Assert.Contains("rented by the month", errors);
    }

    [Fact]
    public void A_monthly_only_market_is_refused_the_days_basis_instead_of_silently_converting_it()
    {
        var (valid, errors) = Check(Activation(BillingArchetype.MonthlyRental, NpmMonthBasis.PureDays));
        Assert.False(valid);
        Assert.Contains("rented by the month", errors);
    }

    [Theory]
    [InlineData(BillingArchetype.PerHead)]
    [InlineData(BillingArchetype.WeeklyMarket)]
    [InlineData(BillingArchetype.Custom)]
    public void The_Public_Market_is_either_daily_or_monthly_and_nothing_else(BillingArchetype other)
    {
        var (valid, errors) = Check(Activation(other, rates: []));
        Assert.False(valid);
        Assert.Contains("either collected daily or rented monthly", errors);
    }

    [Fact]
    public void The_daily_market_the_platform_has_always_activated_is_unchanged()
    {
        var (valid, errors) = Check(Activation(BillingArchetype.DailyStall, NpmMonthBasis.RentGoal,
            [new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m), new(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 900m)]));
        Assert.True(valid, errors);
        var (pureValid, _) = Check(Activation(BillingArchetype.DailyStall, NpmMonthBasis.PureDays, [new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m)]));
        Assert.True(pureValid);                                                                                  // PureDays is still declarable for a genuinely daily-charged market
    }

    [Fact]
    public void Market_sections_belong_to_a_public_market_only()
    {
        var (valid, errors) = Check(Activation(BillingArchetype.MonthlyRental, sections: ["Annex"], code: FacilityCode.TCC));
        Assert.False(valid);
        Assert.Contains("not a public market", errors);
    }

    // ── no daily charge or daily collection for a monthly-only market ─────────────────────────────────

    private static Mock<IStallRepository> StallRepo(Stall stall)
    {
        var repo = new Mock<IStallRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        return repo;
    }

    [Fact]
    public async Task A_monthly_market_stall_takes_a_monthly_payment_record_and_the_daily_market_still_refuses_one()
    {
        var monthly = StallOf(Market(BillingArchetype.MonthlyRental));
        var daily = StallOf(Market(BillingArchetype.DailyStall));
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(r => r.GetPaymentRecordAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((PaymentRecordDto?)null);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.Role).Returns("SuperAdmin");
        user.SetupGet(u => u.Username).Returns("head");

        RecordPaymentCommandHandler Handler(Stall s) => new(payments.Object, StallRepo(s).Object, new Mock<ICollectorRepository>().Object, user.Object,
            new Mock<IUnitOfWork>().Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

        var accepted = await Handler(monthly).Handle(new RecordPaymentCommand(monthly.Id, 2026, 6, PaymentStatus.Partial, 600m, null), CancellationToken.None);
        Assert.True(accepted.IsSuccess, accepted.Error);
        payments.Verify(r => r.AddAsync(It.Is<PaymentRecord>(p => p.BaseRentalAmount == 1500m), It.IsAny<CancellationToken>()), Times.Once);

        payments.Invocations.Clear();
        var refused = await Handler(daily).Handle(new RecordPaymentCommand(daily.Id, 2026, 6, PaymentStatus.Partial, 500m, null), CancellationToken.None);
        Assert.Equal(ResultStatus.Invalid, refused.Status);
        payments.Verify(r => r.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Daily_writers_refuse_a_monthly_market_stall_before_touching_any_daily_record()
    {
        var stall = StallOf(Market(BillingArchetype.MonthlyRental));
        var stalls = StallRepo(stall).Object;
        var daily = new Mock<IDailyCollectionRepository>();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.Role).Returns("SuperAdmin");
        var rates = new Mock<IFeeRateResolver>();

        var record = new RecordDailyCollectionCommandHandler(daily.Object, new Mock<IPaymentRepository>().Object, new Mock<IOrNumberRegistry>().Object,
            stalls, new Mock<ICollectorRepository>().Object, user.Object, new Mock<IUnitOfWork>().Object, CacheTestDoubles.Invalidator, rates.Object, CacheTestDoubles.Tenant);
        var recorded = await record.Handle(new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 10), IsPaid: true), CancellationToken.None);
        Assert.False(recorded.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, recorded.Status);

        var days = new SettleNpmDaysCommandHandler(daily.Object, new Mock<IPaymentRepository>().Object, stalls, new Mock<ICollectorRepository>().Object, user.Object,
            new Mock<INpmMarketClosureRepository>().Object, new Mock<IUnitOfWork>().Object, CacheTestDoubles.Invalidator, rates.Object,
            new Mock<INpmMonthSettlementService>().Object, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));
        Assert.Equal(ResultStatus.Invalid, (await days.Handle(new SettleNpmDaysCommand(stall.Id, new[] { new DateOnly(2026, 6, 1) }, null), CancellationToken.None)).Status);

        var month = new SettleNpmMonthCommandHandler(daily.Object, new Mock<IPaymentRepository>().Object, stalls, new Mock<ICollectorRepository>().Object, user.Object,
            new Mock<INpmMarketClosureRepository>().Object, new Mock<IUnitOfWork>().Object, CacheTestDoubles.Invalidator, rates.Object,
            CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));
        Assert.Equal(ResultStatus.Invalid, (await month.Handle(new SettleNpmMonthCommand(stall.Id, 2026, 6), CancellationToken.None)).Status);

        daily.Invocations.Clear();
        daily.Verify(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_month_basis_of_a_monthly_market_cannot_be_chosen_and_the_daily_markets_can()
    {
        var facilities = new Mock<IFacilityRepository>();
        var uow = new Mock<IUnitOfWork>();
        SetNpmMonthBasisCommandHandler Handler(Facility market)
        {
            facilities.Setup(f => f.GetByCodeAsync(FacilityCode.NPM, It.IsAny<CancellationToken>())).ReturnsAsync(market);
            return new(facilities.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));
        }

        var refused = await Handler(Market(BillingArchetype.MonthlyRental)).Handle(new SetNpmMonthBasisCommand(NpmMonthBasis.PureDays), CancellationToken.None);
        Assert.Equal(ResultStatus.Invalid, refused.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var daily = Market(BillingArchetype.DailyStall);
        Assert.True((await Handler(daily).Handle(new SetNpmMonthBasisCommand(NpmMonthBasis.PureDays), CancellationToken.None)).IsSuccess);
        Assert.Equal(NpmMonthBasis.PureDays, daily.MonthBasis);                                                  // Cantilan-style markets keep their choice
    }

    [Theory]
    [InlineData(BillingArchetype.MonthlyRental, BillingArchetype.MonthlyRental)]
    [InlineData(BillingArchetype.DailyStall, BillingArchetype.DailyStall)]
    public async Task The_collector_menu_routes_a_market_by_its_declared_model(BillingArchetype declared, BillingArchetype expected)
    {
        var collector = CollectorUser.Create("Juan Collector", "EEMO-2026-001", "juan", "juan@eemo.gov", "09170000000", TestPasswords.Hash("Secret123!"));
        collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), FacilityCode.NPM));
        var repo = new Mock<ICollectorRepository>();
        repo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        var facilities = new Mock<IFacilityRepository>();
        facilities.Setup(r => r.GetFacilityNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<FacilityCode, string>)new Dictionary<FacilityCode, string> { [FacilityCode.NPM] = "Madrid Public Market" });
        facilities.Setup(r => r.GetByCodeAsync(FacilityCode.NPM, It.IsAny<CancellationToken>())).ReturnsAsync(Market(declared));
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.CollectorId).Returns(collector.Id);
        var handler = new EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorMobileMenu.GetCollectorMobileMenuQueryHandler(repo.Object, facilities.Object,
            new Mock<IMunicipalityRepository>().Object, new Mock<EEMOCantilanSDS.Application.Common.Tenancy.ITenantContext>().Object, user.Object,
            new FixedClock(DateTime.UtcNow), CacheTestDoubles.TpmMarketDay);

        var menu = (await handler.Handle(new(), CancellationToken.None)).Value!;

        var market = menu.Facilities.Single(f => f.Code == FacilityCode.NPM);
        Assert.Equal(expected, market.Archetype);                       // the device opens the monthly rent screen only for a declared monthly market
        Assert.True(market.IsAvailable);
    }
}
