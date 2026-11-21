using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;

namespace EEMOCantilanSDS.Testing;

/// <summary>A checkout reference begins with the payor's own office, never another LGU's name, and stays as short as PayMongo has always been sent.</summary>
public class OnlinePaymentReferenceTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 9, 30, 0);

    [Theory]
    [InlineData("DEMO", "DEMO-OP-20261121-")]
    [InlineData("cantilan", "CANTILAN-OP-20261121-")]
    [InlineData("san-isidro del norte", "SANISIDR-OP-20261121-")]   // letters and digits only, at most 8
    [InlineData(null, "OP-20261121-")]
    [InlineData("  ", "OP-20261121-")]
    public void Reference_begins_with_the_office_code(string? officeCode, string expectedStart)
    {
        var reference = InitiateOnlinePaymentCommandHandler.GenerateReference(officeCode, Now);

        Assert.StartsWith(expectedStart, reference);
        Assert.Equal(expectedStart.Length + 8, reference.Length);
        Assert.True(reference.Length <= 29);
        Assert.Matches("^[A-Z0-9-]+$", reference);
    }

    [Fact]
    public void Two_references_from_the_same_office_and_minute_differ()
    {
        Assert.NotEqual(InitiateOnlinePaymentCommandHandler.GenerateReference("DEMO", Now), InitiateOnlinePaymentCommandHandler.GenerateReference("DEMO", Now));
    }
}
