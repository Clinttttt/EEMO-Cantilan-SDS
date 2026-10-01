namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>
/// The specialized operations that share one lightweight obligation source (IA-050). They share mechanics only: a
/// payor, an approved effective-dated amount, an assessed period and canonical Official Receipt allocations. They never
/// share a business identity: each kind keeps its own revenue classification.
/// </summary>
public enum ObligationKind
{
    /// <summary>Fish/Meat Vendor Fee: monthly goal on an NPM Fish/Meat stall. Not stall rent, not Weight and Measure.</summary>
    FishMeatVendorFee = 1,

    /// <summary>Kanmanggay Space Rental: monthly, per space. Not BBQ, not an NPM stall/contract.</summary>
    KanmanggaySpaceRental = 2,

    /// <summary>Fiesta / Araw lot rental: one approved amount for an event lot. Event dates are not billing dates.</summary>
    FiestaArawLotRental = 3
}

/// <summary>The event an event-lot rental belongs to.</summary>
public enum LotRentalEvent
{
    Fiesta = 1,
    Araw = 2
}
