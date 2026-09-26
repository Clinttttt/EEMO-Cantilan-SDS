namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>Authority for settlement of one supported source row or source part.</summary>
public enum SettlementAuthority
{
    Legacy = 0,
    PendingCutover = 1,
    Canonical = 2
}
