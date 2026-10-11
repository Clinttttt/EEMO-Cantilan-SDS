using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EEMOCantilanSDS.Api.Controllers;

[ApiController, Route("api/fine-assessments"), Authorize(Roles = "SuperAdmin,Admin")]
public sealed class FineAssessmentsController(ISender sender, FineAssessmentWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet] public async Task<ActionResult<IReadOnlyList<FineAssessmentDto>>> Read(FineAccountKind? kind, Guid? accountId, string? search, CancellationToken ct)
        => HandleResponse(await workflow.ReadAsync(kind, accountId, search, ct));
    [HttpGet("accounts")] public async Task<ActionResult<IReadOnlyList<FineAccountOption>>> Accounts(string? search, CancellationToken ct)
        => HandleResponse(await workflow.SearchAccountsAsync(search, ct));
    [HttpGet("accounts/{kind}/{id:guid}")] public async Task<ActionResult<FineAccountOption>> Account(FineAccountKind kind, Guid id, CancellationToken ct)
        => HandleResponse(await workflow.AccountAsync(new(kind, id), ct));
    [HttpGet("facility-account")] public async Task<ActionResult<FineAccountOption>> FacilityAccount(Guid stallId,
        EEMOCantilanSDS.Domain.Enums.FacilityCode facility, DateOnly snapshotDate, Guid? contractId, CancellationToken ct)
        => HandleResponse(await workflow.ResolveFacilityAccountAsync(stallId, facility, snapshotDate, contractId, ct));
    [HttpGet("policies")] public async Task<ActionResult<IReadOnlyList<FinePolicyDto>>> Policies(CancellationToken ct)
        => HandleResponse(await workflow.PoliciesAsync(ct));
    [HttpGet("sources")] public async Task<ActionResult<IReadOnlyList<FineSourceOption>>> Sources(CancellationToken ct)
        => HandleResponse(await workflow.SourcesAsync(ct));
    [HttpGet("facility-accounts")] public async Task<ActionResult<IReadOnlyList<FineAccountOption>>> FacilityAccounts(Guid stallId,
        EEMOCantilanSDS.Domain.Enums.FacilityCode facility, CancellationToken ct)
        => HandleResponse(await workflow.FacilityAccountsAsync(stallId,facility,ct));
    [HttpGet("accounts/{kind}/{id:guid}/periods")] public async Task<ActionResult<IReadOnlyList<FinePeriodOption>>> Periods(FineAccountKind kind, Guid id, int? year, CancellationToken ct)
        => HandleResponse(await workflow.PeriodsAsync(new(kind,id),year,ct));
    [HttpPost("policies"), Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<FinePolicyDto>> Policy(FinePolicyRequest request, CancellationToken ct)
        => HandleResponse(await workflow.RecordPolicyAsync(request, ct));
    [HttpPost, Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<FineAssessmentDto>> Create(CreateFineAssessmentRequest request, CancellationToken ct)
        => HandleResponse(await workflow.CreateAsync(request, ct));
    [HttpGet("{id:guid}/history")] public async Task<ActionResult<IReadOnlyList<FineAssessmentEventDto>>> History(Guid id, CancellationToken ct)
        => HandleResponse(await workflow.HistoryAsync(id, ct));
}
