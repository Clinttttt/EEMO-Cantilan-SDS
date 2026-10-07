using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

public enum FishMeatVendorType { Fish = 1, Meat = 2 }
public enum VendorRegistrationKind { New = 1, Renew = 2 }
public enum VendorRegistrationStatus { Active = 1, Closed = 2 }
public enum TerminalSection { ComfortRoom = 1, PullPulVansCargoVans = 2, Tricycad = 3 }

/// <summary>IA-068 source-owned registration. Never inferred from an NPM occupant or a matching name.</summary>
public sealed class FishMeatVendorRegistration : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid ClientOperationId { get; private set; }
    public int TaxYear { get; private set; }
    public FishMeatVendorType VendorType { get; private set; }
    public VendorRegistrationKind RegistrationKind { get; private set; }
    public string DisplayName { get; private set; } = "";
    public string? BusinessName { get; private set; }
    public string? Address { get; private set; }
    public string? Reference { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    public VendorRegistrationStatus Status { get; private set; } = VendorRegistrationStatus.Active;
    public Guid? PriorRegistrationId { get; private set; }
    public Guid? CloseClientOperationId { get; private set; }
    public DateOnly? ClosedOn { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ClosedBy { get; private set; }
    public string? CloseNote { get; private set; }
    private FishMeatVendorRegistration() { }
    public void Close(Guid operation, DateOnly businessDate, string actor, string? note)
    {
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        actor = actor.Trim();
        if (operation == Guid.Empty || actor.Length is < 1 or > 100 || note?.Length > 300)
            throw new ArgumentException("InvalidCloseIntent");
        if (Status == VendorRegistrationStatus.Closed) throw new InvalidOperationException("RegistrationClosed");
        Status = VendorRegistrationStatus.Closed;
        CloseClientOperationId = operation;
        ClosedOn = businessDate;
        // PostgreSQL preserves microseconds; first-response audit time must equal its persisted replay value.
        var now = DateTime.UtcNow;
        ClosedAtUtc = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        ClosedBy = actor;
        CloseNote = note;
    }
    public static FishMeatVendorRegistration Renew(FishMeatVendorRegistration prior, Guid operation, int year,
        string? name, string? business, string? address, string? reference, string actor)
    {
        if (year <= prior.TaxYear) throw new ArgumentException("InvalidRenewalYear");
        var next = Register(prior.MunicipalityId, operation, year, prior.VendorType, VendorRegistrationKind.Renew,
            name ?? prior.DisplayName, business ?? prior.BusinessName, address ?? prior.Address, reference ?? prior.Reference, actor);
        next.PriorRegistrationId = prior.Id;
        return next;
    }
    public static FishMeatVendorRegistration Register(Guid tenant, Guid operation, int year, FishMeatVendorType type,
        VendorRegistrationKind kind, string name, string? business, string? address, string? reference, string actor)
    {
        if (tenant == Guid.Empty || operation == Guid.Empty || year is < 2000 or > 2200 || !Enum.IsDefined(type) || !Enum.IsDefined(kind))
            throw new ArgumentException("InvalidRegistration");
        static string? Text(string? value, int max)
        {
            var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (text?.Length > max) throw new ArgumentException("InvalidRegistration");
            return text;
        }
        return new() { Id = Guid.NewGuid(), MunicipalityId = tenant, ClientOperationId = operation, TaxYear = year,
            VendorType = type, RegistrationKind = kind, DisplayName = Text(name, 200) ?? throw new ArgumentException("RequiresPayerSnapshot"),
            BusinessName = Text(business, 200), Address = Text(address, 300), Reference = Text(reference, 200),
            CreatedBy = Text(actor, 100) ?? throw new ArgumentException("InvalidActor"), RecordedAtUtc = DateTime.UtcNow };
    }
}
