using EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.SetSlaughterAnimalLabels;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Slaughterhouse;

public sealed class SlaughterAnimalNameIntegrityTests
{
    [Theory]
    [InlineData("Goat", "Goat")]
    [InlineData("goat", "GOAT")]
    public async Task BuiltInLabelCannotTakeAnExistingCustomName(string existingCustomName, string requestedLabel)
    {
        var validator = new SetSlaughterAnimalLabelsCommandValidator(
            new NamesProvider(SlaughterAnimalLabels.Canonical, [existingCustomName]));

        var result = await validator.ValidateAsync(
            new SetSlaughterAnimalLabelsCommand(requestedLabel, "Carabao", "Cow"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("existing custom animal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecordCannotUseConfiguredBuiltInLabelAsACustomName()
    {
        var validator = new RecordSlaughterCommandValidator(
            AvailableOrRepository(),
            new NamesProvider(new("Carmen Hog", "Carmen Carabao", "Carmen Cow"), []));

        var result = await validator.ValidateAsync(new RecordSlaughterCommand(
            "Owner", new DateOnly(2026, 9, 18), "OR-1", AnimalType.Other, "carmen hog", 1, 252m));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task RecordCannotIntroduceDifferentCasingForAnEstablishedCustomName()
    {
        var validator = new RecordSlaughterCommandValidator(
            AvailableOrRepository(),
            new NamesProvider(SlaughterAnimalLabels.Canonical, ["Goat"]));

        var result = await validator.ValidateAsync(new RecordSlaughterCommand(
            "Owner", new DateOnly(2026, 9, 18), "OR-2", AnimalType.Other, "GOAT", 1, 252m));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("Goat", "goat")]
    [InlineData("Goat", "GOAT")]
    public async Task UpdateCannotListOneCustomAnimalUnderCaseVariants(string first, string second)
    {
        var validator = new UpdateSlaughterCommandValidator(
            new NamesProvider(SlaughterAnimalLabels.Canonical, []));
        var command = new UpdateSlaughterCommand(
            "Owner",
            new DateOnly(2026, 9, 18),
            "OR-3",
            [new(AnimalType.Other, first, 1, 252m), new(AnimalType.Other, second, 1, 252m)]);

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("only once", StringComparison.Ordinal));
    }

    private static ISlaughterRepository AvailableOrRepository()
    {
        var repository = new Mock<ISlaughterRepository>();
        repository.Setup(x => x.IsORNumberAvailableForReceiptAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return repository.Object;
    }

    private sealed class NamesProvider(
        SlaughterAnimalLabels labels,
        IReadOnlyList<string> customNames) : ISlaughterAnimalLabelProvider
    {
        public Task<SlaughterAnimalLabels> GetAsync(CancellationToken ct = default) => Task.FromResult(labels);
        public Task<IReadOnlyList<string>> GetCustomNamesAsync(CancellationToken ct = default) =>
            Task.FromResult(customNames);
    }
}
