using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;

namespace EEMOCantilanSDS.Application.Dtos.Revenue
{
    /// <summary>
    /// Test-only stand-in for the retired "select an Official Receipt for this draft" request. IA-062: a collection no longer
    /// needs a physical serial, so these tests keep their existing flow and the shim below simply returns the current draft.
    /// </summary>
    public sealed record SelectEcfDraftDocumentRequest(long ExpectedRevision, Guid? AccountableDocumentId);
}

namespace EEMOCantilanSDS.IntegrationTests
{
    internal static class TestDocumentShims
    {
        /// <summary>Selecting a physical document is retired (IA-062); the draft is unchanged and still reviewable.</summary>
        public static Task<Result<EcfCollectionDraftDto>> SelectDocumentAsync(
            this CollectionComposerWorkflow composer, Guid draftId, SelectEcfDraftDocumentRequest request,
            CancellationToken ct = default) =>
            composer.GetDraftAsync(draftId, ct);
    }
}
