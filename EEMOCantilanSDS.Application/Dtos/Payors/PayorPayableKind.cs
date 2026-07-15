namespace EEMOCantilanSDS.Application.Dtos.Payors;

/// <summary>
/// What a payable line is, so the payor UI + initiate know what to charge/settle. NPM has two distinct
/// payable items for the same stall + month (daily fees vs the utility bill), so a facility code alone
/// isn't enough to disambiguate them.
/// </summary>
public enum PayorPayableKind
{
    Monthly = 0,      // monthly-rental PaymentRecord (TCC/NCC/BBQ/ICE)
    NpmDaily = 1,     // NPM daily base fees for the month
    NpmUtility = 2    // NPM electricity + water bill for the month
}
