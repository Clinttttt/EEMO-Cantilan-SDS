namespace EEMOCantilanSDS.Domain.Enums;

public enum AccountableDocumentState
{
    InOffice = 1,
    Assigned = 2,
    Consumed = 3,
    ReconciliationRequired = 4,
    Voided = 5,
    /// <summary>Reported lost or missing before use: blocked from issuance for good. It never returns to stock.</summary>
    Lost = 6
}
