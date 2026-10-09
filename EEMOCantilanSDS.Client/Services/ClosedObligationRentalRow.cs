using EEMOCantilanSDS.Application.Dtos.Revenue;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>A read-only source-owned account, never a manufactured closed Stall.</summary>
public sealed record ClosedObligationRentalRow(string Code, string Unit, SpaceAccountPresentation Source, ObligationAccountDto Account)
{
    public string ProfileHref => $"/vendors?workspace={Uri.EscapeDataString(Code)}&account={Account.Id}";
}
