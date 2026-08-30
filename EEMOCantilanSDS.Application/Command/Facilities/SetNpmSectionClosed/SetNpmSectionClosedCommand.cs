using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.SetNpmSectionClosed;

/// <summary>
/// Closes or reopens one of the office's own market sections, and the stalls in it, as one act.
/// </summary>
/// <param name="Section">The office's own name for the section.</param>
/// <param name="Closed">True to close the section and its active stalls; false to reopen the section and exactly the stalls this closure closed.</param>
public record SetNpmSectionClosedCommand(string Section, bool Closed) : IRequest<Result<int>>;
