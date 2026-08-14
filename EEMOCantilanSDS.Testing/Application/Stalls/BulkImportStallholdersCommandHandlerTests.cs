using EEMOCantilanSDS.Application.Command.Stalls.BulkImportStallholders;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class BulkImportStallholdersCommandHandlerTests
{
    private readonly Mock<IStallRepository> _stallRepo = new();
    private readonly Mock<IFacilityRepository> _facilityRepo = new();
    private readonly Mock<IPayorRepository> _payorRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public BulkImportStallholdersCommandHandlerTests()
    {
        // Default: the facility has no existing stalls. Renewal/skip tests override this.
        _stallRepo.Setup(r => r.GetStallsWithContractsByFacilityAsync(
                It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Stall>());
    }

    // Builds an existing stall carrying one contract. expiredYearsAgo>0 makes it EXPIRED (lapsed term);
    // otherwise the term covers today (actively occupied).
    private static Stall ExistingStall(string stallNo, string occupant, bool active, MarketSection? section = null)
    {
        var stall = Stall.Create(Guid.NewGuid(), stallNo, 900m, ApplicableFees.BaseRental, section);
        var effectivity = active ? PhilippineTime.Today.AddYears(-1) : new DateOnly(2020, 1, 1);
        stall.Contracts.Add(Contract.Create(stall.Id, occupant, occupant, effectivity, 3, 900m));
        return stall;
    }

    private void SetupExisting(FacilityCode code, MarketSection? section, params Stall[] stalls)
        => _stallRepo.Setup(r => r.GetStallsWithContractsByFacilityAsync(code, section, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stalls);

    private BulkImportStallholdersCommandHandler Handler()
        => new(_stallRepo.Object, _facilityRepo.Object, _payorRepo.Object, _uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

    private void SetupFacility(FacilityCode code)
        => _facilityRepo.Setup(r => r.GetByCodeAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Facility.Create(code, code.ToString(), code.ToString()));

    private void SetupUnique(bool unique)
        => _stallRepo.Setup(r => r.IsStallNoUniqueAsync(
                It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unique);

    private static ImportStallRow Row(int n, string occupant, string stallNo,
        decimal monthly = 900m, int years = 3, string? areaLoc = null)
        => new(n, occupant, occupant, stallNo, new DateTime(2023, 6, 7), years, 4.8, monthly, null, areaLoc);

    [Fact]
    public async Task ValidRows_AreCreated_InOneTransaction()
    {
        SetupFacility(FacilityCode.TCC);
        SetupUnique(true);
        var cmd = new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow>
        {
            Row(1, "Juan Dela Cruz", "1"),
            Row(2, "Maria Santos", "2"),
        });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.FailedCount);
        _stallRepo.Verify(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _stallRepo.Verify(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NpmCustomSection_Import_CreatesStallsWithCustomName_AndRegistersSection()
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        _facilityRepo.Setup(r => r.GetByCodeAsync(FacilityCode.NPM, It.IsAny<CancellationToken>()))
            .ReturnsAsync(facility);
        SetupUnique(true);

        Stall? captured = null;
        _stallRepo.Setup(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()))
            .Callback<Stall, CancellationToken>((s, _) => captured = s);

        var cmd = new BulkImportStallholdersCommand(FacilityCode.NPM, null, new List<ImportStallRow>
        {
            Row(1, "Juan Dela Cruz", "1"),
        }, CustomSectionName: "Sari-sari Area");

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.NotNull(captured);
        Assert.Null(captured!.Section);                                 // custom section → no canonical enum
        Assert.Equal("Sari-sari Area", captured.CustomSectionName);
        Assert.Contains("Sari-sari Area", facility.CustomSectionNames);  // registered as a reusable option
    }

    [Fact]
    public async Task DuplicateStallNoWithinBatch_SecondRowFails()
    {
        SetupFacility(FacilityCode.TCC);
        SetupUnique(true);
        var cmd = new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow>
        {
            Row(1, "Juan", "5"),
            Row(2, "Pedro", "5"),
        });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value.FailedCount);
        var failed = result.Value.Results.Single(r => !r.Created);
        Assert.Equal(2, failed.RowNumber);
        Assert.Contains("Duplicate", failed.Error);
        _stallRepo.Verify(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidRows_AreReported_AndOnlyValidPersisted()
    {
        SetupFacility(FacilityCode.TCC);
        SetupUnique(true);
        var cmd = new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow>
        {
            Row(1, "", "1"),               // missing occupant
            Row(2, "No Rate", "2", monthly: 0m),
            Row(3, "Bad Years", "3", years: 0),
            Row(4, "Good One", "4"),
        });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.Equal(3, result.Value.FailedCount);
        Assert.Contains(result.Value.Results, r => r.RowNumber == 1 && r.Error!.Contains("occupant"));
        Assert.Contains(result.Value.Results, r => r.RowNumber == 2 && r.Error!.Contains("Monthly"));
        Assert.Contains(result.Value.Results, r => r.RowNumber == 3 && r.Error!.Contains("duration"));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NpmImport_AppliesSectionDailyRateAndFishFee()
    {
        SetupFacility(FacilityCode.NPM);
        SetupUnique(true);
        Stall? captured = null;
        _stallRepo.Setup(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()))
            .Callback<Stall, CancellationToken>((s, _) => captured = s)
            .Returns(Task.CompletedTask);

        var cmd = new BulkImportStallholdersCommand(FacilityCode.NPM, MarketSection.FishSection,
            new List<ImportStallRow> { Row(1, "Fisher Joe", "1") });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.NotNull(captured);
        Assert.Equal(MarketSection.FishSection, captured!.Section);
        Assert.Equal(FeeRates.NpmDailyFee, captured.DailyRate);
        Assert.True(captured.Fees.HasFlag(ApplicableFees.FishFee));
    }

    [Fact]
    public async Task NpmImport_WithUtilities_StampsElectricityAndWaterFees()
    {
        SetupFacility(FacilityCode.NPM);
        SetupUnique(true);
        Stall? captured = null;
        _stallRepo.Setup(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()))
            .Callback<Stall, CancellationToken>((s, _) => captured = s)
            .Returns(Task.CompletedTask);

        var cmd = new BulkImportStallholdersCommand(FacilityCode.NPM, MarketSection.VegetableArea,
            new List<ImportStallRow> { Row(1, "Vega Ven", "1") },
            ApplyElectricity: true, ApplyWater: true);

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.NotNull(captured);
        Assert.True(captured!.Fees.HasFlag(ApplicableFees.Electricity));
        Assert.True(captured.Fees.HasFlag(ApplicableFees.Water));
    }

    [Fact]
    public async Task NpmImport_WithoutUtilities_DoesNotStampUtilityFees()
    {
        SetupFacility(FacilityCode.NPM);
        SetupUnique(true);
        Stall? captured = null;
        _stallRepo.Setup(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()))
            .Callback<Stall, CancellationToken>((s, _) => captured = s)
            .Returns(Task.CompletedTask);

        var cmd = new BulkImportStallholdersCommand(FacilityCode.NPM, MarketSection.VegetableArea,
            new List<ImportStallRow> { Row(1, "Vega Ven", "1") });

        await Handler().Handle(cmd, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.False(captured!.Fees.HasFlag(ApplicableFees.Electricity));
        Assert.False(captured.Fees.HasFlag(ApplicableFees.Water));
    }

    [Fact]
    public async Task ExistingExpiredStall_IsRenewed_NotDuplicated()
    {
        SetupFacility(FacilityCode.TCC);
        var existing = ExistingStall("5", "Old Tenant", active: false);
        SetupExisting(FacilityCode.TCC, null, existing);
        Contract? added = null;
        _stallRepo.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()))
            .Callback<Contract, CancellationToken>((c, _) => added = c).Returns(Task.CompletedTask);

        var result = await Handler().Handle(
            new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow> { Row(1, "New Tenant", "5") }),
            CancellationToken.None);

        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value.RenewedCount);
        Assert.Equal(0, result.Value.FailedCount);
        // Reuses the SAME stall (no new stall) with a fresh contract; the lapsed contract is terminated.
        _stallRepo.Verify(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()), Times.Never);
        _stallRepo.Verify(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(existing.Id, added!.StallId);
        Assert.Equal("New Tenant", added.ActualOccupant);
        Assert.False(existing.Contracts.Single().IsActive); // old term ended
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NpmRenewal_WithUtilities_AddsUtilityFeesToReusedStall_PreservingOthers()
    {
        SetupFacility(FacilityCode.NPM);
        var existing = ExistingStall("1", "Old Tenant", active: false, section: MarketSection.VegetableArea);
        SetupExisting(FacilityCode.NPM, MarketSection.VegetableArea, existing);
        _stallRepo.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await Handler().Handle(
            new BulkImportStallholdersCommand(FacilityCode.NPM, MarketSection.VegetableArea,
                new List<ImportStallRow> { Row(1, "New Tenant", "1") },
                ApplyElectricity: true, ApplyWater: true),
            CancellationToken.None);

        Assert.Equal(1, result.Value!.RenewedCount);
        Assert.True(existing.Fees.HasFlag(ApplicableFees.Electricity)); // utilities applied to the reused stall
        Assert.True(existing.Fees.HasFlag(ApplicableFees.Water));
        Assert.True(existing.Fees.HasFlag(ApplicableFees.BaseRental)); // existing flags preserved (additive)
    }

    [Fact]
    public async Task ExistingActiveStall_IsSkipped_NotOverwritten()
    {
        SetupFacility(FacilityCode.TCC);
        SetupExisting(FacilityCode.TCC, null, ExistingStall("5", "Current Tenant", active: true));

        var result = await Handler().Handle(
            new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow> { Row(1, "New Tenant", "5") }),
            CancellationToken.None);

        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.RenewedCount);
        Assert.Equal(1, result.Value.FailedCount);
        Assert.Contains("active contract", result.Value.Results.Single().Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SameNameAsActiveOccupant_IsSkipped_NotDuplicated()
    {
        SetupFacility(FacilityCode.TCC);
        SetupExisting(FacilityCode.TCC, null, ExistingStall("1", "Juan Dela Cruz", active: true));

        // Import the same person (case/spacing-insensitive) onto a NEW number → skipped, never duplicated.
        var result = await Handler().Handle(
            new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow> { Row(1, "juan  dela cruz", "9") }),
            CancellationToken.None);

        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value.FailedCount);
        Assert.Contains("already an active stallholder", result.Value.Results.Single().Error);
        _stallRepo.Verify(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FacilityNotFound_ReturnsNotFound()
    {
        _facilityRepo.Setup(r => r.GetByCodeAsync(It.IsAny<FacilityCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Facility?)null);
        var cmd = new BulkImportStallholdersCommand(FacilityCode.TCC, null,
            new List<ImportStallRow> { Row(1, "Juan", "1") });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task NccUnknownAreaLocation_FallsBackToStandard_NotExtension()
    {
        SetupFacility(FacilityCode.NCC);
        SetupUnique(true);
        Stall? captured = null;
        _stallRepo.Setup(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()))
            .Callback<Stall, CancellationToken>((s, _) => captured = s)
            .Returns(Task.CompletedTask);

        var cmd = new BulkImportStallholdersCommand(FacilityCode.NCC, null,
            new List<ImportStallRow> { Row(1, "Lucrecia Bebero", "1", areaLoc: "Mezzanine") });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.Equal(1, result.Value!.CreatedCount);
        Assert.NotNull(captured);
        Assert.Equal(NccAreaLocation.Standard, captured!.AreaLocation);
    }

    [Fact]
    public async Task NccRecognisedAreaLocation_IsParsedExactly()
    {
        SetupFacility(FacilityCode.NCC);
        SetupUnique(true);
        Stall? captured = null;
        _stallRepo.Setup(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()))
            .Callback<Stall, CancellationToken>((s, _) => captured = s)
            .Returns(Task.CompletedTask);

        var cmd = new BulkImportStallholdersCommand(FacilityCode.NCC, null,
            new List<ImportStallRow> { Row(1, "Corner Owner", "9", areaLoc: "corner") });

        await Handler().Handle(cmd, CancellationToken.None);

        Assert.Equal(NccAreaLocation.Corner, captured!.AreaLocation);
    }

    [Fact]
    public async Task LengthAndNegativeValues_AreReported()
    {
        SetupFacility(FacilityCode.TCC);
        SetupUnique(true);
        var longName = new string('x', 101);
        var cmd = new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow>
        {
            new(1, "Juan", longName, "1", new DateTime(2023, 6, 7), 3, 4.8, 900m, null, null),     // contract name > 100
            new(2, "Pedro", "Pedro", "2", new DateTime(2023, 6, 7), 3, 4.8, 900m, -5m, null),      // negative actual rental
            new(3, "Maria", "Maria", "3", new DateTime(2023, 6, 7), 3, -2.0, 900m, null, null),    // negative area
        });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Equal(3, result.Value.FailedCount);
        Assert.Contains(result.Value.Results, r => r.RowNumber == 1 && r.Error!.Contains("Name on contract"));
        Assert.Contains(result.Value.Results, r => r.RowNumber == 2 && r.Error!.Contains("Actual monthly rental"));
        Assert.Contains(result.Value.Results, r => r.RowNumber == 3 && r.Error!.Contains("Area"));
    }

    [Theory]
    [InlineData("Closed")]
    [InlineData("close")]
    [InlineData("Vacant")]
    [InlineData("N/A")]
    [InlineData("None")]
    [InlineData("-")]
    [InlineData("  closed  ")]   // surrounding whitespace
    [InlineData("N / A")]        // internal whitespace
    public async Task PlaceholderOccupant_IsRejected_NotCreated(string occupant)
    {
        SetupFacility(FacilityCode.NPM);
        SetupUnique(true);
        var cmd = new BulkImportStallholdersCommand(FacilityCode.NPM, MarketSection.VegetableArea,
            new List<ImportStallRow> { Row(1, occupant, "26") });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value.FailedCount);
        Assert.Contains("Closed/vacant", result.Value.Results.Single().Error);
        _stallRepo.Verify(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()), Times.Never);
        _stallRepo.Verify(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidRows_StillImport_WhenPlaceholderRowsPresent()
    {
        SetupFacility(FacilityCode.NPM);
        SetupUnique(true);
        var cmd = new BulkImportStallholdersCommand(FacilityCode.NPM, MarketSection.VegetableArea, new List<ImportStallRow>
        {
            Row(1, "Gloria B. Erman", "26"),   // real name on contract, but occupant is the placeholder below
            Row(2, "Closed", "27"),            // placeholder → rejected
            Row(3, "Merlita E. Huelma", "28"), // valid
        });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.CreatedCount);
        Assert.Equal(1, result.Value.FailedCount);
        var rejected = result.Value.Results.Single(r => !r.Created);
        Assert.Equal(2, rejected.RowNumber);
        Assert.Equal("27", rejected.StallNo);
        Assert.Contains("Closed/vacant", rejected.Error);
        // Only the two valid rows are persisted.
        _stallRepo.Verify(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LegitimateName_ContainingPlaceholderSubstring_IsNotRejected()
    {
        // Guard against over-matching: a real name that merely contains "close"/"none" must import.
        SetupFacility(FacilityCode.TCC);
        SetupUnique(true);
        var cmd = new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow>
        {
            Row(1, "Rosanne Close", "1"),
            Row(2, "Noneto Vacantes", "2"),
        });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.Equal(2, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.FailedCount);
    }

    [Fact]
    public async Task ReImportingTheSameSheet_DoesNotDuplicateTheTerm()
    {
        // The office's lists are expired sheets, so the "already an active stallholder" guard does not catch a second
        // upload: each run used to add another term to the stall — and another month of arrears with it.
        SetupFacility(FacilityCode.TCC);
        var stall = Stall.Create(Guid.NewGuid(), "5", 900m, ApplicableFees.BaseRental);
        stall.Contracts.Add(Contract.Create(stall.Id, "Juan Dela Cruz", "Juan Dela Cruz", new DateOnly(2023, 6, 7), 3, 900m));
        SetupExisting(FacilityCode.TCC, null, stall);

        var result = await Handler().Handle(
            new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow> { Row(1, "Juan Dela Cruz", "5") }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.RenewedCount);
        Assert.Equal(1, result.Value.FailedCount);
        Assert.Contains("imported before", result.Value.Results.Single().Error);
        // Nothing was written at all — no second term, and the recorded term is left as it stands.
        _stallRepo.Verify(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(stall.Contracts.Single().IsActive);
    }

    [Fact]
    public async Task ADifferentLesseeOnTheSameVacatedNumber_IsStillARelet()
    {
        // The guard is exact: only the same lessee from the same date is a repeat. A genuinely new occupant takes
        // over the vacated number as before.
        SetupFacility(FacilityCode.TCC);
        var stall = Stall.Create(Guid.NewGuid(), "5", 900m, ApplicableFees.BaseRental);
        stall.Contracts.Add(Contract.Create(stall.Id, "Old Tenant", "Old Tenant", new DateOnly(2019, 1, 1), 3, 900m));
        SetupExisting(FacilityCode.TCC, null, stall);
        _stallRepo.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await Handler().Handle(
            new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow> { Row(1, "New Tenant", "5") }),
            CancellationToken.None);

        Assert.Equal(1, result.Value!.RenewedCount);
        Assert.Equal(0, result.Value.FailedCount);
    }

    // ── A list of spaces let without a contract: the office's barbecue and ice-plant lists ──
    // Such a sheet names an occupant and an "Actual Mo. Rental" and nothing else: no leasee per contract, no
    // term, no contract rental. Every row of it was being turned away — for a ₱0 rate and for a missing
    // duration — which is the whole file.

    private static ImportStallRow SpaceOnlyRow(int n, string occupant, string stallNo, decimal actualMonthly) =>
        new(n, occupant, null, stallNo, null, 0, null, 0m, actualMonthly, null);

    [Fact]
    public async Task ASpaceLetWithoutAContract_ImportsAtItsActualMonthlyRental()
    {
        SetupFacility(FacilityCode.BBQ);
        SetupUnique(true);
        Stall? stall = null;
        Contract? contract = null;
        _stallRepo.Setup(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()))
            .Callback<Stall, CancellationToken>((s, _) => stall = s).Returns(Task.CompletedTask);
        _stallRepo.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()))
            .Callback<Contract, CancellationToken>((c, _) => contract = c).Returns(Task.CompletedTask);

        var result = await Handler().Handle(
            new BulkImportStallholdersCommand(FacilityCode.BBQ, null, new List<ImportStallRow>
            {
                SpaceOnlyRow(1, "Joy Ruaza", "1", 1_600m),
                SpaceOnlyRow(2, "Mary May Benablo", "2", 3_200m),
            }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.CreatedCount);
        Assert.Equal(0, result.Value.FailedCount);

        // The rate is the figure the sheet states, on both the space and its occupancy.
        Assert.Equal(3_200m, stall!.MonthlyRate);
        Assert.Equal(3_200m, contract!.MonthlyRentalRate);
        Assert.Equal(3_200m, contract.ActualMonthlyRental);
        // Held without a signed contract: no name on one, and an open-ended term that never falls due for renewal.
        Assert.Equal(OccupancyArrangement.SpaceOnly, contract.Arrangement);
        Assert.Null(contract.NameOnContract);
        Assert.Equal(DomainRules.OpenEndedTermYears, contract.DurationYears);
    }

    [Fact]
    public async Task ARowNamingALeaseeButNoTerm_IsStillReported()
    {
        // The inference is narrow on purpose: a row that DOES name a leasee per contract but omits the number of
        // years is a missing figure, not a space held without a contract, and must still be reported.
        SetupFacility(FacilityCode.TCC);
        SetupUnique(true);

        var result = await Handler().Handle(
            new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow>
            {
                new(1, "Bernadette Miranda", "Bernadette Miranda", "4", new DateTime(2023, 6, 7), 0, null, 0m, 1_500m, null),
            }),
            CancellationToken.None);

        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Contains("duration", result.Value.Results.Single().Error);
    }

    [Fact]
    public async Task AnImportedRelet_EndsTheOutgoingTermTheDayBefore_AndRevokesItsPayorLinks()
    {
        // An imported row landing on a vacated number hands the space over, exactly as the add-vendor path does:
        // the outgoing occupancy is dated to the day before the incoming term, and any payor account still linked
        // to the space is revoked — otherwise the previous lessee's login sees and can pay the new lessee's dues.
        SetupFacility(FacilityCode.TCC);
        var existing = ExistingStall("5", "Old Tenant", active: false);
        SetupExisting(FacilityCode.TCC, null, existing);
        _stallRepo.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await Handler().Handle(
            new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow> { Row(1, "New Tenant", "5") }),
            CancellationToken.None);

        Assert.Equal(1, result.Value!.RenewedCount);
        var outgoing = existing.Contracts.Single();
        Assert.False(outgoing.IsActive);
        Assert.Equal(new DateOnly(2023, 6, 6), outgoing.EndedOn);   // the day before the imported effectivity
        _payorRepo.Verify(p => p.RemoveStallLinksAsync(existing.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ANewStall_DoesNotTouchPayorLinks()
    {
        // Only a hand-over revokes links. A brand-new space has none, and nothing else may be removed.
        SetupFacility(FacilityCode.TCC);
        SetupUnique(true);

        await Handler().Handle(
            new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow> { Row(1, "Juan Dela Cruz", "1") }),
            CancellationToken.None);

        _payorRepo.Verify(p => p.RemoveStallLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(FacilityCode.SLH)]
    [InlineData(FacilityCode.TRM)]
    [InlineData(FacilityCode.TPM)]
    public void Validator_RejectsUnsupportedFacilities(FacilityCode code)
    {
        var validator = new BulkImportStallholdersCommandValidator();
        var cmd = new BulkImportStallholdersCommand(code, null, new List<ImportStallRow> { Row(1, "Juan", "1") });

        var result = validator.Validate(cmd);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("only supported"));
    }

    [Fact]
    public void Validator_RequiresSectionForNpm()
    {
        var validator = new BulkImportStallholdersCommandValidator();
        var cmd = new BulkImportStallholdersCommand(FacilityCode.NPM, null, new List<ImportStallRow> { Row(1, "Juan", "1") });

        var result = validator.Validate(cmd);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("section"));
    }

    [Fact]
    public async Task AnUnNumberedSpace_IsAcceptedAndDoesNotOccupyAStallNumber()
    {
        // A commercial-centre space held on an extension has no number on the office's list. It used to be given the
        // next ordinary stall number, which then reported that number as occupied — so the office could no longer
        // register the actual stall of the same number. The two now coexist in one batch.
        SetupFacility(FacilityCode.TCC);
        SetupUnique(true);
        var cmd = new BulkImportStallholdersCommand(FacilityCode.TCC, null, new List<ImportStallRow>
        {
            Row(1, "Joseph Villamor", "4") with { Arrangement = OccupancyArrangement.SignedContract },
            Row(2, "Bernadette Lim", SpaceNumber.Format(1)) with { Arrangement = OccupancyArrangement.Extension },
            Row(3, "Jessie Navarro", SpaceNumber.Format(2)) with { Arrangement = OccupancyArrangement.SpaceOnly },
        });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.CreatedCount);
        Assert.All(result.Value!.Results, r => Assert.Null(r.Error));

        // The stall number the office issues and the identifiers for its un-numbered spaces are different values, so
        // neither can consume the other.
        var recorded = result.Value!.Results.Select(r => r.StallNo).ToList();
        Assert.Contains("4", recorded);
        Assert.Contains("SP-1", recorded);
        Assert.Contains("SP-2", recorded);
        Assert.Equal(recorded.Count, recorded.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
