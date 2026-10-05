using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>Recorded checkout correlation only. Child Collections are the sole money authority.</summary>
public sealed class MobileCollectionSession : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid ClientCollectionSessionId { get; private set; }
    public Guid CollectorId { get; private set; }
    public Guid? PayorId { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string IntentFingerprint { get; private set; } = string.Empty;
    public string ResultJson { get; private set; } = string.Empty;
    private MobileCollectionSession() { }
    public static MobileCollectionSession Recorded(Guid tenant, Guid session, Guid collector, Guid? payor,
        DateOnly date, string fingerprint, string resultJson) => new()
        {
            MunicipalityId = tenant, ClientCollectionSessionId = session, CollectorId = collector,
            PayorId = payor, BusinessDate = date, IntentFingerprint = fingerprint, ResultJson = resultJson
        };
}
