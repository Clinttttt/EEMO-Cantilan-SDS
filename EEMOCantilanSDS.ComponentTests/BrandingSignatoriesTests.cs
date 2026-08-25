using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Services;
using Moq;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// How a stored signature strip is read back — and the three states it has to tell apart.
///
/// <para>
/// Removing the last signatory used to put the office's default trio straight back, because an empty list MEANT "not
/// customised" everywhere: in the client, in the command and in the column. An office that wanted a sheet with no footer
/// could not have one. There are now three states, and the one that matters most for safety is the third row below:
/// values written before any of this existed are bare JSON arrays, and they must keep meaning exactly what they meant.
/// </para>
/// </summary>
public class BrandingSignatoriesTests
{
    private static BrandingState WithStored(string? reportSignatories)
    {
        var state = new BrandingState(Mock.Of<IMunicipalitiesApiClient>());
        state.Apply(new MunicipalityBrandingDto(
            Code: "CANTILAN", TenantCode: "cantilan", Name: "Cantilan", Province: "Surigao del Sur",
            OfficeName: "Economic Enterprise & Management Office", SealPath: null,
            Status: "Active", IsActive: true, OfficeAcronym: "EEMO", Address: null,
            ReportSignatories: reportSignatories));

        return state;
    }

    [Fact]
    public void NothingStoredMeansTheOfficesDefaultLines()
    {
        // Two lines, not three. A "Received by" line went out unsigned on every sheet until the office pointed out that
        // nobody receives a report of collections from itself.
        var state = WithStored(null);

        Assert.Equal(2, state.Signatories.Count);
        Assert.Equal(new[] { "Prepared by", "Reviewed by" }, state.Signatories.Select(s => s.Caption));
        Assert.True(state.SignatoriesAreOfficeDefault);
        Assert.False(state.HasNoSignatories);
        Assert.Equal("left", state.SignatoryAlignment);
    }

    [Fact]
    public void AnEMPTYLinesArrayMeansNoSignatoriesAtAll()
    {
        // The state that did not exist before. It is a CHOICE, so it must not be mistaken for the absence of one -
        // otherwise the trio returns and the office can never print a sheet without a footer.
        var state = WithStored("{\"Align\":\"left\",\"Lines\":[]}");

        Assert.Empty(state.Signatories);
        Assert.True(state.HasNoSignatories);
        Assert.False(state.SignatoriesAreOfficeDefault);   // nothing to "restore" from - this is deliberate
    }

    [Fact]
    public void ABAREARRAYWrittenBeforeAlignmentExistedStillReadsTheSame()
    {
        // The compatibility case, and the reason the reader accepts two shapes. Every LGU that had set its own lines
        // before this change has one of these in the column; it must keep its lines and print where it always printed.
        var state = WithStored("[{\"caption\":\"Prepared by\",\"name\":\"Ana Reyes\"}]");

        Assert.Single(state.Signatories);
        Assert.Equal("Prepared by", state.Signatories[0].Caption);
        Assert.Equal("Ana Reyes", state.Signatories[0].Name);
        Assert.Equal("left", state.SignatoryAlignment);
        Assert.False(state.SignatoriesAreOfficeDefault);
    }

    [Fact]
    public void TheStoredAlignmentIsRead()
    {
        var state = WithStored("{\"Align\":\"center\",\"Lines\":[{\"Caption\":\"Received by\",\"Name\":\"Rep\"}]}");

        Assert.Equal("center", state.SignatoryAlignment);
        Assert.Single(state.Signatories);
    }

    [Fact]
    public void AValueThatCannotBeReadNeverBlanksTheFooter()
    {
        // A sheet is an official document. Whatever is in the column, it must still carry signatories - so unreadable
        // falls back to the office's default lines rather than to nothing.
        var state = WithStored("{ this is not json");

        Assert.Equal(state.DefaultSignatories.Count, state.Signatories.Count);
        Assert.NotEmpty(state.Signatories);
        Assert.Equal("left", state.SignatoryAlignment);
    }
}
