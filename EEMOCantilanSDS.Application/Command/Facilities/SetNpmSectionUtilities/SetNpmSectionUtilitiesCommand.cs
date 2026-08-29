using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.SetNpmSectionUtilities;

/// <summary>
/// Records whether stalls in one of the office's OWN market sections are usually metered (Head-only).
///
/// <para>
/// A DEFAULT for a stall being recorded there, and nothing more. The meters belong to the space, not to the section it
/// trades in: this changes no stall that already exists, bills nothing, and never removes electricity or water from a
/// stall that carries it. It saves a clerk ticking the same two boxes for every space in a wired row.
/// </para>
/// </summary>
public record SetNpmSectionUtilitiesCommand(string Section, bool Electricity, bool Water) : IRequest<Result<bool>>;
