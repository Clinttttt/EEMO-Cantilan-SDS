using EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ISlaughterApiClient
{
    Task<Result<SlaughterOverviewDto>> GetOverviewAsync(int year, int month);
    Task<Result<IReadOnlyList<SlaughterTransactionDto>>> GetTransactionsAsync(int year, int month);
    Task<Result<IReadOnlyList<OwnerTransactionGroupDto>>> GetGroupedTransactionsAsync(int year, int month);
    Task<Result<OwnerTransactionHistoryDto>> GetOwnerHistoryAsync(string ownerName, int year, int month);
    Task<Result<bool>> RecordTransactionAsync(RecordSlaughterCommand command);
    Task<Result<bool>> UpdateTransactionAsync(UpdateSlaughterCommand command);
    Task<Result<ClientProfileDto>> GetClientProfileAsync(string ownerName);
}
