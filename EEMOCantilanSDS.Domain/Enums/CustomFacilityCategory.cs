namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>
/// A custom monthly facility's declared operational category, independent of its monthly billing engine.
/// The default preserves every existing Custom1..Custom5 as Stall Rent.
/// This value is set at creation and cannot reclassify recorded historical collections.
/// </summary>
public enum CustomFacilityCategory
{
    StallRent = 0,
    SpaceRental = 1
}
