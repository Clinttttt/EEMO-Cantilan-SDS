using System;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Payors.GetMyPayorProfile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Payors;

/// <summary>
/// A payor reading their own details.
///
/// <para>
/// The portal's session is HttpOnly cookies by design, so the app holds no token it can read and cannot show a payor their
/// own name or number without asking. This answers, and only ever about the caller: there is no id to pass.
/// </para>
///
/// <para>
/// Both figures are the OFFICE's record, read from the row rather than from the token's claims — which is also why
/// activation stopped asking a payor to type a name. A correction in the register reaches the payor's own screen without
/// waiting for them to sign in again.
/// </para>
/// </summary>
public class MyPayorProfileHandlerTests
{
    private static readonly Guid CallerId = Guid.NewGuid();

    private static (GetMyPayorProfileQueryHandler handler, Mock<IPayorRepository> repo) Build(
        PayorUser? caller, Guid? actingId)
    {
        var repo = new Mock<IPayorRepository>();
        repo.Setup(r => r.GetPayorByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(caller);

        var current = new Mock<ICurrentUserService>();
        current.SetupGet(c => c.UserId).Returns(actingId);

        return (new GetMyPayorProfileQueryHandler(repo.Object, current.Object), repo);
    }

    [Fact]
    public async Task ItAnswersTheOfficesRecordOfTheCaller()
    {
        var payor = PayorUser.Create("Godon Lar", "09384326778", TestPasswords.Hash("Secret123!"));
        var (handler, repo) = Build(payor, CallerId);

        var result = await handler.Handle(new GetMyPayorProfileQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Godon Lar", result.Value!.FullName);
        Assert.Equal("09384326778", result.Value.ContactNumber);

        // Asked of the caller's own id, which came from the token: no other payor is reachable from here.
        repo.Verify(r => r.GetPayorByIdAsync(CallerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithNoSessionItAnswersNothing()
    {
        var (handler, repo) = Build(PayorUser.Create("Godon Lar", "09384326778", TestPasswords.Hash("Secret123!")), actingId: null);

        var result = await handler.Handle(new GetMyPayorProfileQuery(), CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        repo.Verify(r => r.GetPayorByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnAccountThatNoLongerExistsIsNotAnswered()
    {
        // A cookie outliving its account: answered as no session rather than as an empty profile.
        var (handler, _) = Build(caller: null, CallerId);

        var result = await handler.Handle(new GetMyPayorProfileQuery(), CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }
}
