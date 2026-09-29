namespace EEMOCantilanSDS.Application.Dtos;

/// <summary>A supported non-facility operation and whether this collector is explicitly assigned.</summary>
public sealed record CollectorOperationAssignmentDto(string Code, string DisplayName, bool Assigned);

public sealed record ReplaceCollectorOperationAssignmentsRequest(IReadOnlyList<string>? OperationCodes);
