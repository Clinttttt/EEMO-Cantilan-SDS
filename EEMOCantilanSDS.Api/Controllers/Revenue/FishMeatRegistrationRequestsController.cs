using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[ApiController, Authorize, Route("api/office-sources/fish-meat/registration-requests")]
public sealed class FishMeatRegistrationRequestsController(ISender sender, OfficeCollectionWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet, Authorize(Roles = "SuperAdmin,Admin,Collector")]
    public async Task<ActionResult<FishMeatRegistrationRequestPage>> List(int page = 1, int pageSize = 25,
        FishMeatRequestStatus? status = null, CancellationToken ct = default) => HandleResponse(await workflow.VendorRequestsAsync(page, pageSize, status, ct));
    [HttpGet("{id:guid}"), Authorize(Roles = "SuperAdmin,Admin,Collector")]
    public async Task<ActionResult<FishMeatRegistrationRequestDto>> Get(Guid id, CancellationToken ct) => HandleResponse(await workflow.VendorRequestAsync(id, ct));
    [HttpPost, Authorize(Roles = "Collector")]
    public async Task<ActionResult<FishMeatRegistrationRequestDto>> Submit(SubmitFishMeatRegistrationRequest request, CancellationToken ct) => HandleResponse(await workflow.SubmitVendorRequestAsync(request, ct));
    [HttpPost("{id:guid}/cancel"), Authorize(Roles = "Collector")]
    public async Task<ActionResult<FishMeatRegistrationRequestDto>> Cancel(Guid id, CancelFishMeatRegistrationRequest request, CancellationToken ct) => HandleResponse(await workflow.CancelVendorRequestAsync(id, request, ct));
    [HttpPost("{id:guid}/review"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<FishMeatRegistrationRequestDto>> Review(Guid id, ReviewFishMeatRegistrationRequest request, CancellationToken ct) => HandleResponse(await workflow.ReviewVendorRequestAsync(id, request, ct));
    [HttpPost("{id:guid}/complete"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<FishMeatRegistrationRequestDto>> Complete(Guid id, CompleteFishMeatRegistrationRequest request, CancellationToken ct) => HandleResponse(await workflow.CompleteVendorRequestAsync(id, request, ct));
}
