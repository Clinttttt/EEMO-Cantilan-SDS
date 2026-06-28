using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrNumber;

/// <summary>
/// Adds an OR (receipt) number to an existing PAID NPM daily collection — used when a collector
/// recorded the collection in the field without an OR and an admin enters it afterward.
/// </summary>
public record SaveDailyCollectionOrNumberCommand(
    Guid StallId,
    DateOnly CollectionDate,
    string ORNumber) : IRequest<Result<bool>>;
