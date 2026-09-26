using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>Office custody record for one received OR or CT numbered range.</summary>
public sealed class AccountableFormBook : AuditableEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public RevenueInstrumentType InstrumentType { get; private set; }
    public string SeriesName { get; private set; } = string.Empty;
    public string NumberPrefix { get; private set; } = string.Empty;
    public long FirstSerialNumber { get; private set; }
    public long LastSerialNumber { get; private set; }
    public int SerialWidth { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public string ReceivedByActorId { get; private set; } = string.Empty;

    private AccountableFormBook() { }

    public static AccountableFormBook Receive(
        Guid municipalityId, RevenueInstrumentType instrumentType, string seriesName,
        string numberPrefix, long firstSerialNumber, long lastSerialNumber, int serialWidth,
        DateTime receivedAtUtc, string receivedByActorId, string createdBy)
    {
        if (municipalityId == Guid.Empty)
            throw new ArgumentException("Municipality is required.", nameof(municipalityId));
        if (!Enum.IsDefined(instrumentType))
            throw new ArgumentOutOfRangeException(nameof(instrumentType));
        if (string.IsNullOrWhiteSpace(seriesName) || seriesName.Trim().Length > 100)
            throw new ArgumentException("Series name is required and must not exceed 100 characters.", nameof(seriesName));
        if (numberPrefix is null || numberPrefix.Length > 30)
            throw new ArgumentException("Number prefix must not exceed 30 characters.", nameof(numberPrefix));
        if (firstSerialNumber < 0 || lastSerialNumber < firstSerialNumber)
            throw new ArgumentOutOfRangeException(nameof(lastSerialNumber));
        if (serialWidth is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(serialWidth));
        if (receivedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Receipt time must be UTC.", nameof(receivedAtUtc));
        if (string.IsNullOrWhiteSpace(receivedByActorId) || receivedByActorId.Trim().Length > 100)
            throw new ArgumentException("Receiving actor is required and must not exceed 100 characters.", nameof(receivedByActorId));

        return new AccountableFormBook
        {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, InstrumentType = instrumentType,
            SeriesName = seriesName.Trim(), NumberPrefix = numberPrefix,
            FirstSerialNumber = firstSerialNumber, LastSerialNumber = lastSerialNumber,
            SerialWidth = serialWidth, ReceivedAtUtc = receivedAtUtc,
            ReceivedByActorId = receivedByActorId.Trim(), CreatedAt = receivedAtUtc, CreatedBy = createdBy
        };
    }

    public string FormatNumber(long serialNumber)
    {
        if (serialNumber < FirstSerialNumber || serialNumber > LastSerialNumber)
            throw new ArgumentOutOfRangeException(nameof(serialNumber), "Serial is outside the received book range.");
        return NumberPrefix + serialNumber.ToString($"D{SerialWidth}", System.Globalization.CultureInfo.InvariantCulture);
    }
}
