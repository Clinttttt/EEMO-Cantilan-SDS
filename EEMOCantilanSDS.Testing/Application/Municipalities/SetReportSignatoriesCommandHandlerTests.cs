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
/// The signatory lines an LGU prints at the foot of its official sheets. Tenant-scoped, presentation-only, and
/// always recoverable: clearing the list restores the office's default trio rather than printing nothing.
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
        var saved = JsonSerializer.Deserialize<List<ReportSignatoryDto>>(lgu.ReportSignatories!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(2, saved.Count);
        Assert.Equal("Prepared by", saved[0].Caption);
        Assert.Equal("Ana Reyes", saved[0].Name);
        Assert.Equal("Certified correct", saved[1].Caption);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnEmptyList_RestoresTheOfficeDefault()
    {
        // Stored as null, not "[]" — the sheets must fall back to the standard trio, never print an empty foot.
        var (handler, lgu, _) = Build();
        lgu.SetReportSignatories("[{\"caption\":\"Prepared by\",\"name\":\"Someone\"}]");

        var result = await handler.Handle(new SetReportSignatoriesCommand(Array.Empty<ReportSignatoryDto>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(lgu.ReportSignatories);
    }

    [Fact]
    public async Task BlankLinesAreDropped_AndTheRowIsCapped()
    {
        var (handler, lgu, _) = Build();

        var many = Enumerable.Range(1, 9).Select(i => new ReportSignatoryDto($"Caption {i}", $"Name {i}")).ToList();
        many.Insert(0, new ReportSignatoryDto("   ", "  "));

        var result = await handler.Handle(new SetReportSignatoriesCommand(many), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = JsonSerializer.Deserialize<List<ReportSignatoryDto>>(lgu.ReportSignatories!,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
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

        Assert.Equal(400, result.StatusCode);
        Assert.Null(lgu.ReportSignatories);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
