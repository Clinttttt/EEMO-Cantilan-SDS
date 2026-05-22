namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record TopStallDto(
    string StallNumber,
    string OccupantName,
    decimal Revenue
);
