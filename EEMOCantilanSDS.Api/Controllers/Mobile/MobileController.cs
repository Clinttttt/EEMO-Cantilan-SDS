using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;
using EEMOCantilanSDS.Application.Command.Collectors.UpdateProfile;
using EEMOCantilanSDS.Application.Command.Notifications.RegisterDeviceToken;
using EEMOCantilanSDS.Application.Command.Notifications.RemoveDeviceToken;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;
using EEMOCantilanSDS.Application.Command.TaboanMarket.AddVendor;
using EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;
using EEMOCantilanSDS.Application.Command.Suggestions.HideSuggestion;
using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorMobileMenu;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorProfile;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorReport;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorRecords;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmArrears;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmCollection;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmUtility;
using EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityPayment;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileSlaughterCollection;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileTpmCollection;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileTrmCollection;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "Collector")]
[Route("api/[controller]")]
[ApiController]
public class MobileController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("menu")]
    public async Task<ActionResult<MobileMenuDto>> GetMenuAsync()
    {
        var result = await Sender.Send(new GetCollectorMobileMenuQuery());
        return HandleResponse(result);
    }

    [HttpGet("profile")]
    public async Task<ActionResult<MobileCollectorProfileDto>> GetProfileAsync()
    {
        var result = await Sender.Send(new GetCollectorProfileQuery());
        return HandleResponse(result);
    }

    [HttpPut("profile")]
    public async Task<ActionResult<bool>> UpdateProfileAsync([FromBody] UpdateMobileProfileRequest request)
    {
        var result = await Sender.Send(new UpdateCollectorProfileCommand(
            request.FullName, request.ContactNumber, request.Email));
        return HandleResponse(result);
    }

    [HttpPost("device-token")]
    public async Task<ActionResult<bool>> RegisterDeviceTokenAsync([FromBody] RegisterDeviceTokenRequest request)
    {
        var result = await Sender.Send(new RegisterDeviceTokenCommand(request.Token, request.Platform));
        return HandleResponse(result);
    }

    [HttpPost("device-token/remove")]
    public async Task<ActionResult<bool>> RemoveDeviceTokenAsync([FromBody] RegisterDeviceTokenRequest request)
    {
        var result = await Sender.Send(new RemoveDeviceTokenCommand(request.Token));
        return HandleResponse(result);
    }

    [HttpGet("records")]
    public async Task<ActionResult<IReadOnlyList<MobileCollectorRecordDto>>> GetRecordsAsync(
        [FromQuery] FacilityCode? facility, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var result = await Sender.Send(new GetCollectorRecordsQuery(facility, from, to));
        return HandleResponse(result);
    }

    [HttpGet("reports")]
    public async Task<ActionResult<MobileCollectorReportDto>> GetReportAsync(
        [FromQuery] FacilityCode? facility, [FromQuery] int year, [FromQuery] int month)
    {
        var result = await Sender.Send(new GetCollectorReportQuery(facility, year, month));
        return HandleResponse(result);
    }

    [HttpGet("npm/collections")]
    public async Task<ActionResult<MobileNpmCollectionDto>> GetNpmCollectionsAsync([FromQuery] int year, [FromQuery] int month)
    {
        var result = await Sender.Send(new GetMobileNpmCollectionQuery(year, month));
        return HandleResponse(result);
    }

    /// <summary>
    /// What the market is behind on: months that closed owing, and the days of this month gone by.
    /// </summary>
    /// <remarks>
    /// Separate from the round above, which is fetched at every stall and must stay light. This walks each unsettled month of
    /// every payor and asks the office's own settlement to price it, so the collector pays for that only when opening the
    /// arrears screen.
    /// </remarks>
    [HttpGet("npm/arrears")]
    public async Task<ActionResult<MobileNpmArrearsDto>> GetNpmArrearsAsync([FromQuery] int year, [FromQuery] int month)
    {
        var result = await Sender.Send(new GetMobileNpmArrearsQuery(year, month));
        return HandleResponse(result);
    }

    [HttpPost("npm/collections/record")]
    public async Task<ActionResult<bool>> RecordNpmCollectionAsync([FromBody] RecordMobileNpmCollectionRequest request)
    {
        var command = new RecordDailyCollectionCommand(
            request.StallId,
            // The day the collector says the money answers for, and today when the app does not say. A day that went
            // uncollected stays owed, so a collector catching a payor the next morning has to be able to settle YESTERDAY
            // rather than record it against today and leave yesterday open. A future date is refused by the validator.
            request.CollectionDate ?? PhilippineTime.Today,
            request.IsPaid,
            request.FishKilos,
            request.ORNumber,
            IsAbsent: request.IsAbsent);

        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    /// <summary>
    /// Settles SEVERAL owed days of one market stall against one physical receipt, in a single transaction.
    ///
    /// <para>
    /// A payor settling four owed days at once is ordinary in the field, and the office's own portal has always been able
    /// to record it; a collector could only do it one day at a time. This is the same command the portal sends, so the
    /// rules are not restated here: days that are future, market-closed, already paid or excused, or that no term answers
    /// for are skipped, and one OR may cover the rest.
    /// </para>
    ///
    /// <para>
    /// Nothing about who may settle is widened. This controller is collectors only, and the command's own guard
    /// (<c>NpmSettlementAccess</c>) already restricts a collector to a facility they are assigned to — the same rule that
    /// governs recording a single day above.
    /// </para>
    /// </summary>
    [HttpPost("npm/collections/settle-days")]
    public async Task<ActionResult<bool>> SettleNpmDaysAsync([FromBody] SettleMobileNpmDaysRequest request)
    {
        var result = await Sender.Send(new SettleNpmDaysCommand(
            request.StallId,
            request.Dates ?? Array.Empty<DateOnly>(),
            request.ORNumber));

        return HandleResponse(result);
    }

    [HttpGet("npm-utility/collections")]
    public async Task<ActionResult<MobileNpmUtilityDto>> GetNpmUtilityAsync([FromQuery] int year, [FromQuery] int month)
    {
        var result = await Sender.Send(new GetMobileNpmUtilityQuery(year, month));
        return HandleResponse(result);
    }

    [HttpPost("npm-utility/pay")]
    public async Task<ActionResult<bool>> RecordNpmUtilityPaymentAsync([FromBody] RecordMobileUtilityPaymentRequest request)
    {
        var result = await Sender.Send(new RecordUtilityPaymentCommand(
            request.BillId,
            request.ElecStatus, request.ElecPartialAmount,
            request.WaterStatus, request.WaterPartialAmount,
            request.ElecORNumber, request.WaterORNumber, null, request.ClientOperationId));

        return result.IsSuccess
            ? HandleResponse(Result<bool>.Success(true))
            : HandleResponse(Result<bool>.Failure(result.Error ?? "Unable to record the utility payment.", result.StatusCode ?? 400));
    }

    [HttpGet("monthly/collections")]
    public async Task<ActionResult<MobileMonthlyCollectionDto>> GetMonthlyCollectionsAsync(
        [FromQuery] FacilityCode facility, [FromQuery] int year, [FromQuery] int month)
    {
        var result = await Sender.Send(new GetMobileMonthlyCollectionQuery(facility, year, month));
        return HandleResponse(result);
    }

    [HttpPost("monthly/collections/record")]
    public async Task<ActionResult<bool>> RecordMonthlyCollectionAsync([FromBody] RecordMobileMonthlyCollectionRequest request)
    {
        var today = PhilippineTime.Today;
        var command = new RecordPaymentCommand(
            request.StallId,
            today.Year,
            today.Month,
            request.Status,
            request.PartialAmount,
            Remarks: null,
            ORNumber: request.ORNumber);

        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpGet("slaughter/collections")]
    public async Task<ActionResult<MobileSlaughterCollectionDto>> GetSlaughterCollectionsAsync(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] int day)
    {
        var result = await Sender.Send(new GetMobileSlaughterCollectionQuery(year, month, day));
        return HandleResponse(result);
    }

    [HttpPost("slaughter/record")]
    public async Task<ActionResult<bool>> RecordSlaughterAsync([FromBody] RecordMobileSlaughterRequest request)
    {
        var command = new RecordSlaughterCommand(
            request.OwnerName,
            PhilippineTime.Today,
            request.ORNumber,
            request.AnimalType,
            request.CustomAnimalType,
            request.NumberOfHeads,
            request.CustomRate);

        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPut("slaughter/update")]
    public async Task<ActionResult<bool>> UpdateSlaughterAsync([FromBody] UpdateMobileSlaughterRequest request)
    {
        var command = new UpdateSlaughterCommand(
            request.OwnerName,
            request.TransactionDate,
            request.ORNumber,
            request.Animals.Select(a => new AnimalEntry(a.AnimalType, a.CustomAnimalType, a.NumberOfHeads, a.CustomRate)).ToList());

        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpGet("trm/collections")]
    public async Task<ActionResult<MobileTrmCollectionDto>> GetTrmCollectionsAsync()
    {
        var result = await Sender.Send(new GetMobileTrmCollectionQuery());
        return HandleResponse(result);
    }

    [HttpPost("trm/trips")]
    public async Task<ActionResult<TrmTripDto>> RecordTripAsync([FromBody] RecordMobileTripRequest request)
    {
        var command = new RecordTripCommand(
            request.TransporterId,
            request.DriverName,
            request.PlateNumber,
            request.Route,
            request.ORNumber,
            request.Remarks,
            request.Organization);

        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpGet("tpm/collections")]
    public async Task<ActionResult<MobileTpmCollectionDto>> GetTpmCollectionsAsync()
    {
        var result = await Sender.Send(new GetMobileTpmCollectionQuery());
        return HandleResponse(result);
    }

    [HttpPost("tpm/vendors")]
    public async Task<ActionResult<TpmVendorAttendanceDto>> AddTpmVendorAsync([FromBody] AddMobileTpmVendorRequest request)
    {
        var command = new AddVendorToMarketDayCommand(
            request.VendorName,
            request.Goods,
            PhilippineTime.Today,
            string.IsNullOrWhiteSpace(request.ORNumber) ? null : request.ORNumber.Trim());
        return HandleResponse(await Sender.Send(command));
    }

    [HttpPost("tpm/attendance/payment")]
    public async Task<ActionResult<bool>> MarkTpmVendorPaidAsync([FromBody] MarkMobileTpmVendorPaidRequest request)
    {
        var command = new MarkVendorPaidCommand(request.AttendanceId, request.IsPaid, request.ORNumber);
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPost("suggestions/hide")]
    public async Task<ActionResult<bool>> HideSuggestionAsync([FromBody] HideMobileSuggestionRequest request)
    {
        var result = await Sender.Send(new HideSuggestionCommand(request.Type, request.Value));
        return HandleResponse(result);
    }

    // Offline-first: replay a batch of queued collections recorded while offline. Idempotent per
    // operation (client operation id); returns a per-item Synced/Rejected/Failed outcome.
    [HttpPost("sync")]
    public async Task<ActionResult<SyncOfflineCollectionsResultDto>> SyncOfflineCollectionsAsync(
        [FromBody] SyncOfflineCollectionsCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }
}
