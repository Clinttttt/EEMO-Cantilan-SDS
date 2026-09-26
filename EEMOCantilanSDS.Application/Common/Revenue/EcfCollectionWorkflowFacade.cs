using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Compatibility façade for existing ECF API/tests; all draft and posting work uses the shared Composer.</summary>
public sealed class EcfCollectionWorkflow
{
    private readonly CollectionComposerWorkflow _composer;

    public EcfCollectionWorkflow(
        IAppDbContext db, ICurrentUserService currentUser, ICurrentMunicipalityAccessor municipality) =>
        _composer = new CollectionComposerWorkflow(db, currentUser, municipality);

    public Task<Result<IReadOnlyList<EcfObligationQuoteDto>>> GetObligationsAsync(int year, int month, CancellationToken ct = default) =>
        _composer.GetObligationsAsync(year, month, ct);
    public Task<Result<EcfObligationQuoteDto>> GetObligationAsync(Guid utilityBillId, CancellationToken ct = default) =>
        _composer.GetObligationAsync(utilityBillId, ct);
    public Task<Result<IReadOnlyList<EcfAvailableDocumentDto>>> GetAvailableReceiptsAsync(CancellationToken ct = default) =>
        _composer.GetAvailableReceiptsAsync(ct);
    public Task<Result<EcfCollectionDraftDto>> GetCurrentDraftAsync(CancellationToken ct = default) =>
        _composer.GetCurrentDraftAsync(ct);
    public Task<Result<EcfCollectionDraftDto>> GetDraftAsync(Guid draftId, CancellationToken ct = default) =>
        _composer.GetDraftAsync(draftId, ct);
    public Task<Result<EcfCollectionDraftDto>> CreateDraftAsync(CreateEcfCollectionDraftRequest request, CancellationToken ct = default) =>
        _composer.CreateDraftAsync(request, ct);
    public Task<Result<EcfCollectionDraftDto>> UpdateAllocationAsync(Guid draftId, UpdateEcfDraftAllocationRequest request, CancellationToken ct = default) =>
        _composer.UpdateAllocationAsync(draftId, request, ct);
    public Task<Result<EcfCollectionDraftDto>> SelectDocumentAsync(Guid draftId, SelectEcfDraftDocumentRequest request, CancellationToken ct = default) =>
        _composer.SelectDocumentAsync(draftId, request, ct);
    public Task<Result<EcfCollectionDraftDto>> ReviewAsync(Guid draftId, EcfDraftRevisionRequest request, CancellationToken ct = default) =>
        _composer.ReviewAsync(draftId, request, ct);
    public Task<Result<EcfCollectionDraftDto>> DiscardAsync(Guid draftId, EcfDraftRevisionRequest request, CancellationToken ct = default) =>
        _composer.DiscardAsync(draftId, request, ct);
    public Task<Result<EcfPostOutcomeDto>> PostAsync(Guid draftId, PostEcfCollectionDraftRequest request, CancellationToken ct = default) =>
        _composer.PostAsync(draftId, request, ct);
    public Task<Result<IReadOnlyList<EcfCollectionActivityDto>>> GetActivityAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        _composer.GetActivityAsync(from, to, ct);
}
