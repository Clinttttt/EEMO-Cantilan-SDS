using EEMOCantilanSDS.Application.Command.Revenue.CreateRevenueClassification;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IRevenueClassificationsApiClient
{
    Task<Result<IReadOnlyList<RevenueClassificationDto>>> GetClassificationsAsync(DateOnly? asOf = null);

    Task<Result<IReadOnlyList<RevenueClassificationPolicyDto>>> GetPolicyHistoryAsync(Guid classificationId);

    Task<Result<RevenueClassificationDto>> CreateClassificationAsync(CreateRevenueClassificationCommand command);

    Task<Result<RevenueClassificationPolicyDto>> AppendPolicyAsync(
        Guid classificationId,
        AppendRevenueClassificationPolicyRequest request);

    Task<Result<bool>> RetireAsync(Guid classificationId);
}
