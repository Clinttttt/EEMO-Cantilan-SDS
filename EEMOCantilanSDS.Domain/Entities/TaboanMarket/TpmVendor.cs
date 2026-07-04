using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.TaboanMarket;

/// <summary>
/// Represents a vendor registered for Tabo-an Public Market (TPM).
/// TPM operates every Friday where vendors pay ₱100 per market day.
/// </summary>
public class TpmVendor : AuditableEntity, IMunicipalityOwned
{
    /// <inheritdoc />
    public Guid MunicipalityId { get; private set; }

    public string VendorName { get; private set; } = string.Empty;
    public string Goods { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public string? ContactNumber { get; private set; }
    public string? Remarks { get; private set; }

    // Navigation
    public ICollection<TpmAttendance> Attendances { get; private set; } = new List<TpmAttendance>();

    private TpmVendor() { } // EF Core

    public static TpmVendor Create(
        string vendorName,
        string goods,
        string? contactNumber = null,
        string? remarks = null,
        string createdBy = "System")
    {
        if (string.IsNullOrWhiteSpace(vendorName))
            throw new ArgumentException("Vendor name is required.", nameof(vendorName));

        if (string.IsNullOrWhiteSpace(goods))
            throw new ArgumentException("Goods/product is required.", nameof(goods));

        return new TpmVendor
        {
            Id = Guid.NewGuid(),
            VendorName = vendorName.Trim(),
            Goods = goods.Trim(),
            ContactNumber = contactNumber?.Trim(),
            Remarks = remarks?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    public void UpdateDetails(
        string vendorName,
        string goods,
        string? contactNumber,
        string? remarks,
        string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(vendorName))
            throw new ArgumentException("Vendor name is required.", nameof(vendorName));

        if (string.IsNullOrWhiteSpace(goods))
            throw new ArgumentException("Goods/product is required.", nameof(goods));

        VendorName = vendorName.Trim();
        Goods = goods.Trim();
        ContactNumber = contactNumber?.Trim();
        Remarks = remarks?.Trim();
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void Deactivate(string updatedBy)
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }

    public void Activate(string updatedBy)
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
