using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;
using EEMOCantilanSDS.Application.Command.Collectors.RecordCollectorRemittance;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Queries.Collectors.GetCollectorRemittances;
using EEMOCantilanSDS.Application.Queries.Collectors.GetReportOfCollections;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;

using EEMOCantilanSDS.Application.Requests.Collectors;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ICollectorsApiClient
{
    Task<Result<IReadOnlyList<CollectorListDto>>> GetAllCollectorsAsync();
    Task<Result<CollectorActivityDto>> GetCollectorByIdAsync(Guid id);
    Task<Result<CollectorDto>> CreateCollectorAsync(CreateCollectorCommand command);
    Task<Result<bool>> UpdateCollectorAsync(UpdateCollectorCommand command);
    Task<Result<bool>> ToggleCollectorStatusAsync(Guid id, bool isActive);
    Task<Result<bool>> ResetCollectorPasswordAsync(Guid id, string newPassword, string confirmPassword);
    Task<Result<string>> GetNextEmployeeIdAsync();

    /// <summary>Sends a push notification to a collector's devices. Returns the number of devices reached.</summary>
    Task<Result<int>> SendNotificationAsync(Guid collectorId, string title, string body);

    /// <summary>Gets (or rotates) the LGU's collector-app bind link + the app download link.</summary>
    Task<Result<MobileBindLinkDto>> GetCollectorAppLinkAsync(bool rotate = false);

    /// <summary>
    /// One collector's remittances over a period, with the fee money collected, what has been remitted and what they still
    /// hold. Read by the Report of Collections and by the screen that files them, so the two cannot disagree.
    /// </summary>
    Task<Result<CollectorRemittanceSummaryDto>> GetCollectorRemittancesAsync(Guid collectorId, DateOnly from, DateOnly to);

    /// <summary>The office's Report of Collections for one collector over one period.</summary>
    Task<Result<ReportOfCollectionsDto>> GetReportOfCollectionsAsync(Guid collectorId, DateOnly from, DateOnly to);

    /// <summary>Files cash received from a collector. The office side does this; a collector never files their own.</summary>
    Task<Result<RemittanceRecordedDto>> RecordCollectorRemittanceAsync(Guid collectorId, RecordCollectorRemittanceRequest request);
}
