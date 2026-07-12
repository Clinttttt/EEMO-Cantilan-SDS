using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrForDays;

/// <summary>
/// Applies ONE Official Receipt (OR) number to several PAID NPM daily collections of the same stall in a
/// single transaction — used when one physical receipt covers multiple days of the same payor. The OR may
/// recur across days of this stall but is rejected if it already belongs to a different stall/module.
/// </summary>
public record SaveDailyCollectionOrForDaysCommand(
    Guid StallId,
    IReadOnlyList<DateOnly> Dates,
    string ORNumber) : IRequest<Result<bool>>;
