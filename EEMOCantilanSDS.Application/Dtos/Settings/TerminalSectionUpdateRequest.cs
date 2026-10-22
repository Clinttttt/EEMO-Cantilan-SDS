using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Dtos.Settings;

public sealed record TerminalSectionUpdateRequest(TerminalSection Section, bool IsEnabled,
    string? LocalDisplayName, long ExpectedVersion);
