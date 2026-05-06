using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.TransportTerminal;

/// <summary>
/// Represents a registered transporter (driver) at the Transport Terminal.
/// Each trip they make costs ₱30 terminal fee.
/// </summary>
public class TrmTransporter : AuditableEntity
{
    public string Name { get; private set; } = string.Empty;
    public string Organization { get; private set; } = string.Empty;
    public string DefaultRoute { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public string? Remarks { get; private set; }

    // Navigation
    public ICollection<TrmTrip> Trips { get; private set; } = new List<TrmTrip>();

    private TrmTransporter() { } // EF Core

    public static TrmTransporter Create(
        string name,
        string organization,
        string defaultRoute,
        string? remarks = null,
        string createdBy = "System")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(organization))
            throw new ArgumentException("Organization is required.", nameof(organization));
        if (string.IsNullOrWhiteSpace(defaultRoute))
            throw new ArgumentException("Default route is required.", nameof(defaultRoute));

        return new TrmTransporter
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Organization = organization.Trim(),
            DefaultRoute = defaultRoute.Trim(),
            Remarks = remarks?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    public void UpdateDetails(string name, string organization, string defaultRoute, string? remarks, string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(organization))
            throw new ArgumentException("Organization is required.", nameof(organization));
        if (string.IsNullOrWhiteSpace(defaultRoute))
            throw new ArgumentException("Default route is required.", nameof(defaultRoute));

        Name = name.Trim();
        Organization = organization.Trim();
        DefaultRoute = defaultRoute.Trim();
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
