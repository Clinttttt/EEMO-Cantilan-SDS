using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

public sealed record SetAnnualTargetRequest(Guid ClientOperationId, string RowKey, int Year, decimal Amount,
    string ApprovedSource, string? Reference = null, string? Note = null, Guid? ExpectedRevisionId = null);
public sealed record SetMonthlyIncomeAdjustmentRequest(Guid ClientOperationId, string RowKey, int Year, int Month,
    decimal ExpectedSystemAmount, decimal OfficialAmount, string Reason, string? Reference = null,
    Guid? ExpectedRevisionId = null);
public sealed record ReportRevisionDto(Guid Id, OfficialReportRevisionKind Kind, string RowKey, int Year, int Month,
    int Revision, Guid? SupersedesId, decimal Amount, decimal? SystemAmountAtRevision, string SourceOrReason,
    string? Reference, string? Note, Guid ActorId, string ActorName, DateTime RecordedAtUtc);
public enum TargetCoverageState { None = 0, Partial = 1, Complete = 2 }
public sealed record TargetCoverageDto(TargetCoverageState State, int TargetedRows, int TotalRows,
    decimal AnnualTarget, decimal CoveredActual, decimal? Attainment);
