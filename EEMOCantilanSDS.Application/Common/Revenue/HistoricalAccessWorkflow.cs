using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// ST-19 verified access to HISTORICAL accounts for the Payor portal. Staff issue a single-use, expiring, tenant-bound code for ONE unpaid liable account
/// (the ORIGINAL stall contract, or the space obligation account); the debtor either activates a new payor account with it or, when the contact number
/// already has an account, links it from the signed-in portal. Either way the result is a <see cref="PayorLiabilityLink"/> and read-only visibility of that
/// account's own unpaid obligations. Nothing is paid, assessed or moved here; no payor is created or merged from a name, number or spelling; a successor
/// occupant is never linked to a predecessor's account.
/// </summary>
public sealed class HistoricalAccessWorkflow(
    IAppDbContext db, ICurrentUserService user, ICurrentMunicipalityAccessor tenant, HistoricalArrearsReader arrears, IClock clock,
    IRequestTenantScope tenantScope, IMunicipalityRepository municipalities, ITokenService tokens, IPasswordHasher passwords, IStallLedgerQueries ledger)
{
    private const int DefaultValidityDays = 30;
    private const string GenericRefusal = "Invalid or expired activation code.";

    public async Task<Result<HistoricalAccessCodeDto>> IssueAsync(IssueHistoricalAccessRequest request, CancellationToken ct)
    {
        var tenantId = tenant.MunicipalityId;
        if (!user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin" or "Collector") || tenantId == Guid.Empty ||
            user.MunicipalityId is { } claimed && claimed != tenantId) return Result<HistoricalAccessCodeDto>.Forbidden();
        var contact = request.ContactNumber?.Trim();
        if (string.IsNullOrEmpty(contact) || contact.Length > 20) return Result<HistoricalAccessCodeDto>.Failure("A contact number of at most 20 characters is required.", ResultStatus.Invalid);
        if (request.LiableId == Guid.Empty || string.IsNullOrWhiteSpace(request.ConfirmedPayer)) return Result<HistoricalAccessCodeDto>.Failure("Choose the liable account and confirm its payer.", ResultStatus.Invalid);
        if (request.ValidityDays is < 1 or > 90) return Result<HistoricalAccessCodeDto>.Failure("Validity must be between 1 and 90 days.", ResultStatus.Invalid);

        // Eligibility is an actual unpaid liable account the issuer is entitled to see (a collector: assigned facilities and operations only). It is
        // never a hard-coded source list and never current occupancy.
        var account = (await arrears.GetForActorAsync(ct)).Accounts.FirstOrDefault(a => a.Kind == request.Kind &&
            (request.Kind == ArrearsAccountKind.StallRent ? a.ContractId == request.LiableId : a.ObligationAccountId == request.LiableId));
        if (account is null) return Result<HistoricalAccessCodeDto>.NotFound();
        if (!string.Equals(account.Payer.Trim(), request.ConfirmedPayer.Trim(), StringComparison.OrdinalIgnoreCase))
            return Result<HistoricalAccessCodeDto>.Failure("The liable payer on this account has changed. Refresh the Arrears list and confirm the payer again.", ResultStatus.Conflict);

        var existing = await db.PayorUsers.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Username == contact, ct);
        if (existing is not null && await LinkExistsAsync(existing.Id, account, ct))
            return Result<HistoricalAccessCodeDto>.Failure("This payor account is already linked to this historical account.", ResultStatus.Conflict);

        var stale = await db.PayorHistoricalAccessCodes.Where(x => !x.IsUsed && (request.Kind == ArrearsAccountKind.StallRent
            ? x.ContractId == request.LiableId : x.ObligationAccountId == request.LiableId)).ToListAsync(ct);
        db.PayorHistoricalAccessCodes.RemoveRange(stale);   // re-issuing replaces: only the newest code is ever redeemable

        string code;
        do code = PayorActivationCode.GenerateCode();
        while (await db.PayorHistoricalAccessCodes.IgnoreQueryFilters().AnyAsync(x => x.Code == code, ct)
               || await db.PayorActivationCodes.IgnoreQueryFilters().AnyAsync(x => x.Code == code, ct));

        var expires = DateTime.UtcNow.AddDays(request.ValidityDays ?? DefaultValidityDays);
        DateOnly? liableFrom = DateTime.TryParseExact(account.FirstPeriod, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var first)
            ? new DateOnly(first.Year, first.Month, 1) : null;
        db.PayorHistoricalAccessCodes.Add(PayorHistoricalAccessCode.Create(code, contact, account.Payer.Trim(),
            request.Kind == ArrearsAccountKind.StallRent ? request.LiableId : null, request.Kind == ArrearsAccountKind.SpaceRental ? request.LiableId : null,
            liableFrom, expires, user.Username ?? "Staff"));
        await db.SaveChangesAsync(ct);
        return Result<HistoricalAccessCodeDto>.Success(new(request.Kind, request.LiableId, account.OperationName, account.Space, account.Payer, code, contact,
            existing is null ? "Activation" : "Link", expires));
    }

    /// <summary>Anonymous: a debtor with no payor account yet activates one with an issued code and the number it was issued for.</summary>
    public async Task<Result<TokenResponseDto>> ActivateAsync(RedeemHistoricalAccessRequest request, CancellationToken ct)
    {
        var contact = request.ContactNumber?.Trim();
        var codeText = request.ActivationCode?.Trim();
        if (string.IsNullOrEmpty(codeText) || string.IsNullOrEmpty(contact) || contact.Length > 20 || (request.Password?.Length ?? 0) < 8 || (request.FullName?.Length ?? 0) > 100)
            return Result<TokenResponseDto>.Failure(GenericRefusal, ResultStatus.Invalid);
        // The code is a globally unique secret and activation is anonymous, so it is found across tenants and then pins this request to its own LGU.
        var code = await db.PayorHistoricalAccessCodes.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Code == codeText && !x.IsDeleted, ct);
        if (code is null || !code.CanBeRedeemedBy(contact)) return Result<TokenResponseDto>.Failure(GenericRefusal, ResultStatus.Invalid);
        var municipality = await municipalities.GetByIdAsync(code.MunicipalityId, ct);
        if (municipality is null) return Result<TokenResponseDto>.Failure(GenericRefusal, ResultStatus.Invalid);
        tenantScope.Use(municipality.Id, municipality.TenantCode);

        // One number = one payor. An existing account links the code from its signed-in portal instead; this never links onto it, because the code proves the
        // debt is this person's, not that the existing account's owner is the same person.
        if (await db.PayorUsers.AsNoTracking().IgnoreQueryFilters().AnyAsync(x => x.Username == contact, ct))
            return Result<TokenResponseDto>.Failure("This mobile number is already activated. Sign in and link this code from your account.", ResultStatus.Conflict);

        var payor = PayorUser.Create(code.PayerName, contact, passwords.Hash(request.Password!));
        db.PayorUsers.Add(payor);
        db.PayorLiabilityLinks.Add(PayorLiabilityLink.Create(payor.Id, code));
        code.MarkUsed(payor.Id);
        // Two simultaneous redemptions of one code (or one number): the database's unique keys let exactly one through; the loser is refused like any other
        // spent code, never shown a server error.
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Result<TokenResponseDto>.Failure(GenericRefusal, ResultStatus.Invalid); }
        return Result<TokenResponseDto>.Success(await tokens.CreateTokenResponse(payor));
    }

    /// <summary>A signed-in payor links a historical account of their own with an issued code. The code must be this tenant's and issued for their registered number.</summary>
    public async Task<Result<int>> LinkAsync(LinkHistoricalAccessRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.Role != "Payor" || user.UserId is not { } payorId || tenant.MunicipalityId == Guid.Empty) return Result<int>.Unauthorized();
        var codeText = request.ActivationCode?.Trim();
        var payor = await db.PayorUsers.FirstOrDefaultAsync(x => x.Id == payorId, ct);
        if (payor is null || string.IsNullOrEmpty(codeText)) return Result<int>.Failure(GenericRefusal, ResultStatus.Invalid);
        var code = await db.PayorHistoricalAccessCodes.FirstOrDefaultAsync(x => x.Code == codeText && x.MunicipalityId == tenant.MunicipalityId, ct);
        if (code is null || !code.CanBeRedeemedBy(payor.Username)) return Result<int>.Failure(GenericRefusal, ResultStatus.Invalid);
        var duplicate = await db.PayorLiabilityLinks.AnyAsync(x => x.PayorUserId == payorId && (code.ContractId != null ? x.ContractId == code.ContractId : x.ObligationAccountId == code.ObligationAccountId), ct);
        if (duplicate) return Result<int>.Failure("This historical account is already linked to your account.", ResultStatus.Conflict);
        db.PayorLiabilityLinks.Add(PayorLiabilityLink.Create(payorId, code));
        code.MarkUsed(payorId);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Result<int>.Failure(GenericRefusal, ResultStatus.Invalid); }
        return Result<int>.Success(1);
    }

    /// <summary>The signed-in payor's OWN linked historical accounts: each unpaid obligation with its term context, read-only. Never the stall's other lessees' debt.</summary>
    public async Task<Result<IReadOnlyList<PayorHistoricalObligationDto>>> GetMineAsync(CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.Role != "Payor" || user.UserId is not { } payorId || tenant.MunicipalityId == Guid.Empty)
            return Result<IReadOnlyList<PayorHistoricalObligationDto>>.Unauthorized();
        var tenantId = tenant.MunicipalityId;
        var today = clock.PhilippineToday;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var currentMonth = monthStart.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);   // the month in progress is not yet owed, as on the Head and Mobile
        var links = await db.PayorLiabilityLinks.AsNoTracking().Where(x => x.PayorUserId == payorId && x.MunicipalityId == tenantId).ToListAsync(ct);
        var rows = new List<PayorHistoricalObligationDto>();

        foreach (var link in links.Where(l => l.ContractId.HasValue))
        {
            var stall = await db.Stalls.AsNoTracking().Include(s => s.Facility).Include(s => s.Contracts)
                .FirstOrDefaultAsync(s => s.MunicipalityId == tenantId && s.Contracts.Any(c => c.Id == link.ContractId), ct);
            var contract = stall?.Contracts.FirstOrDefault(c => c.Id == link.ContractId);
            if (stall is null || contract is null) continue;
            var current = stall.Occupancies(today).LastOrDefault();
            var status = stall.Status != StallStatus.Active ? "Closed" : current?.Contract.Id == contract.Id ? (current.IsCurrent ? "Active" : "Lapsed")
                : "Re-let";
            var context = $"{contract.EffectivityDate:MMM yyyy} term" + (contract.EndedOn is { } ended ? $", ended {ended:MMM d, yyyy}" : "");
            foreach (var month in await ledger.GetOutstandingMonthsAsync(stall.Id, contract.Id, null, ct))
                if (!month.IsExcused && month.BalanceDue > 0m && string.CompareOrdinal(month.Period, currentMonth) < 0)
                    rows.Add(new($"contract:{contract.Id:N}", "StallRent", stall.Facility?.Name ?? "Stall Rent", $"Stall {stall.StallNo}", status, context,
                        month.Period, month.TotalBill, month.AmountPaid, month.BalanceDue, month.Status.ToString(), "PayAtOfficeOrCollector", false));
        }

        var spaceIds = links.Where(l => l.ObligationAccountId.HasValue).Select(l => l.ObligationAccountId!.Value).ToArray();
        if (spaceIds.Length > 0)
        {
            var accounts = await db.ObligationAccounts.AsNoTracking().Where(x => x.MunicipalityId == tenantId && spaceIds.Contains(x.Id)).ToListAsync(ct);
            var terms = (await db.SpaceContractTerms.AsNoTracking().Where(x => x.MunicipalityId == tenantId && spaceIds.Contains(x.ObligationAccountId)).ToListAsync(ct))
                .GroupBy(x => x.ObligationAccountId).ToDictionary(g => g.Key, g => SpaceTermSplit.Ordered(g));
            var quotes = await new ObligationCollectionSource(db).GetQuotesAsync(tenantId, accounts, today, ct);
            var byId = accounts.ToDictionary(a => a.Id);
            foreach (var q in quotes.Where(q => q.OutstandingAmount > 0m && (ObligationAccount.IsMonthlySpaceKind(q.Kind) ? q.PeriodStart < monthStart : q.PeriodStart < today)))
            {
                var account = byId[q.AccountId];
                var accountTerms = terms.GetValueOrDefault(q.AccountId) ?? [];
                var earlier = SpaceTermSplit.CurrentTermStart(accountTerms) is { } termStart && q.PeriodStart < termStart;
                var status = account.ActiveTo is { } to && to < today ? "Closed" : earlier ? "Renewed" : SpaceArrears.TermLastDay(account, accountTerms) is { } last && last < today ? "Lapsed" : "Active";
                rows.Add(new($"space:{q.AccountId:N}", "SpaceRental", q.KindLabel, q.SubjectLabel, status, earlier ? "Earlier term" : "Current term",
                    q.PeriodStart.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), q.AssessedAmount, q.SettledAmount, q.OutstandingAmount,
                    q.SettledAmount > 0m ? "Partial" : "Unpaid", "PayAtOfficeOrCollector", false));
            }
        }
        return Result<IReadOnlyList<PayorHistoricalObligationDto>>.Success(rows.OrderBy(r => r.AccountKey, StringComparer.Ordinal).ThenBy(r => r.Period, StringComparer.Ordinal).ToArray());
    }

    private Task<bool> LinkExistsAsync(Guid payorUserId, ArrearsAccountDto account, CancellationToken ct) =>
        db.PayorLiabilityLinks.AnyAsync(x => x.PayorUserId == payorUserId && (account.Kind == ArrearsAccountKind.StallRent
            ? x.ContractId == account.ContractId : x.ObligationAccountId == account.ObligationAccountId), ct);
}
