using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Domain.Entities.TransportTerminal;

/// <summary>
/// Represents a single trip departure recorded at the Transport Terminal.
/// Each trip costs ₱30 terminal fee paid by the driver.
/// </summary>
public class TrmTrip : AuditableEntity, IMunicipalityOwned
{
    /// <inheritdoc />
    public Guid MunicipalityId { get; private set; }
    public Guid? TransporterId { get; private set; }
    public Guid? CollectorId { get; private set; }
    public int TripNumber { get; private set; }
    public string DriverName { get; private set; } = string.Empty;
    public string PlateNumber { get; private set; } = string.Empty;
    public string Organization { get; private set; } = "Non-associated";
    public string Route { get; private set; } = string.Empty;
    public decimal Fee { get; private set; } = FeeRates.TrmTripFee;
    public string? ORNumber { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public string? Remarks { get; private set; }

    // Offline-sync idempotency key from the mobile client (null for online records).
    public Guid? ClientOperationId { get; private set; }

    // Navigation
    public TrmTransporter? Transporter { get; private set; }

    private TrmTrip() { } // EF Core

    public static TrmTrip Create(
        Guid? transporterId,
        int tripNumber,
        string driverName,
        string plateNumber,
        string route,
        string orNumber,
        string? organization = null,
        Guid? collectorId = null,
        string? remarks = null,
        string createdBy = "System",
        DateTime? recordedAt = null)
    {
        if (string.IsNullOrWhiteSpace(driverName))
            throw new ArgumentException("Driver name is required.", nameof(driverName));
        if (string.IsNullOrWhiteSpace(plateNumber))
            throw new ArgumentException("Plate number is required.", nameof(plateNumber));
        if (string.IsNullOrWhiteSpace(route))
            throw new ArgumentException("Route is required.", nameof(route));
        if (string.IsNullOrWhiteSpace(orNumber))
            throw new ArgumentException("OR number is required.", nameof(orNumber));

        return new TrmTrip
        {
            Id = Guid.NewGuid(),
            TransporterId = transporterId,
            CollectorId = collectorId,
            TripNumber = tripNumber,
            DriverName = driverName.Trim(),
            PlateNumber = plateNumber.Trim().ToUpper(),
            Organization = string.IsNullOrWhiteSpace(organization) ? "Non-associated" : organization.Trim(),
            Route = route.Trim(),
            Fee = FeeRates.TrmTripFee,
            ORNumber = orNumber.Trim(),
            RecordedAt = recordedAt ?? DateTime.UtcNow,
            Remarks = remarks?.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    public void UpdateORNumber(string orNumber, string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(orNumber))
            throw new ArgumentException("OR number is required.", nameof(orNumber));

        ORNumber = orNumber.Trim();
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>Stamps the offline-sync idempotency key (set once when replaying a queued offline record).</summary>
    public void SetClientOperationId(Guid clientOperationId) => ClientOperationId = clientOperationId;
}
