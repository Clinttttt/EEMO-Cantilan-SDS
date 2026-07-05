using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Domain.Entities.TaboanMarket;

/// <summary>
/// Represents a vendor's attendance record for a specific Friday market day.
/// Each vendor pays ₱100 per market day.
/// </summary>
public class TpmAttendance : AuditableEntity, IMunicipalityOwned
{
    /// <inheritdoc />
    public Guid MunicipalityId { get; private set; }
    public Guid VendorId { get; private set; }
    public Guid? CollectorId { get; private set; }
    public DateOnly MarketDate { get; private set; }
    public decimal Fee { get; private set; } = FeeRates.TpmVendorFee;
    public bool IsPaid { get; private set; }
    public string? ORNumber { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public string? Remarks { get; private set; }

    // Offline-sync idempotency key from the mobile client (null for online records).
    public Guid? ClientOperationId { get; private set; }

    // Navigation
    public TpmVendor? Vendor { get; private set; }

    private TpmAttendance() { } // EF Core

    public static TpmAttendance Create(
        Guid vendorId,
        DateOnly marketDate,
        string createdBy = "System",
        decimal? fee = null)
    {
        if (marketDate.DayOfWeek != DayOfWeek.Friday)
            throw new ArgumentException("Market date must be a Friday.", nameof(marketDate));

        return new TpmAttendance
        {
            Id = Guid.NewGuid(),
            VendorId = vendorId,
            MarketDate = marketDate,
            Fee = fee ?? FeeRates.TpmVendorFee,
            IsPaid = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    public void MarkPaid(
        Guid? collectorId,
        string? remarks = null,
        string updatedBy = "System")
    {
        IsPaid = true;
        CollectorId = collectorId;
        PaidAt = DateTime.UtcNow;
        Remarks = remarks?.Trim();
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void MarkUnpaid(string updatedBy = "System")
    {
        IsPaid = false;
        CollectorId = null;
        PaidAt = null;
        ORNumber = null;
        Remarks = null;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void SetORNumber(
        string orNumber,
        string updatedBy = "System")
    {
        if (!IsPaid)
            throw new InvalidOperationException("Cannot set OR number for unpaid attendance.");

        if (string.IsNullOrWhiteSpace(orNumber))
            throw new ArgumentException("OR number is required.", nameof(orNumber));

        ORNumber = orNumber.Trim();
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    /// <summary>Stamps the offline-sync idempotency key (set once when replaying a queued offline record).</summary>
    public void SetClientOperationId(Guid clientOperationId) => ClientOperationId = clientOperationId;
}
