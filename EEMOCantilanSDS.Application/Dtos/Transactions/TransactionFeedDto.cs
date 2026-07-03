using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Transactions;

/// <summary>
/// A single recorded money-movement across any facility, normalized for the cross-facility
/// Transactions transparency feed. <see cref="OccurredAt"/> is the transaction's actual business
/// moment in Philippine local time (Kind=Unspecified). <see cref="HasTime"/> is false for sources
/// that only carry a calendar date (daily fee, slaughter, market day) so the client shows date only.
/// </summary>
public record TransactionFeedDto(
    Guid Id,
    FacilityCode FacilityCode,
    string FacilityName,
    DateTime OccurredAt,
    bool HasTime,
    string Party,          // payer / owner / driver / vendor
    string Reference,      // stall no / plate / animal / goods
    string Kind,           // "Monthly Rent", "Daily Fee", "Slaughter", "Terminal Trip", "Market Day"
    decimal Amount,
    string? ORNumber,
    string Status,         // "Paid" / "Partial"
    string RecordedBy      // collector's name, or the admin/head who recorded it (audit actor)
);
