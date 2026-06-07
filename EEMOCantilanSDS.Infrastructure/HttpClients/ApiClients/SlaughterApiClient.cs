using EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class SlaughterApiClient(HttpClient http) : HandleResponse(http), ISlaughterApiClient
{
    public async Task<Result<SlaughterOverviewDto>> GetOverviewAsync(int year, int month) =>
        await GetAsync<SlaughterOverviewDto>($"api/slaughter/overview?year={year}&month={month}");

    public async Task<Result<SlaughterHistoryDto>> GetHistoryAsync(int year) =>
        await GetAsync<SlaughterHistoryDto>($"api/slaughter/history?year={year}");

    public async Task<Result<IReadOnlyList<SlaughterTransactionDto>>> GetTransactionsAsync(int year, int month) =>
        await GetAsync<IReadOnlyList<SlaughterTransactionDto>>($"api/slaughter/transactions?year={year}&month={month}");

    public async Task<Result<IReadOnlyList<OwnerTransactionGroupDto>>> GetGroupedTransactionsAsync(int year, int month) =>
        await GetAsync<IReadOnlyList<OwnerTransactionGroupDto>>($"api/slaughter/grouped-transactions?year={year}&month={month}");

    public async Task<Result<OwnerTransactionHistoryDto>> GetOwnerHistoryAsync(string ownerName, int year, int month) =>
        await GetAsync<OwnerTransactionHistoryDto>($"api/slaughter/owner-history?ownerName={Uri.EscapeDataString(ownerName)}&year={year}&month={month}");

    public async Task<Result<bool>> RecordTransactionAsync(RecordSlaughterCommand command) =>
        await PostAsync<RecordSlaughterCommand, bool>("api/slaughter/record", command);

    public async Task<Result<bool>> UpdateTransactionAsync(UpdateSlaughterCommand command) =>
        await PutAsync<UpdateSlaughterCommand, bool>("api/slaughter/update", command);

    public async Task<Result<ClientProfileDto>> GetClientProfileAsync(string ownerName) =>
        await GetAsync<ClientProfileDto>($"api/slaughter/client/{Uri.EscapeDataString(ownerName)}");
}
