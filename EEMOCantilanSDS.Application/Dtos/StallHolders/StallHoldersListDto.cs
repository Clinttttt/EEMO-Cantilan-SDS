namespace EEMOCantilanSDS.Application.Dtos.StallHolders;

using EEMOCantilanSDS.Domain.Enums;

public class StallHoldersListDto
{
    public int TotalStalls { get; set; }
    public int VegetableCount { get; set; }
    public int FishCount { get; set; }
    public int MeatCount { get; set; }
    public List<StallHoldersSectionDto> Sections { get; set; } = new();
    public int GrandTotalActiveStalls { get; set; }
    public decimal GrandTotalMonthlyRate { get; set; }
    public decimal GrandTotalWholeYearRental { get; set; }
}

public class StallHoldersSectionDto
{
    public string SectionName { get; set; } = string.Empty;
    public int StallCount { get; set; }
    public List<StallHolderRowDto> Rows { get; set; } = new();
    public decimal SectionMonthlyTotal { get; set; }
    public decimal SectionActualMonthly { get; set; }
    public decimal SectionWholeYearTotal { get; set; }
    public decimal SectionFishFeeTotal { get; set; }
}

public class StallHolderRowDto
{
    public int RowNumber { get; set; }
    public string ActualOccupant { get; set; } = string.Empty;
    public string NameOnContract { get; set; } = string.Empty;
    public string StallNo { get; set; } = string.Empty;
    public DateOnly EffectivityDate { get; set; }
    public int DurationYears { get; set; }
    public double? AreaSqm { get; set; }
    public decimal MonthlyRentalRate { get; set; }
    public decimal ActualMonthlyRental { get; set; }
    public decimal WholeYearRental { get; set; }
    public decimal? FishFeeTotal { get; set; }
    public bool IsClosed { get; set; }
    public string? AreaLocation { get; set; }   // NCC: "Corner" / "Extension" / "Standard"

    /// <summary>
    /// How the space is held. The office's sheets print a row without a signed contract as "No contract (space only)"
    /// — or "No contract (Extension …)" — and leave the contract-derived columns blank, because a barbecue stand or
    /// an ice-plant space has no leasee name, effectivity, term, area or contract rate to state. Only the rent
    /// actually charged appears.
    /// </summary>
    public OccupancyArrangement Arrangement { get; set; } = OccupancyArrangement.SignedContract;

    /// <summary>True when a signed contract stands behind the row, so its contract columns have something to say.</summary>
    public bool HasSignedContract => Arrangement == OccupancyArrangement.SignedContract;
}
