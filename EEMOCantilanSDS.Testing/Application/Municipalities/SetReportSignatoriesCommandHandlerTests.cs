using System.Text.Json;
using EEMOCantilanSDS.Application.Command.Municipalities.SetReportSignatories;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The signatory lines an LGU prints at the foot of its official sheets. Tenant-scoped and presentation-only.
///
/// <para>
/// THREE intentions, and the two empty ones are not the same. Null restores the office's default trio; an empty list
/// means the office wants no signatory lines at all; a populated list is those lines. They used to share one value, so
/// removing the last line put three back and an office could not choose to print a sheet without a footer.
/// </para>
///
/// <para>
/// The stored value is an object carrying the lines AND their alignment, so one save writes one value and the two cannot
/// disagree. Values written before alignment existed are bare arrays and the reader still understands them.
/// </para>
/// </summary>
public class SetReportSignatoriesCommandHandlerTests
{
    private static (SetReportSignatoriesCommandHandler handler, Municipality lgu, Mock<IUnitOfWork> uow) Build()
    {
        var lgu = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active);
        var repo = new Mock<IMunicipalityRepository>();
        var uow = new Mock<IUnitOfWork>();
        var tenant = new Mock<ITenantContext>();
        var user = new Mock<ICurrentUserService>();

        repo.Setup(r => r.GetByIdentifierAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(lgu);
        tenant.SetupGet(t => t.TenantCode).Returns("cantilan");
        user.SetupGet(u => u.Username).Returns("head");

        return (new SetReportSignatoriesCommandHandler(repo.Object, uow.Object, tenant.Object, user.Object), lgu, uow);
    }

    [Fact]
    public async Task Saves_TheLinesInOrder_Trimmed()
    {
        var (handler, lgu, uow) = Build();

        var result = await handler.Handle(new SetReportSignatoriesCommand(new[]
        {
            new ReportSignatoryDto("  Prepared by ", " Ana Reyes "),
            new ReportSignatoryDto("Certified correct", "Municipal Treasurer"),
        }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = Stored(lgu);
        Assert.Equal(2, saved.Count);
        Assert.Equal("Prepared by", saved[0].Caption);
        Assert.Equal("Ana Reyes", saved[0].Name);
        Assert.Equal("Certified correct", saved[1].Caption);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Reads the lines back out of the stored object, which is what the column now holds.</summary>
    private static List<ReportSignatoryDto> Stored(Municipality lgu)
    {
        using var doc = JsonDocument.Parse(lgu.ReportSignatories!);
        return JsonSerializer.Deserialize<List<ReportSignatoryDto>>(
            doc.RootElement.GetProperty("Lines").GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    [Fact]
    public async Task NULLRestoresTheOfficeDefault()
    {
        // Stored as null, so the sheets fall back to the standard trio. This is the "I did not mean to customise this"
        // case, and it is the ONLY one that clears the column.
        var (handler, lgu, _) = Build();
        lgu.SetReportSignatories("[{\"caption\":\"Prepared by\",\"name\":\"Someone\"}]");

        var result = await handler.Handle(new SetReportSignatoriesCommand(null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(lgu.ReportSignatories);
    }

    [Fact]
    public async Task AnEMPTYListMeansNoSignatoriesAtAll()
    {
        // The state that did not exist before: an office that wants a sheet with no footer. It is STORED - not cleared -
        // because clearing means "use the trio", and the two must stay tellable apart.
        var (handler, lgu, _) = Build();
        lgu.SetReportSignatories("[{\"caption\":\"Prepared by\",\"name\":\"Someone\"}]");

        var result = await handler.Handle(
            new SetReportSignatoriesCommand(Array.Empty<ReportSignatoryDto>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(lgu.ReportSignatories);
        Assert.Empty(Stored(lgu));
    }

    [Fact]
    public async Task TheALIGNMENTIsStoredWithTheLines()
    {
        var (handler, lgu, _) = Build();

        var result = await handler.Handle(new SetReportSignatoriesCommand(
            new[] { new ReportSignatoryDto("Received by", "Authorized Representative") },
            Align: "center"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(lgu.ReportSignatories!);
        Assert.Equal("center", doc.RootElement.GetProperty("Align").GetString());
        Assert.Single(Stored(lgu));
    }

    [Fact]
    public async Task AnUnknownAlignmentFallsBackToLeft()
    {
        // Anything the sheet cannot lay out becomes "left", so a bad value can never produce a footer nobody expects.
        var (handler, lgu, _) = Build();

        await handler.Handle(new SetReportSignatoriesCommand(
            new[] { new ReportSignatoryDto("Received by", "Rep") }, Align: "diagonal"), CancellationToken.None);

        using var doc = JsonDocument.Parse(lgu.ReportSignatories!);
        Assert.Equal("left", doc.RootElement.GetProperty("Align").GetString());
    }

    [Fact]
    public async Task BlankLinesAreDropped_AndTheRowIsCapped()
    {
        var (handler, lgu, _) = Build();

        var many = Enumerable.Range(1, 9).Select(i => new ReportSignatoryDto($"Caption {i}", $"Name {i}")).ToList();
        many.Insert(0, new ReportSignatoryDto("   ", "  "));

        var result = await handler.Handle(new SetReportSignatoriesCommand(many), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = Stored(lgu);
        Assert.Equal(SetReportSignatoriesCommandHandler.MaxSignatories, saved.Count);
        Assert.Equal("Caption 1", saved[0].Caption);           // the blank line was dropped, not counted
    }

    [Fact]
    public async Task AnOverlongLine_IsRefused_WithoutSaving()
    {
        var (handler, lgu, uow) = Build();

        var result = await handler.Handle(new SetReportSignatoriesCommand(new[]
        {
            new ReportSignatoryDto("Prepared by", new string('x', 61)),
        }), CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Null(lgu.ReportSignatories);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
