using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;
using EEMOCantilanSDS.Application.Command.TaboanMarket.AddVendor;
using EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;
using EEMOCantilanSDS.Application.Command.Suggestions.HideSuggestion;
using EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorMobileMenu;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmCollection;
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

    [HttpGet("npm/collections")]
    public async Task<ActionResult<MobileNpmCollectionDto>> GetNpmCollectionsAsync([FromQuery] int year, [FromQuery] int month)
    {
        var result = await Sender.Send(new GetMobileNpmCollectionQuery(year, month));
        return HandleResponse(result);
    }

    [HttpPost("npm/collections/record")]
    public async Task<ActionResult<bool>> RecordNpmCollectionAsync([FromBody] RecordMobileNpmCollectionRequest request)
    {
        var command = new RecordDailyCollectionCommand(
            request.StallId,
            PhilippineTime.Today,
            request.IsPaid,
            request.FishKilos,
            request.ORNumber);

        var result = await Sender.Send(command);
        return HandleResponse(result);
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
}
