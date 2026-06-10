using EEMOCantilanSDS.Application.Command.Suggestions.HideSuggestion;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Suggestions;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class HideSuggestionCommandHandlerTests
{
    private static (HideSuggestionCommandHandler handler, Mock<ISuggestionRepository> repo, Mock<IUnitOfWork> uow) Build(
        IReadOnlySet<string> alreadyHidden)
    {
        var repo = new Mock<ISuggestionRepository>();
        var uow = new Mock<IUnitOfWork>();
        var currentUser = new Mock<ICurrentUserService>();

        repo.Setup(r => r.GetHiddenValuesAsync(It.IsAny<SuggestionType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(alreadyHidden);
        currentUser.SetupGet(u => u.Username).Returns("admin");

        return (new HideSuggestionCommandHandler(repo.Object, currentUser.Object, uow.Object), repo, uow);
    }

    [Fact]
    public async Task NewValue_IsAdded_AndSaved()
    {
        var (handler, repo, uow) = Build(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var result = await handler.Handle(new HideSuggestionCommand(SuggestionType.TrmDriver, "  Juan Cruz  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Value is trimmed before persisting.
        repo.Verify(r => r.AddAsync(It.Is<HiddenSuggestion>(h => h.Value == "Juan Cruz" && h.Type == SuggestionType.TrmDriver), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AlreadyHidden_IsIdempotent_NoDuplicateAdded()
    {
        var (handler, repo, uow) = Build(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Vegetables" });

        var result = await handler.Handle(new HideSuggestionCommand(SuggestionType.TpmGoods, "vegetables"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddAsync(It.IsAny<HiddenSuggestion>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
