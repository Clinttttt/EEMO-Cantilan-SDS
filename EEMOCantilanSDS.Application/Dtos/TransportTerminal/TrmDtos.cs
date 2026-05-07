namespace EEMOCantilanSDS.Application.Dtos.TransportTerminal;

public record TrmOverviewDto
{
    public decimal CollectedToday { get; init; }
    public int TripsToday { get; init; }
    public int TotalTransporters { get; init; }
    public int PendingORCount { get; init; }
}

public record TrmTransporterListDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Organization { get; init; } = string.Empty;
    public string DefaultRoute { get; init; } = string.Empty;
    public string PlateNumber { get; init; } = string.Empty;
    public int TripsToday { get; init; }
}

public record TrmTransporterProfileDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Organization { get; init; } = string.Empty;
    public string DefaultRoute { get; init; } = string.Empty;
    public string PlateNumber { get; init; } = string.Empty;
    public int TripsToday { get; init; }
    public int TotalTrips { get; init; }
    public decimal TotalFees { get; init; }
    public List<TrmTripDto> TripHistory { get; init; } = new();
}

public record TrmTripDto
{
    public Guid Id { get; init; }
    public Guid TransporterId { get; init; }
    public int TripNumber { get; init; }
    public string DriverName { get; init; } = string.Empty;
    public string Organization { get; init; } = string.Empty;
    public string PlateNumber { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public decimal Fee { get; init; }
    public string? ORNumber { get; init; }
    public DateTime RecordedAt { get; init; }
}

public record TrmTransporterDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Organization { get; init; } = string.Empty;
    public string DefaultRoute { get; init; } = string.Empty;
    public string PlateNumber { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}
