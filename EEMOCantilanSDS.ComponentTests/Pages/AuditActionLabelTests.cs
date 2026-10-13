using EEMOCantilanSDS.Client.Services;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>An audit event's action is shown as readable words; the stored value is never changed and the three core actions keep their short label.</summary>
public sealed class AuditActionLabelTests
{
    [Theory]
    [InlineData("Created", "CREATED")]
    [InlineData("updated", "UPDATED")]
    [InlineData("Deleted", "DELETED")]
    [InlineData("WcfMobileCollectionEnabled", "WCF mobile collection enabled")]
    [InlineData("WCFMobileCollectionEnabled", "WCF mobile collection enabled")]
    [InlineData("WCFMOBILECOLLECTIONENABLED", "WCFMOBILECOLLECTIONENABLED")]   // an unbroken uppercase run carries no word boundary to read
    [InlineData("EcfActivated", "ECF activated")]
    [InlineData("NpmVendorLinkConfirmed", "NPM vendor link confirmed")]
    [InlineData("", "")]
    public void TheActionReadsAsWords(string stored, string expected) => Assert.Equal(expected, AuditActionLabel.For(stored));

    [Fact]
    public void OnlyThePlainCreateUpdateDeleteAreCore()
    {
        Assert.True(AuditActionLabel.IsCore("Created") && AuditActionLabel.IsCore("updated") && AuditActionLabel.IsCore("DELETED"));
        Assert.False(AuditActionLabel.IsCore("WcfMobileCollectionEnabled"));
    }
}
