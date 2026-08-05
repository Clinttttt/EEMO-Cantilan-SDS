using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A status word is not a person. The office's source lists reuse the occupant column to say a space is not
/// occupied, and three New Public Market contracts were imported with the literal word "Closed" as the occupant
/// while the real lessee's name sat on the contract line — so the Register of Inactive Stall Accounts printed
/// "Closed" where a name belongs, in a document signed by the head of office.
/// </summary>
public class OccupantNameTests
{
    [Theory]
    [InlineData("Closed")]
    [InlineData("closed")]
    [InlineData("  CLOSED  ")]
    [InlineData("Vacant")]
    [InlineData("N/A")]
    [InlineData("Unoccupied")]
    public void AStatusWordInTheOccupantColumn_FallsBackToTheNameOnTheContract(string stored)
    {
        Assert.Equal("Gloria B. Erman", OccupantName.Resolve(stored, "Gloria B. Erman"));
    }

    [Fact]
    public void ARealNameIsKeptExactly()
    {
        Assert.Equal("Merlita A. Abuso", OccupantName.Resolve("Merlita A. Abuso", "Someone Else"));
    }

    [Fact]
    public void ABlankOccupantFallsBackToTheContractName()
    {
        Assert.Equal("Catherine U. Bruzon", OccupantName.Resolve("   ", "Catherine U. Bruzon"));
    }

    [Fact]
    public void WhenNeitherStatesAPerson_TheCallerDecidesTheWording()
    {
        // Empty rather than a guess: the register says "Unnamed occupant", the queue says something else, and
        // neither should be baked in here.
        Assert.Equal(string.Empty, OccupantName.Resolve("Closed", "Vacant"));
        Assert.Equal(string.Empty, OccupantName.Resolve(null, null));
    }

    [Fact]
    public void ANameThatMerelyContainsAStatusWordIsNotTouched()
    {
        // Only an exact match is a status. Real people are named things like this.
        Assert.Equal("Close Ann Reyes", OccupantName.Resolve("Close Ann Reyes", null));
        Assert.Equal("Nadine Vacantes", OccupantName.Resolve("Nadine Vacantes", null));
    }
}
