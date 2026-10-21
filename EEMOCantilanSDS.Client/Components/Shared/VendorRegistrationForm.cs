using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Client.Components.Shared;

/// <summary>The facts a Fish / Meat registration drawer collects: one registration for one tax year. Pages own validation and saving.</summary>
public sealed class VendorRegistrationForm
{
    public string Name { get; set; } = string.Empty;
    public string Business { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string NameExtension { get; set; } = string.Empty;
    public string Sex { get; set; } = string.Empty;
    public string NatureOfBusiness { get; set; } = string.Empty;
    public FishMeatVendorType Type { get; set; } = FishMeatVendorType.Fish;
    public VendorRegistrationKind Kind { get; set; } = VendorRegistrationKind.New;

    /// <summary>The tax year a renewal is for; a new registration is always for the current year.</summary>
    public int TaxYear { get; set; }

    public static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The same text means the same intent: used to keep one operation ID across an unchanged retry and mint a fresh one after any edit.</summary>
    public string Fingerprint => string.Join("|", Name.Trim(), Business.Trim(), Address.Trim(), Reference.Trim(), LastName.Trim(), FirstName.Trim(), MiddleName.Trim(),
        NameExtension.Trim(), Sex.Trim(), NatureOfBusiness.Trim(), (int)Type, (int)Kind, TaxYear);
}
