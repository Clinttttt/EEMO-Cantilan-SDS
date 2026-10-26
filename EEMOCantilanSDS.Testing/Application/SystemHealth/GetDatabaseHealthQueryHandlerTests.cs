using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Application.Queries.SystemHealth.GetDatabaseHealth;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>ST-11 #4: the shared database's load belongs to the platform operator, not to every LGU Head.</summary>
public class GetDatabaseHealthQueryHandlerTests
{
    private static GetDatabaseHealthQueryHandler Handler()
    {
        var repo = new Mock<IDatabaseHealthRepository>();
        repo.Setup(r => r.GetHealthAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DatabaseHealthDto(
            DatabaseSizeBytes: 9_000, ActiveConnections: 4, IdleConnections: 6, TotalConnections: 10, MaxConnections: 100,
            BlockedQueries: 1, Deadlocks: 2, CacheHitRatioPct: 99, LongestQuerySeconds: 3.5, UptimeSince: DateTime.UtcNow.AddDays(-3),
            CollectedAt: DateTime.UtcNow, CpuPercent: 40, MemoryPercent: 50, ProvisionedStorageBytes: 5_000, TenantSizeBytes: 700));
        return new GetDatabaseHealthQueryHandler(repo.Object);
    }

    [Fact]
    public async Task AnLguHeadSeesOnlyTheirOwnStorage()
    {
        var dto = (await Handler().Handle(new GetDatabaseHealthQuery(), CancellationToken.None)).Value!;

        Assert.Equal((700L, 5_000L), (dto.TenantSizeBytes, dto.ProvisionedStorageBytes));
        Assert.Equal((0L, 0, 0, 0L, 0), (dto.DatabaseSizeBytes, dto.TotalConnections, dto.BlockedQueries, dto.Deadlocks, dto.MaxConnections));
        Assert.Null(dto.UptimeSince);
        Assert.Null(dto.CpuPercent);
        Assert.Null(dto.MemoryPercent);
        Assert.Null(dto.LongestQuerySeconds);
    }

    [Fact]
    public async Task ThePlatformOperatorSeesTheSharedDatabase()
    {
        var dto = (await Handler().Handle(new GetDatabaseHealthQuery(PlatformWide: true), CancellationToken.None)).Value!;

        Assert.Equal((9_000L, 10, 2L, 40d), (dto.DatabaseSizeBytes, dto.TotalConnections, dto.Deadlocks, dto.CpuPercent));
    }
}
