namespace EEMOCantilanSDS.Client.Services;

/// <summary>A read-only display row with its original typed source item. Never a Stall DTO adapter.</summary>
public sealed record RentalTableRow<T>(T Source, string Number, string Occupant, string Contract, string StartDate,
    decimal? Amount, string PaymentStatus, string LifecycleStatus = "Active", string? Context = null);
