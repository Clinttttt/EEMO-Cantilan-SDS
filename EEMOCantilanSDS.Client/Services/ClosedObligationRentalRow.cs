using EEMOCantilanSDS.Application.Dtos.Revenue;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>A read-only source-owned account, never a manufactured closed Stall.</summary>
public sealed record ClosedObligationRentalRow(string Code, string Unit, SpaceAccountPresentation Source, ObligationAccountDto Account)
{
    // A monthly space opens its own profile, where the lifecycle panel lives; an event lot has no lifecycle and keeps its workspace view.
    public string ProfileHref => Code == "FA" ? $"/vendors?workspace={Uri.EscapeDataString(Code)}&account={Account.Id}" : Source.ProfileHref(Account.Id);
}
