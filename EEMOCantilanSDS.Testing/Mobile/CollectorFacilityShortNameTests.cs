using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Records;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The collector app labelled Records, Reports and Profile with the internal facility code ("TCC"), which on any office but
/// Cantilan is not the name the office uses. It now shows the office's own short name from the menu, as the Web portal does,
/// and keeps the code where the server sent none (an older server, a menu cached before the field existed).
/// </summary>
public class CollectorFacilityShortNameTests
{
    private static MobileFacilityMenuItemDto Facility(FacilityCode code, string? shortName) =>
        new(code, code.ToString(), string.Empty, true, true, ShortName: shortName);

    [Fact]
    public void Shows_the_offices_own_short_name() =>
        Assert.Equal("DTC", CollectorRecordLabels.FacilityShortName([Facility(FacilityCode.NCC, "DCC"), Facility(FacilityCode.TCC, "DTC")], FacilityCode.TCC));

    [Fact]
    public void Keeps_the_code_when_the_server_sent_no_short_name()
    {
        Assert.Equal("TCC", CollectorRecordLabels.FacilityShortName([Facility(FacilityCode.TCC, null)], FacilityCode.TCC));
        Assert.Equal("NCC", CollectorRecordLabels.FacilityShortName([Facility(FacilityCode.TCC, "DTC")], FacilityCode.NCC));
        Assert.Equal("NPM", CollectorRecordLabels.FacilityShortName(null, FacilityCode.NPM));
    }
}
