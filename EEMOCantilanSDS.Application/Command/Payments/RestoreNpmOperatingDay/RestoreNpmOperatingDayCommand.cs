using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.RestoreNpmOperatingDay;

/// <summary>Removes the Head's explicit closure or opening of one day, so the weekly schedule (or an ordinary open day) decides again.</summary>
public record RestoreNpmOperatingDayCommand(DateOnly Date, string? Remarks = null) : IRequest<Result<bool>>;
