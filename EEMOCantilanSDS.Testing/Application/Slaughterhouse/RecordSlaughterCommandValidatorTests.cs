using EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Slaughterhouse;

public class RecordSlaughterCommandValidatorTests
{
    private static RecordSlaughterCommand ValidCommand(string orNumber) =>
        new(
            OwnerName: "Juan Dela Cruz",
            TransactionDate: new DateOnly(2026, 6, 6),
            ORNumber: orNumber,
            AnimalType: AnimalType.Hog,
            CustomAnimalType: null,
            NumberOfHeads: 2,
            CustomRate: null);

    [Fact]
    public async Task UniqueOrNumber_PassesValidation()
    {
        var repo = new Mock<ISlaughterRepository>();
        repo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var validator = new RecordSlaughterCommandValidator(repo.Object);

        var result = await validator.ValidateAsync(ValidCommand("OR-NEW"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task DuplicateOrNumber_FailsValidation()
    {
        var repo = new Mock<ISlaughterRepository>();
        repo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var validator = new RecordSlaughterCommandValidator(repo.Object);

        var result = await validator.ValidateAsync(ValidCommand("OR-DUP"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RecordSlaughterCommand.ORNumber)
            && e.ErrorMessage == "OR number already exists.");
    }
}
