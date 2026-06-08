namespace EEMOCantilanSDS.Application.Dtos.StallHolders;

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
}
