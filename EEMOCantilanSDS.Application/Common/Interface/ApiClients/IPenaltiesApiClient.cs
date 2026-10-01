using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Office view of approved penalty definitions and the register of posted fines (IA-049).</summary>
public interface IPenaltiesApiClient
{
    Task<Result<IReadOnlyList<PenaltyDefinitionDto>>> GetDefinitionsAsync();
    Task<Result<PenaltyDefinitionDto>> DefineAsync(DefinePenaltyRequest request);
    Task<Result<IReadOnlyList<PenaltyRegisterRowDto>>> GetRegisterAsync(DateOnly from, DateOnly to);
}
