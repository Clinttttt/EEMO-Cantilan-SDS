using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Specialized obligation accounts (Fish/Meat Vendor Fee, Kanmanggay space rental, Fiesta/Araw lot rental). The Head
/// opens accounts and approves amounts; Head and Admin read the registers. Money is collected only through the
/// Composer, never here.
/// </summary>
[ApiController]
[Route("api/obligations")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class ObligationsController(ISender sender, ObligationWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("workspace")]
    public async Task<ActionResult<ObligationWorkspaceDto>> WorkspaceAsync(ObligationKind kind, CancellationToken ct) =>
        HandleResponse(await workflow.GetWorkspaceAsync(kind, ct));

    [HttpGet("status-report")]
    public async Task<ActionResult<ObligationWorkspaceDto>> StatusReportAsync(ObligationKind kind, int year, CancellationToken ct) =>
        HandleResponse(await workflow.GetStatusReportAsync(kind, year, ct));

    [HttpPost("import")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<ImportSpaceHoldersResult>> ImportAsync(ImportSpaceHoldersRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.ImportSpaceHoldersAsync(request, ct));

    [HttpPost("import/preview")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<SpaceHolderImportPreview>> PreviewImportAsync(ImportSpaceHoldersRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.PreviewSpaceHoldersAsync(request, ct));
    [HttpGet("accounts")]
    public async Task<ActionResult<IReadOnlyList<ObligationAccountDto>>> AccountsAsync(
        [FromQuery] ObligationKind kind, CancellationToken ct) =>
        HandleResponse(await workflow.GetAccountsAsync(kind, ct));

    [HttpGet("accounts/{accountId:guid}/register")]
    public async Task<ActionResult<IReadOnlyList<ObligationQuoteDto>>> RegisterAsync(Guid accountId, CancellationToken ct) =>
        HandleResponse(await workflow.GetRegisterAsync(accountId, ct));

    [HttpGet("vendor-stalls")]
    public async Task<ActionResult<IReadOnlyList<VendorFeeStallDto>>> VendorStallsAsync(CancellationToken ct) =>
        HandleResponse(await workflow.GetVendorStallsAsync(ct));

    [HttpPost("accounts")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<ObligationAccountDto>> CreateAsync(
        [FromBody] CreateObligationAccountRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.CreateAccountAsync(request, ct));

    [HttpPost("accounts/{accountId:guid}/rates")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<bool>> SetRateAsync(
        Guid accountId, [FromBody] SetObligationRateRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.SetRateAsync(accountId, request, ct));

    [HttpPost("accounts/{accountId:guid}/close")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<bool>> CloseAsync(
        Guid accountId, [FromBody] CloseObligationAccountRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.CloseAccountAsync(accountId, request, ct));
}
