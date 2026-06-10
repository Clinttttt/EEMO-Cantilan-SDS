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
    public Guid? TransporterId { get; init; }
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

// ── Collection history (server-aggregated, mirrors FacilityHistory / SlaughterHistory) ──
public record TrmHistoryDto(
    int Year,
    IReadOnlyList<TrmPeriodSummaryDto> Monthly,   // each month of Year (up to current month for the current year)
    IReadOnlyList<TrmPeriodSummaryDto> Yearly     // rolling last 5 years
);

public record TrmPeriodSummaryDto(
    string Label,            // "January" for monthly rows, "2024" for yearly rows
    int Year,
    int? Month,              // null for yearly rows
    int Trips,
    int Transporters,        // distinct transporters served
    decimal Collected,
    IReadOnlyList<TrmOrgTallyDto> Organizations,  // trip/fee tally per organization
    IReadOnlyList<TrmRouteTallyDto> Routes        // trip/fee tally per route
);

public record TrmOrgTallyDto(
    string Organization,
    int Trips,
    decimal Collected
);

public record TrmRouteTallyDto(
    string Route,
    int Trips,
    decimal Collected
);
