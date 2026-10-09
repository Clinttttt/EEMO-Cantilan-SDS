using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>Editable presentation state, not a registration command or financial source.</summary>
public interface IRentalRegistrationState
{
    string ActualOccupant { get; set; }
    string ContractName { get; set; }
    OccupancyArrangement Arrangement { get; set; }
    bool HasSignedContract { get; }
    decimal MonthlyRate { get; set; }
}

public sealed class ObligationRentalRegistrationState(Func<string> occupant, Action<string> setOccupant,
    Func<OccupancyArrangement> basis, Action<OccupancyArrangement> setBasis,
    Func<decimal> amount, Action<decimal> setAmount) : IRentalRegistrationState
{
    public string ActualOccupant { get => occupant(); set => setOccupant(value); }
    public string ContractName { get => string.Empty; set => throw new NotSupportedException("Contract name is not stored by this source."); }
    public OccupancyArrangement Arrangement { get => basis(); set => setBasis(value); }
    public bool HasSignedContract => Arrangement == OccupancyArrangement.SignedContract;
    public decimal MonthlyRate { get => amount(); set => setAmount(value); }
}
