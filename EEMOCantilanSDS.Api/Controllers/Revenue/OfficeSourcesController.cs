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
    [HttpGet("npm-requests/stall-suggestion"), Authorize(Roles = "Collector")]
    public async Task<ActionResult<NpmStallNumberSuggestionDto>> SuggestNpmStall([FromQuery] EEMOCantilanSDS.Domain.Enums.MarketSection section, CancellationToken ct) =>
        HandleResponse(await workflow.SuggestNpmStallAsync(section, ct));

    [HttpGet("npm-requests/pending/{vendorRegistrationId:guid}"), Authorize(Roles = "Collector")]
    public async Task<ActionResult<NpmRegistrationRequestDto>> PendingNpmRequest(Guid vendorRegistrationId, CancellationToken ct) =>
        HandleResponse(await workflow.PendingNpmRequestAsync(vendorRegistrationId, ct));

    [HttpGet("npm-requests"), Authorize(Roles = "SuperAdmin,Admin,Collector")]
    public async Task<ActionResult<NpmRegistrationRequestPage>> NpmRequests(int page = 1, int pageSize = 25, CancellationToken ct = default) => HandleResponse(await workflow.NpmRequestsAsync(page, pageSize, ct));
    [HttpPost("npm-requests"), Authorize(Roles = "Collector")]
    public async Task<ActionResult<NpmRegistrationRequestDto>> SubmitNpmRequest(SubmitNpmRegistrationRequest request, CancellationToken ct) => HandleResponse(await workflow.SubmitNpmRequestAsync(request, ct));
    [HttpPost("npm-requests/{id:guid}/cancel"), Authorize(Roles = "Collector")]
    public async Task<ActionResult<NpmRegistrationRequestDto>> CancelNpmRequest(Guid id, CancelNpmRegistrationRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.CancelNpmRequestAsync(id, request, ct));
    [HttpPost("npm-requests/{id:guid}/review"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<NpmRegistrationRequestDto>> ReviewNpmRequest(Guid id, ReviewNpmRegistrationRequest request, CancellationToken ct) => HandleResponse(await workflow.ReviewNpmRequestAsync(id, request, ct));
    [HttpPost("fish-meat/npm-occupancies"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<NpmVendorOccupancyResult>> CreateNpmOccupancy(CreateNpmVendorOccupancyRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.CreateNpmVendorOccupancyAsync(request, sender, ct));
    [HttpGet("fish-meat/npm-candidates"), Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<NpmVendorCandidates>> NpmCandidates(int taxYear, bool historical, bool exceptions, CancellationToken ct) =>
        HandleResponse(await workflow.NpmVendorCandidatesAsync(taxYear, historical, exceptions, ct));
    [HttpGet("fish-meat/registrations/{id:guid}/npm-links"), Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<IReadOnlyList<NpmVendorAssociationDto>>> NpmLinks(Guid id, CancellationToken ct) => HandleResponse(await workflow.NpmVendorLinksAsync(id, ct));
    [HttpPost("fish-meat/registrations/{id:guid}/npm-links"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<NpmVendorAssociationDto>> LinkNpm(Guid id, ConfirmNpmVendorAssociationRequest request, CancellationToken ct) => HandleResponse(await workflow.LinkNpmVendorAsync(id, request, ct));
    [HttpPost("fish-meat/npm-links/{id:guid}/correct"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<NpmVendorAssociationDto>> CorrectNpmLink(Guid id, CorrectNpmVendorAssociationRequest request, CancellationToken ct) => HandleResponse(await workflow.CorrectNpmVendorLinkAsync(id, request, ct));
    /// <summary>Read-only evidence of whether a collection the office is asked to enter is already on the server. Records nothing, and never authorises a back-entry on its own.</summary>
    [HttpPost("back-entry/lookup"), Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<BackEntryLookupResult>> BackEntryLookup(BackEntryLookupRequest request, [FromServices] IBackEntryReconciliationReader reader, CancellationToken ct) =>
        HandleResponse(await reader.LookupAsync(request, ct));
    [HttpGet("activity"), Authorize(Roles = "SuperAdmin,Admin,Collector")]
    public async Task<ActionResult<IReadOnlyList<SourceNativeActivityDto>>> Activity(DateOnly from, DateOnly to, string? operationCode, CancellationToken ct) =>
        HandleResponse(await workflow.ActivityAsync(from, to, operationCode, ct));
    [HttpGet("fish-meat/registrations"), Authorize(Roles = "SuperAdmin,Admin,Collector")]
    public async Task<ActionResult<IReadOnlyList<FishMeatVendorRegistrationDto>>> Registrations(int year, CancellationToken ct) => HandleResponse(await workflow.RegistrationsAsync(year, ct));
    [HttpPost("fish-meat/registrations"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<FishMeatVendorRegistrationDto>> Register(RegisterFishMeatVendorRequest request, CancellationToken ct) => HandleResponse(await workflow.RegisterAsync(request, ct));
    [HttpGet("fish-meat/manage"), Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<VendorRegistryManagement>> Manage(int taxYear, int month, EEMOCantilanSDS.Domain.Entities.Revenue.VendorRegistrationStatus? status,
        int? year, CancellationToken ct) => HandleResponse(await workflow.ManageRegistrationsAsync(taxYear, month, status, year, ct));
    [HttpPut("fish-meat/registrations/{id:guid}/office-details"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<VendorRegistrationMutationResult>> CorrectOfficeDetails(Guid id, CorrectVendorOfficeDetailsRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.CorrectOfficeDetailsAsync(id, request, ct));
    [HttpPost("fish-meat/registrations/{id:guid}/close"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<VendorRegistrationMutationResult>> Close(Guid id, CloseVendorRegistrationRequest request, CancellationToken ct) => HandleResponse(await workflow.CloseRegistrationAsync(id, request, ct));
    [HttpPost("fish-meat/registrations/{id:guid}/renew"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<VendorRegistrationMutationResult>> Renew(Guid id, RenewVendorRegistrationRequest request, CancellationToken ct) => HandleResponse(await workflow.RenewRegistrationAsync(id, request, ct));
    [HttpPost("fish-meat/registrations/{id:guid}/delete"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<VendorRegistrationMutationResult>> Delete(Guid id, DeleteVendorRegistrationRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.DeleteRegistrationAsync(id, request, ct));
    [HttpPost("fish-meat/import/preview"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<VendorRegistryImportPreview>> PreviewImport(VendorRegistryImportRequest request, CancellationToken ct) => HandleResponse(await workflow.PreviewImportAsync(request, ct));
    [HttpPost("fish-meat/import/save"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<VendorRegistryImportResult>> SaveImport(VendorRegistryImportRequest request, CancellationToken ct) => HandleResponse(await workflow.SaveImportAsync(request, ct));
    [HttpGet("fish-meat/summary"), Authorize(Roles = "SuperAdmin,Admin,Collector")]
    public async Task<ActionResult<VendorRegistrySummary>> Summary(int year, CancellationToken ct) => HandleResponse(await workflow.RegistrySummaryAsync(year, ct));
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
