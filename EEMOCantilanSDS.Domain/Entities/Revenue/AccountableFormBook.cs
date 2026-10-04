using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// Office custody record for one received OR or CT numbered range. The range, its quantity and its serial pattern are the
/// facts the office registered; nothing about a booklet size is assumed.
/// </summary>
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

    /// <summary>
    /// Literal text printed after the digits of every serial in this range (for example " A"). It is part of the exact printed
    /// identity and is repeated unchanged; it is never interpreted (IA-059) and never confused with <see cref="FormVariant"/>.
    /// </summary>
    public string NumberSuffix { get; private set; } = string.Empty;

    /// <summary>The printed form designation when it is known and different from the base form (for example "51-A"); empty otherwise.</summary>
    public string FormVariant { get; private set; } = string.Empty;

    /// <summary>The date the office states the stock was received; null for a range recorded before this was captured.</summary>
    public DateOnly? ReceivedOn { get; private set; }

    /// <summary>Who the stock came from for accountability, as text (for example "Municipal Treasurer"). Provenance only: no approval is implied.</summary>
    public string? SourceAuthority { get; private set; }

    /// <summary>An upstream issue / requisition reference, when the office has one.</summary>
    public string? SourceReference { get; private set; }

    public int Quantity => checked((int)(LastSerialNumber - FirstSerialNumber + 1));

    private AccountableFormBook() { }

    public static AccountableFormBook Receive(
        Guid municipalityId, RevenueInstrumentType instrumentType, string seriesName,
        string numberPrefix, long firstSerialNumber, long lastSerialNumber, int serialWidth,
        DateTime receivedAtUtc, string receivedByActorId, string createdBy) =>
        Receive(municipalityId, instrumentType, seriesName, numberPrefix, string.Empty, string.Empty, firstSerialNumber,
            lastSerialNumber, serialWidth, receivedAtUtc, null, null, null, receivedByActorId, createdBy);

    public static AccountableFormBook Receive(
        Guid municipalityId, RevenueInstrumentType instrumentType, string seriesName,
        string numberPrefix, string numberSuffix, string formVariant, long firstSerialNumber, long lastSerialNumber, int serialWidth,
        DateTime receivedAtUtc, DateOnly? receivedOn, string? sourceAuthority, string? sourceReference,
        string receivedByActorId, string createdBy)
    {
        if (municipalityId == Guid.Empty)
            throw new ArgumentException("Municipality is required.", nameof(municipalityId));
        if (!Enum.IsDefined(instrumentType))
            throw new ArgumentOutOfRangeException(nameof(instrumentType));
        if (string.IsNullOrWhiteSpace(seriesName) || seriesName.Trim().Length > 100)
            throw new ArgumentException("Series name is required and must not exceed 100 characters.", nameof(seriesName));
        if (numberPrefix is null || numberPrefix.Length > 30)
            throw new ArgumentException("Number prefix must not exceed 30 characters.", nameof(numberPrefix));
        if (numberSuffix is null || numberSuffix.Length > 30)
            throw new ArgumentException("Number suffix must not exceed 30 characters.", nameof(numberSuffix));
        var variant = (formVariant ?? string.Empty).Trim();
        if (variant.Length > 20)
            throw new ArgumentException("Form variant must not exceed 20 characters.", nameof(formVariant));
        if (firstSerialNumber < 0 || lastSerialNumber < firstSerialNumber)
            throw new ArgumentOutOfRangeException(nameof(lastSerialNumber));
        if (serialWidth is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(serialWidth));
        if (receivedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Receipt time must be UTC.", nameof(receivedAtUtc));
        if (string.IsNullOrWhiteSpace(receivedByActorId) || receivedByActorId.Trim().Length > 100)
            throw new ArgumentException("Receiving actor is required and must not exceed 100 characters.", nameof(receivedByActorId));
        var authority = string.IsNullOrWhiteSpace(sourceAuthority) ? null : sourceAuthority.Trim();
        var reference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim();
        if (authority?.Length > 100 || reference?.Length > 100)
            throw new ArgumentException("Source and reference must not exceed 100 characters.");

        return new AccountableFormBook
        {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, InstrumentType = instrumentType,
            SeriesName = seriesName.Trim(), NumberPrefix = numberPrefix, NumberSuffix = numberSuffix, FormVariant = variant,
            FirstSerialNumber = firstSerialNumber, LastSerialNumber = lastSerialNumber,
            SerialWidth = serialWidth, ReceivedAtUtc = receivedAtUtc, ReceivedOn = receivedOn,
            SourceAuthority = authority, SourceReference = reference,
            ReceivedByActorId = receivedByActorId.Trim(), CreatedAt = receivedAtUtc, CreatedBy = createdBy
        };
    }

    /// <summary>The exact printed identity of one serial in this range: prefix + zero-padded number + suffix.</summary>
    public string FormatNumber(long serialNumber)
    {
        if (serialNumber < FirstSerialNumber || serialNumber > LastSerialNumber)
            throw new ArgumentOutOfRangeException(nameof(serialNumber), "Serial is outside the received book range.");
        return NumberPrefix + serialNumber.ToString($"D{SerialWidth}", System.Globalization.CultureInfo.InvariantCulture) + NumberSuffix;
    }
}
