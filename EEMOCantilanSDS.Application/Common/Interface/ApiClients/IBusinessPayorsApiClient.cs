using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IBusinessPayorsApiClient
{
    Task<Result<IReadOnlyList<PayorOccupancyDto>>> GetOccupanciesAsync(string? search, PayorLinkFilter filter);
    Task<Result<IReadOnlyList<PayorCandidateDto>>> SearchPayorsAsync(string search);
    Task<Result<PayorLinkOutcomeDto>> LinkAsync(LinkPayorRequest request);
    Task<Result<PayorLinkOutcomeDto>> CreateAndLinkAsync(CreatePayorAndLinkRequest request);
}
