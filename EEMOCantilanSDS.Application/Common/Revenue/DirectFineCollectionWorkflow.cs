using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Only the direct-fine source/policy adapter. Cash is written by the existing canonical coordinator.</summary>
public sealed class DirectFineCollectionWorkflow(IAppDbContext db, ICurrentUserService user,
    ICurrentMunicipalityAccessor tenant, IClock clock)
{
    private const string Origin = "DirectFineCollection";
    private Guid Tenant => tenant.MunicipalityId;
    private bool Authorized => user.IsAuthenticated && user.Role == "SuperAdmin" && user.UserId is { } id
        && id != Guid.Empty && Tenant != Guid.Empty && user.MunicipalityId == Tenant;
    private static DirectFineCollectionRequest Normalize(DirectFineCollectionRequest r) => r with {
        Reason = Clean(r.Reason), SupportingReference = Clean(r.SupportingReference), PayerParticulars = Clean(r.PayerParticulars) };
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private async Task<(DirectFineAuthority Authority, PenaltyDefinition Version)?> Resolve(Guid id, DateOnly date, CancellationToken ct)
    {
        var a = await db.DirectFineAuthorities.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == id
            && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date), ct);
        if (a is null) return null;
        var version = await db.PenaltyDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == a.PenaltyVersionId, ct);
        if (version is null) return null;
        var versions = await db.PenaltyDefinitions.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.Code == version.Code).ToListAsync(ct);
        if (!version.IsActive || version.Basis is not (GovernedServiceBasis.FixedAmount or GovernedServiceBasis.DirectApprovedAmount)
            || PenaltyDefinition.Resolve(versions, version.Code, date)?.Id != version.Id
            || (version.Basis == GovernedServiceBasis.DirectApprovedAmount && version.MaximumAmount is null && !a.AllowsUncappedAmount)) return null;
        return (a, version);
    }
    public async Task<Result<DirectFineOptionsDto>> OptionsAsync(CancellationToken ct = default)
    {
        if (!Authorized) return Result<DirectFineOptionsDto>.Forbidden();
        var ids = await db.DirectFineAuthorities.AsNoTracking().Where(x => x.MunicipalityId == Tenant).Select(x => x.Id).ToListAsync(ct);
        // Built-in Head-office collection mode: no separately configured DirectFineAuthority.
        // Keep any existing authority-backed categories for historical/backward compatibility.
        var list = new List<DirectFineCategoryDto> {
            new(Guid.Empty, "Direct fine", GovernedServiceBasis.DirectApprovedAmount,
                null, null, false, true, false, "Manual Head-office collection")
        };
        foreach (var id in ids) if (await Resolve(id, clock.PhilippineToday, ct) is { } f)
            list.Add(new(id, f.Version.DisplayName, f.Version.Basis, f.Version.FixedAmount, f.Version.MaximumAmount,
                f.Authority.RequiresPayerParticulars, f.Authority.RequiresReason, f.Authority.RequiresSupportingReference, f.Authority.GoverningReference));
        return Result<DirectFineOptionsDto>.Success(new(clock.PhilippineToday, list, $"{Tenant:N}:{user.UserId:N}"));
    }
    public async Task<Result<DirectFineQuoteDto>> QuoteAsync(DirectFineCollectionRequest request, CancellationToken ct = default)
    {
        if (!Authorized) return Result<DirectFineQuoteDto>.Forbidden();
        var r = Normalize(request);
        if (r.ClientOperationId == Guid.Empty || r.BusinessDate != clock.PhilippineToday)
            return Result<DirectFineQuoteDto>.Failure("Use the current authorized business date.", ResultStatus.Invalid);
        var manual = r.AuthorityId == Guid.Empty;
        var facts = manual ? null : await Resolve(r.AuthorityId, r.BusinessDate, ct);
        if (!manual && facts is null)
            return Result<DirectFineQuoteDto>.Failure("The selected direct-fine category is unavailable.", ResultStatus.Invalid);
        if (manual)
        {
            if (r.Amount <= 0m || decimal.Round(r.Amount, 2) != r.Amount)
                return Result<DirectFineQuoteDto>.Failure("Enter a positive fine amount in whole centavos.", ResultStatus.Invalid);
            // A reason is the minimum auditable explanation for an ad-hoc collection.
            if (r.Reason is null || r.Reason.Length > 1000 || r.SupportingReference?.Length > 200
                || r.PayerParticulars?.Length > 200)
                return Result<DirectFineQuoteDto>.Failure("Enter a reason (up to 1,000 characters), and keep optional receipt particulars within 200 characters.", ResultStatus.Invalid);
        }
        else
        {
            var f = facts!.Value;
            if (f.Version.CheckAmount(r.Amount) is { } amountError)
                return Result<DirectFineQuoteDto>.Failure(amountError switch {
                    "AMOUNT_ABOVE_CEILING" => "The fine amount exceeds the approved maximum.",
                    "AMOUNT_NOT_APPROVED" => "Enter the category's fixed approved amount.",
                    _ => "Enter a positive fine amount in whole centavos." }, ResultStatus.Invalid);
            if (r.Reason?.Length > 1000 || r.SupportingReference?.Length > 200 || r.PayerParticulars?.Length > 200
                || (f.Authority.RequiresReason && r.Reason is null) || (f.Authority.RequiresSupportingReference && r.SupportingReference is null)
                || (f.Authority.RequiresPayerParticulars && r.PayerParticulars is null))
                return Result<DirectFineQuoteDto>.Failure("Supply the category's required reason, reference and receipt particulars.", ResultStatus.Invalid);
        }
        var classification = await db.RevenueClassifications.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.SemanticCode == RevenueClassificationCodes.PenaltiesAndFines, ct);
        var policy = classification is null ? null : await Policy(classification.Id, r.BusinessDate, ct);
        if (policy is null || policy.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt)
            return Result<DirectFineQuoteDto>.Failure("The approved Fines Official Receipt policy is unavailable.", ResultStatus.Invalid);
        return Result<DirectFineQuoteDto>.Success(new(r,
            manual ? "Direct fine" : facts!.Value.Version.DisplayName,
            manual ? "Head-office manual collection" : facts!.Value.Authority.GoverningReference,
            RevenueInstrumentType.OfficialReceipt));
    }
    public async Task<Result<GovernedServiceOutcomeDto>> PostAsync(DirectFineCollectionRequest request, CancellationToken ct = default)
    {
        if (!Authorized) return Result<GovernedServiceOutcomeDto>.Forbidden();
        var r = Normalize(request); var normalized = JsonSerializer.Serialize(new { r.ClientOperationId, r.AuthorityId, r.BusinessDate,
            Amount = r.Amount.ToString("0.00##########################", System.Globalization.CultureInfo.InvariantCulture), r.Reason, r.SupportingReference, r.PayerParticulars });
        var actor = user.UserId!.Value.ToString("N");
        var fingerprint = PostingOperation.ComputeIntentFingerprint(1, normalized, Origin, actor);
        async Task<Result<GovernedServiceOutcomeDto>?> Replay()
        {
            var prior = await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.ClientOperationId == r.ClientOperationId, ct);
            if (prior is null) return null;
            if (prior.Origin != Origin || prior.ActorId != actor || prior.IntentFingerprint != fingerprint || prior.CollectionId is null)
                return Result<GovernedServiceOutcomeDto>.Failure("CollectionIntentConflict", ResultStatus.Conflict);
            var c = await db.Collections.AsNoTracking().SingleAsync(x => x.MunicipalityId == Tenant && x.Id == prior.CollectionId, ct);
            var corrected = await db.CollectionCorrections.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.OriginalCollectionId == c.Id && x.FinancialEffectAmount < 0m, ct);
            return Result<GovernedServiceOutcomeDto>.Success(new(c.Id, c.ReferenceCode, c.BusinessDate, c.TotalAmount,
                RevenueInstrumentType.OfficialReceipt, corrected ? "Corrected" : "Posted", true));
        }
        if (await Replay() is { } replay) return replay;
        var outer = db.HasActiveTransaction;
        try
        {
            await using var tx = outer ? null : await db.BeginSerializableTransactionAsync(ct);
            var quote = await QuoteAsync(r, ct);
            if (!quote.IsSuccess) return Result<GovernedServiceOutcomeDto>.Failure(quote.Error!, ResultStatus.Invalid);
            var manual = r.AuthorityId == Guid.Empty;
            var f = manual ? null : await Resolve(r.AuthorityId, r.BusinessDate, ct);
            var classification = await db.RevenueClassifications.SingleAsync(x => x.MunicipalityId == Tenant && x.SemanticCode == RevenueClassificationCodes.PenaltiesAndFines, ct);
            var policy = (await Policy(classification.Id, r.BusinessDate, ct))!;
            // Manual direct fines intentionally have no invented penalty version or approval record.
            // The canonical SRC, actor, immutable line snapshot and operation fingerprint are the audit trail.
            var snapshot = manual
                ? JsonSerializer.Serialize(new { origin = "Direct fine collection", mode = "Manual",
                    category = "Direct fine", r.Reason, r.SupportingReference, r.PayerParticulars,
                    RecordedById = user.UserId, RecordedByRole = "SuperAdmin", RecordedAtUtc = clock.UtcNow })
                : JsonSerializer.Serialize(new { origin = "Direct fine collection", category = f!.Value.Version.DisplayName,
                    penaltyVersionId = f.Value.Version.Id, f.Value.Authority.GoverningReference, f.Value.Authority.ApprovalReference,
                    f.Value.Authority.VerifiedBy, f.Value.Authority.VerifiedAtUtc, r.Reason, r.SupportingReference, r.PayerParticulars });
            var c = await new CanonicalCollectionPostingCoordinator(db).PostAsync(Tenant, r.ClientOperationId, 1, normalized,
                Origin, actor, user.Username ?? "Head", "SuperAdmin", r.BusinessDate, user.Username ?? "Head",
                [new(classification, policy, r.Amount, manual ? null : CollectionSourceKind.DirectFineCollection,
                    manual ? null : f!.Value.Authority.Id, CalculationSnapshot: snapshot)],
                payerName: r.PayerParticulars, ct: ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return Result<GovernedServiceOutcomeDto>.Success(new(c.Id, c.ReferenceCode, c.BusinessDate, c.TotalAmount, RevenueInstrumentType.OfficialReceipt, "Posted", false));
        }
        catch (Exception ex) when (!outer && (ex is DbUpdateException || ex is System.Data.Common.DbException))
        {
            db.ChangeTracker.Clear();
            return await Replay() ?? Result<GovernedServiceOutcomeDto>.Failure("Retry the same collection request.", ResultStatus.Conflict);
        }
    }
    private Task<RevenueClassificationPolicy?> Policy(Guid classification, DateOnly date, CancellationToken ct) =>
        db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.RevenueClassificationId == classification
            && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= date)
        .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
}
