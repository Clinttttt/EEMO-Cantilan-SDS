using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

public sealed class TenantTerminalSectionSettingsTests
{
    private static readonly Guid Cantilan = Guid.NewGuid();
    private static readonly Guid Carmen = Guid.NewGuid();

    [Fact]
    public void PreconfigurationTenants_KeepThreeLegacySectionsEnabled()
    {
        var rows = TerminalSectionSettingsStore.Project([]);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.True(row.IsEnabled));
        Assert.All(rows, row => Assert.Equal(0, row.Version));
    }

    [Fact]
    public void ExplicitlyDisabledSections_DoNotBecomeEnabledByFallback()
    {
        var settings = new [] {
            TerminalSectionSetting.Create(TerminalSection.ComfortRoom, false, "Public toilet", Carmen, "Head"),
            TerminalSectionSetting.Create(TerminalSection.PullPulVansCargoVans, true, "Van station", Carmen, "Head"),
            TerminalSectionSetting.Create(TerminalSection.Tricycad, false, null, Carmen, "Head"),
        };
        var rows = TerminalSectionSettingsStore.Project(settings);
        Assert.Single(rows, row => row.IsEnabled);
        Assert.Equal("Van station", rows.Single(row => row.IsEnabled).DisplayName);
        Assert.False(rows.Single(row => row.Section == TerminalSection.ComfortRoom).IsEnabled);
        Assert.Equal("Public toilet", rows.Single(row => row.Section == TerminalSection.ComfortRoom).DisplayName);
    }

    [Fact]
    public void UpdatesPreserveStableIdentityAndIncreaseVersionOnlyOnChange()
    {
        var row = TerminalSectionSetting.Create(TerminalSection.ComfortRoom, true, null, Cantilan, "Activation");
        row.Update(true, null, "Head");
        Assert.Equal(1, row.Version);
        row.Update(false, "Public comfort room", "Head");
        Assert.Equal(2, row.Version);
        Assert.Equal(TerminalSection.ComfortRoom, row.Section);
        Assert.Equal(Cantilan, row.MunicipalityId);
        Assert.False(row.IsEnabled);
    }

    [Fact]
    public void InvalidSectionNamesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => TerminalSectionSetting.NormalizeName(new string('x', 81)));
        Assert.Throws<ArgumentException>(() => TerminalSectionSetting.NormalizeName("Bad\nName"));
        Assert.Throws<ArgumentException>(() => TerminalSectionSetting.Create((TerminalSection)99, true, null, Carmen, "Head"));
    }
}
