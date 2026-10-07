using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;
[ApiController, Authorize, Route("api/office-sources")]
public sealed class OfficeSourcesController(ISender sender, OfficeCollectionWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("activity"), Authorize(Roles = "SuperAdmin,Admin,Collector")]
    public async Task<ActionResult<IReadOnlyList<SourceNativeActivityDto>>> Activity(DateOnly from, DateOnly to, string? operationCode, CancellationToken ct) =>
        HandleResponse(await workflow.ActivityAsync(from, to, operationCode, ct));
    [HttpGet("fish-meat/registrations"), Authorize(Roles = "SuperAdmin,Admin,Collector")]
    public async Task<ActionResult<IReadOnlyList<FishMeatVendorRegistrationDto>>> Registrations(int year, CancellationToken ct) => HandleResponse(await workflow.RegistrationsAsync(year, ct));
    [HttpPost("fish-meat/registrations"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<FishMeatVendorRegistrationDto>> Register(RegisterFishMeatVendorRequest request, CancellationToken ct) => HandleResponse(await workflow.RegisterAsync(request, ct));
    [HttpPut("terminal/vehicle-section"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<bool>> MapVehicle(TerminalVehicleMappingRequest request, CancellationToken ct) => HandleResponse(await workflow.MapVehicleAsync(request, ct));
    [HttpGet("terminal/vehicle-choices"), Authorize(Roles = "SuperAdmin,Admin,Collector")]
    public async Task<ActionResult<IReadOnlyList<TerminalVehicleChoice>>> VehicleChoices(DateOnly date, CancellationToken ct) =>
        Ok(await workflow.VehicleChoicesAsync(date, ct));
    [HttpPost("quote"), Authorize(Roles = "Collector")]
    public async Task<ActionResult<SourceNativeChargeQuote>> Quote(SourceNativeCollectionRequest request, CancellationToken ct) => HandleResponse(await workflow.QuoteAsync(request, ct));
    [HttpPost("record"), Authorize(Roles = "Collector")]
    public async Task<ActionResult<GovernedServiceOutcomeDto>> Record(SourceNativeCollectionRequest request, CancellationToken ct) => HandleResponse(await workflow.PostAsync(request, ct));
}
