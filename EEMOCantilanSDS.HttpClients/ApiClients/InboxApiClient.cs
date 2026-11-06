using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Notifications;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class InboxApiClient(HttpClient http) : HandleResponse(http), IInboxApiClient
{
    public Task<Result<InboxPageDto>> GetAsync() => GetAsync<InboxPageDto>("api/inbox");

    public Task<Result<InboxPageDto>> MarkReadAsync(Guid? id) =>
        PostAsync<object, InboxPageDto>(id is { } one ? $"api/inbox/{one}/read" : "api/inbox/read", new { });
}
