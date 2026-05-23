namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record SectionBreakdownDto(
    string SectionName,
    decimal Revenue,
    decimal Percentage,
    int TotalStalls,
    int ActiveStalls,
    int ClosedStalls,
    int NoContractStalls
);
