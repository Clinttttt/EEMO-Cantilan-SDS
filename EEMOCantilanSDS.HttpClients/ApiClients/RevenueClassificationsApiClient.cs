using System.Globalization;
using EEMOCantilanSDS.Application.Command.Revenue.CreateRevenueClassification;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class RevenueClassificationsApiClient(HttpClient http)
    : HandleResponse(http), IRevenueClassificationsApiClient
{
    public async Task<Result<IReadOnlyList<RevenueClassificationDto>>> GetClassificationsAsync(DateOnly? asOf = null)
    {
        var url = "api/revenue-classifications";
        if (asOf.HasValue)
        {
            var date = asOf.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            url += $"?asOf={Uri.EscapeDataString(date)}";
        }

        return await GetAsync<IReadOnlyList<RevenueClassificationDto>>(url);
    }

    public async Task<Result<IReadOnlyList<RevenueClassificationPolicyDto>>> GetPolicyHistoryAsync(Guid classificationId) =>
        await GetAsync<IReadOnlyList<RevenueClassificationPolicyDto>>(
            $"api/revenue-classifications/{classificationId}/policies");

    public async Task<Result<RevenueClassificationDto>> CreateClassificationAsync(CreateRevenueClassificationCommand command) =>
        await PostAsync<CreateRevenueClassificationCommand, RevenueClassificationDto>(
            "api/revenue-classifications", command);

    public async Task<Result<RevenueClassificationPolicyDto>> AppendPolicyAsync(
        Guid classificationId,
        AppendRevenueClassificationPolicyRequest request) =>
        await PostAsync<AppendRevenueClassificationPolicyRequest, RevenueClassificationPolicyDto>(
            $"api/revenue-classifications/{classificationId}/policies", request);

    public async Task<Result<bool>> RetireAsync(Guid classificationId) =>
        await PostAsync<bool>($"api/revenue-classifications/{classificationId}/retire");
}
