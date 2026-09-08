using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Queries.Stalls.GetStallHoldersList;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The stallholder roster refuses a year that is not a year, rather than throwing over it.
/// </summary>
/// <remarks>
/// Found by audit 2026-09-07: <c>?year=0</c> reached <c>DateOnly</c> and returned 500 from a report endpoint. The bounds and the wording
/// are taken from <c>GetCollectableDaysQueryHandler</c>, which had this rule already — two report queries should not disagree about what
/// a year is.
///
/// <para>WRITTEN BECAUSE MY OWN VERIFICATION PLAN DID NOT WORK. The commit that added the guard said it would be proved end to end
/// against production, on the grounds that a 400 in place of a 500 is the whole claim. It cannot be: authentication runs before the
/// handler, so an anonymous request returns 401 whatever the year, and an authenticated probe from here is not something to reach for.
/// The claim needed a test, so here it is.</para>
///
/// <para>The doubles throw if touched. That is the second half of the rule: a nonsense year must be refused BEFORE the query reaches the
/// cache or the database, or a caller could still spend the work and the cache entry on it.</para>
/// </remarks>
public class StallHoldersListYearBoundTests
{
    private static GetStallHoldersListQueryHandler HandlerThatRefusesToBeUsed()
    {
        var register = new Mock<IStallRegisterQueries>(MockBehavior.Strict);
        var cache = new Mock<IEemoAppCache>(MockBehavior.Strict);

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantCode).Returns("cantilan");

        return new GetStallHoldersListQueryHandler(register.Object, cache.Object, tenant.Object, new EemoCacheOptions());
    }

    /// <summary>A year outside the bounds is refused as invalid, and nothing downstream is asked.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1989)]
    [InlineData(2201)]
    [InlineData(-5)]
    public async Task AYearThatIsNotAYearIsRefused(int year)
    {
        var result = await HandlerThatRefusesToBeUsed().Handle(
            new GetStallHoldersListQuery(FacilityCode.NPM, Year: year), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal("That is not a real year.", result.Error);
    }

    /// <summary>
    /// A year inside the bounds, and no year at all, both get past the guard.
    /// </summary>
    /// <remarks>
    /// Asserted by the strict cache double THROWING when reached: the point is not what comes back but that the guard did not swallow a
    /// legitimate request. A guard that refused everything would satisfy the theory above and break the page.
    /// </remarks>
    [Theory]
    [InlineData(1990)]
    [InlineData(2019)]
    [InlineData(2200)]
    [InlineData(null)]
    public async Task ALegitimateYearIsNotRefusedByTheGuard(int? year)
    {
        var handler = HandlerThatRefusesToBeUsed();

        await Assert.ThrowsAnyAsync<Exception>(() => handler.Handle(
            new GetStallHoldersListQuery(FacilityCode.NPM, Year: year), CancellationToken.None));
    }
}
