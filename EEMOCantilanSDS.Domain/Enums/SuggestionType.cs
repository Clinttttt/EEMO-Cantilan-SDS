namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>
/// Categories of mobile pick-list suggestions that an operator can hide (blocklist).
/// Pick-lists are derived from existing records; hiding suppresses a value from being suggested.
/// </summary>
public enum SuggestionType
{
    TrmDriver = 1,
    TrmRoute = 2,
    TrmOrganization = 3,
    TpmGoods = 4,
    SlhOwner = 5,
    TpmVendor = 6
}
