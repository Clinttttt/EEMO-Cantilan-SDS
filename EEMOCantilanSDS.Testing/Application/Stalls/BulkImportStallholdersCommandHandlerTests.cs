using EEMOCantilanSDS.Application.Command.Stalls.BulkImportStallholders;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class BulkImportStallholdersCommandHandlerTests
{
    private readonly Mock<IStallRepository> _stallRepo = new();
    private readonly Mock<IFacilityRepository> _facilityRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private BulkImportStallholdersCommandHandler Handler()
        => new(_stallRepo.Object, _facilityRepo.Object, _uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

    private void SetupFacility(FacilityCode code)
        => _facilityRepo.Setup(r => r.GetByCodeAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Facility.Create(code, code.ToString(), code.ToString()));

    private void SetupUnique(bool unique)
        => _stallRepo.Setup(r => r.IsStallNoUniqueAsync(
                It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
    public async Task ExistingStallNo_IsReported_AndNothingSaved()
    {
        SetupFacility(FacilityCode.TCC);
        SetupUnique(false); // collides with an existing stall
        var cmd = new BulkImportStallholdersCommand(FacilityCode.TCC, null,
            new List<ImportStallRow> { Row(1, "Juan", "1") });

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.Equal(0, result.Value!.CreatedCount);
        Assert.Contains("already exists", result.Value.Results.Single().Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
}
