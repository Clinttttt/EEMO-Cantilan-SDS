using EEMOCantilanSDS.Domain.Common;
using MediatR;
using System;

namespace EEMOCantilanSDS.Application.Command.Stalls.UpdateStallDetails;

public record UpdateStallDetailsCommand(
    Guid StallId,
    string ActualOccupant,
    string? NameOnContract,
    double? AreaSqm,
    string? AreaNote
) : IRequest<Result<bool>>;
