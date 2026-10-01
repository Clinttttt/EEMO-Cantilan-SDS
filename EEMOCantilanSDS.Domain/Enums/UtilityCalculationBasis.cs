namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>
/// How one utility part (electricity or water) of a UtilityBill was assessed. Metered is the original reading x rate
/// model. DirectApproved is the office's approved amount with no meter reading (IA-050): the amount is stored as one
/// unit at the approved amount so every existing charge, balance and report calculation stays exact, and this basis
/// tells every presentation that the readings are not evidence.
/// </summary>
public enum UtilityCalculationBasis
{
    Metered = 0,
    DirectApproved = 1
}
