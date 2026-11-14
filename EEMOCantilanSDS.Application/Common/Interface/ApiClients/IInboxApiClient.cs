using EEMOCantilanSDS.Application.Dtos.Notifications;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IInboxApiClient
{
    Task<Result<InboxPageDto>> GetAsync();

    /// <summary>Marks one message read, or all of them when no id is given.</summary>
    Task<Result<InboxPageDto>> MarkReadAsync(Guid? id);
}
