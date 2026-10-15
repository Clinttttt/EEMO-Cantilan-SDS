namespace EEMOCantilanSDS.Client.Components.Shared;

/// <summary>Presentation only: collapse an exact accidental double paste of an address.
/// The persisted office record is never modified silently.</summary>
public static class VendorAddressDisplay
{
    public static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Not recorded";
        var address = value.Trim();
        if (address.Length % 2 == 0
            && address.AsSpan(0, address.Length / 2).Equals(address.AsSpan(address.Length / 2), StringComparison.OrdinalIgnoreCase))
            return address[..(address.Length / 2)].Trim();
        return address;
    }
}