using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.SaveSlaughterOrNumber;

/// <summary>
/// Stamps an official receipt (OR) number on a slaughter visit identified by (owner, date). A visit is
/// recorded as one row per animal type but is a single receipt, so the OR is applied to every row.
/// </summary>
public record SaveSlaughterOrNumberCommand(
    string OwnerName,
    DateOnly TransactionDate,
    string ORNumber) : IRequest<Result<bool>>;
