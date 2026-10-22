using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

public enum FishMeatVendorType { Fish = 1, Meat = 2 }
public enum VendorRegistrationKind { New = 1, Renew = 2 }
public enum VendorRegistrationStatus { Active = 1, Closed = 2, Deleted = 3 }
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
    // Optional office-register facts, separate from the canonical vendor type and financial collections.
    public string? LastName { get; private set; }
    public string? FirstName { get; private set; }
    public string? MiddleName { get; private set; }
    public string? NameExtension { get; private set; }
    public string? Sex { get; private set; }
    public string? NatureOfBusiness { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    public VendorRegistrationStatus Status { get; private set; } = VendorRegistrationStatus.Active;
    public Guid? PriorRegistrationId { get; private set; }
    public Guid? CloseClientOperationId { get; private set; }
    public DateOnly? ClosedOn { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ClosedBy { get; private set; }
    public string? CloseNote { get; private set; }
    public Guid? DeleteClientOperationId { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public string? DeletedBy { get; private set; }
    public string? DeleteReason { get; private set; }
    private FishMeatVendorRegistration() { }

    public void DeleteClosed(Guid operation, string actor, string reason, DateTime now)
    {
        if (Status != VendorRegistrationStatus.Closed) throw new InvalidOperationException("CloseRegistrationBeforeDelete");
        if (operation == Guid.Empty || string.IsNullOrWhiteSpace(actor) || actor.Trim().Length > 100
            || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500 || now.Kind != DateTimeKind.Utc)
            throw new ArgumentException("InvalidDeletionEvidence");
        Status = VendorRegistrationStatus.Deleted;
        DeleteClientOperationId = operation;
        DeletedBy = actor.Trim(); DeleteReason = reason.Trim();
        DeletedAtUtc = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }

    /// <summary>Correct office facts on the existing registration, without changing identity, registration
    /// category, tax year, status, NPM links, collections, or receipt history.</summary>
    public void CorrectOfficeDetails(string? business, string? address, string? lastName,
        string? firstName, string? middleName, string? extension, string? sex, string? nature)
    {
        static string? Normalized(string? value, int max)
        {
            var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (text?.Length > max) throw new ArgumentException("InvalidOfficeDetails");
            return text;
        }
        BusinessName = Normalized(business, 200);
        Address = Normalized(address, 300);
        LastName = Normalized(lastName, 100);
        FirstName = Normalized(firstName, 100);
        MiddleName = Normalized(middleName, 100);
        NameExtension = Normalized(extension, 30);
        Sex = Normalized(sex, 30);
        NatureOfBusiness = Normalized(nature, 300);
    }
    public void Close(Guid operation, DateOnly businessDate, string actor, string? note)
    {
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        actor = actor.Trim();
        if (operation == Guid.Empty || actor.Length is < 1 or > 100 || note?.Length > 300)
            throw new ArgumentException("InvalidCloseIntent");
        if (Status != VendorRegistrationStatus.Active) throw new InvalidOperationException("RegistrationClosed");
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
        next.LastName = prior.LastName; next.FirstName = prior.FirstName;
        next.MiddleName = prior.MiddleName; next.NameExtension = prior.NameExtension;
        next.Sex = prior.Sex; next.NatureOfBusiness = prior.NatureOfBusiness;
        return next;
    }
    public static FishMeatVendorRegistration Register(Guid tenant, Guid operation, int year, FishMeatVendorType type,
        VendorRegistrationKind kind, string name, string? business, string? address, string? reference, string actor,
        string? lastName = null, string? firstName = null, string? middleName = null,
        string? nameExtension = null, string? sex = null, string? natureOfBusiness = null)
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
            LastName = Text(lastName, 100), FirstName = Text(firstName, 100), MiddleName = Text(middleName, 100),
            NameExtension = Text(nameExtension, 30), Sex = Text(sex, 30), NatureOfBusiness = Text(natureOfBusiness, 300),
            CreatedBy = Text(actor, 100) ?? throw new ArgumentException("InvalidActor"), RecordedAtUtc = DateTime.UtcNow };
    }
}
