using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.AddNpmCustomSection;

/// <summary>
/// Adds a custom NPM section name to the current tenant's registry (Head-only). Idempotent.
/// </summary>
/// <param name="DailyRate">
/// The daily fee for stalls in this section, if the office states one now. Optional: a section left unpriced has its
/// stalls billed the market's own rate, which is what happened before a section could be priced at all. Written
/// effective TODAY and never backwards, so no elapsed day is re-priced. A stall let at its own rate keeps that rate.
/// </param>
public record AddNpmCustomSectionCommand(string Name, decimal? DailyRate = null) : IRequest<Result<bool>>;
