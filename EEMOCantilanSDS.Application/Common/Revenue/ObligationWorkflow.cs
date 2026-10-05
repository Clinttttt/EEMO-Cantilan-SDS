using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Head/Admin setup and reading of the specialized obligation accounts (IA-050): Fish/Meat Vendor Fee, Kanmanggay space
/// rental and Fiesta/Araw lot rental. The Head opens accounts and states the approved amount; nobody in the field prices
/// anything. Money is collected only by allocating a period to an Official Receipt in the Composer, so the register here
/// is a read of assessed periods and canonical allocations.
/// </summary>
public sealed class ObligationWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock? clock = null)
{
    private DateOnly BusinessToday => clock?.PhilippineToday ?? PhilippineTime.Today;

    public Task<Result<IReadOnlyList<ObligationAccountDto>>> GetAccountsAsync(ObligationKind kind, CancellationToken ct = default) =>
        Run<IReadOnlyList<ObligationAccountDto>>(async actor =>
        {
            if (!Enum.IsDefined(kind)) return Result<IReadOnlyList<ObligationAccountDto>>.Failure("Unknown obligation kind.", ResultStatus.Invalid);
            var accounts = await db.ObligationAccounts.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.Kind == kind).ToListAsync(ct);
            var quotes = (await new ObligationCollectionSource(db).GetQuotesAsync(actor.TenantId, accounts, BusinessToday, ct))
                .ToLookup(x => x.AccountId);
            var ids = accounts.Select(x => x.Id).ToArray();
            var rates = (await db.ObligationRates.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && ids.Contains(x.ObligationAccountId)).ToListAsync(ct))
                .ToLookup(x => x.ObligationAccountId);
            var payorIds = accounts.Select(x => x.PayorId).Distinct().ToArray();
            var payors = await db.Payors.AsNoTracking().Where(x => x.MunicipalityId == actor.TenantId && payorIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
            var stallIds = accounts.Where(x => x.StallId.HasValue).Select(x => x.StallId!.Value).ToArray();
            var stallNos = await db.Stalls.AsNoTracking().Where(x => x.MunicipalityId == actor.TenantId && stallIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.StallNo, ct);

            var rows = accounts.OrderBy(x => x.SubjectLabel, StringComparer.OrdinalIgnoreCase).Select(account =>
            {
                var current = ObligationRate.Resolve(rates[account.Id], BusinessToday);
                var accountQuotes = quotes[account.Id].ToList();
                return new ObligationAccountDto(
                    account.Id, account.Kind, ObligationCollectionSource.KindLabel(account.Kind), account.PayorId,
                    payors.GetValueOrDefault(account.PayorId), account.StallId,
                    account.StallId is { } sid ? stallNos.GetValueOrDefault(sid) : null,
                    account.SubjectLabel, account.Event, account.EventDate, account.ActiveFrom, account.ActiveTo,
                    current?.Amount, current?.EffectiveFrom,
                    accountQuotes.Sum(x => x.AssessedAmount), accountQuotes.Sum(x => x.SettledAmount),
                    accountQuotes.Sum(x => x.OutstandingAmount));
            }).ToList();
            return Result<IReadOnlyList<ObligationAccountDto>>.Success(rows);
        }, ct);

    /// <summary>The period-by-period register of an account: assessed, collected and remaining, oldest first.</summary>
    public Task<Result<IReadOnlyList<ObligationQuoteDto>>> GetRegisterAsync(Guid accountId, CancellationToken ct = default) =>
        Run<IReadOnlyList<ObligationQuoteDto>>(async actor =>
        {
            var account = await db.ObligationAccounts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.MunicipalityId == actor.TenantId && x.Id == accountId, ct);
            if (account is null) return Result<IReadOnlyList<ObligationQuoteDto>>.NotFound();
            return Result<IReadOnlyList<ObligationQuoteDto>>.Success(
                await new ObligationCollectionSource(db).GetQuotesAsync(actor.TenantId, [account], BusinessToday, ct));
        }, ct);

    /// <summary>
    /// NPM Fish and Meat stalls, with the Payor explicitly linked to each stall's current occupancy. The vendor context
    /// stays NPM's: this lists it, it never creates a second vendor registry.
    /// </summary>
    public Task<Result<IReadOnlyList<VendorFeeStallDto>>> GetVendorStallsAsync(CancellationToken ct = default) =>
        Run<IReadOnlyList<VendorFeeStallDto>>(async actor =>
        {
            var stalls = await db.Stalls.AsNoTracking()
                .Include(x => x.Facility).Include(x => x.Contracts).ThenInclude(x => x.Payor)
                .Where(x => x.MunicipalityId == actor.TenantId && x.Facility!.Code == FacilityCode.NPM
                    && (x.Section == MarketSection.FishSection || x.Section == MarketSection.MeatSection))
                .ToListAsync(ct);
            var withAccount = (await db.ObligationAccounts.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.Kind == ObligationKind.FishMeatVendorFee && x.ActiveTo == null
                    && x.StallId != null).Select(x => x.StallId!.Value).ToListAsync(ct)).ToHashSet();
            var today = BusinessToday;
            var rows = stalls.OrderBy(x => x.StallNo, StringComparer.OrdinalIgnoreCase).Select(stall =>
            {
                var contract = stall.OccupancyAnsweringForMonth(today.Year, today.Month, today)?.Contract;
                var payor = contract?.Payor is { } p && p.MunicipalityId == actor.TenantId ? p : null;
                return new VendorFeeStallDto(stall.Id, stall.StallNo,
                    stall.Section == MarketSection.FishSection ? "Fish" : "Meat", payor?.Id, payor?.DisplayName,
                    withAccount.Contains(stall.Id));
            }).ToList();
            return Result<IReadOnlyList<VendorFeeStallDto>>.Success(rows);
        }, ct);

    public Task<Result<ObligationAccountDto>> CreateAccountAsync(CreateObligationAccountRequest request, CancellationToken ct = default) =>
        Run<ObligationAccountDto>(async actor =>
        {
            // Opening a receivable and approving its amount is the Head authority, like classification policy.
            if (actor.Role != "SuperAdmin") return Result<ObligationAccountDto>.Forbidden();
            if (!Enum.IsDefined(request.Kind))
                return Result<ObligationAccountDto>.Failure("Unknown obligation kind.", ResultStatus.Invalid);
            if (request.Kind == ObligationKind.FishMeatVendorFee && Domain.Constants.FishMeatVendorFeeRules.UsesDirectCollection(BusinessToday))
                return Result<ObligationAccountDto>.Failure("Vendor fees are direct collections. New monthly vendor-fee accounts are no longer opened.", ResultStatus.Conflict);

            var payorId = request.PayorId;
            var stallId = request.StallId;
            if (request.Kind == ObligationKind.FishMeatVendorFee)
            {
                // The Payor comes from the NPM stall's linked occupancy; a typed or guessed Payor is never accepted.
                if (stallId is not { } sid) return Result<ObligationAccountDto>.Failure("Choose the NPM Fish or Meat stall.", ResultStatus.Invalid);
                var stall = await db.Stalls.AsNoTracking().Include(x => x.Facility)
                    .Include(x => x.Contracts).ThenInclude(x => x.Payor)
                    .SingleOrDefaultAsync(x => x.MunicipalityId == actor.TenantId && x.Id == sid, ct);
                if (stall?.Facility?.Code != FacilityCode.NPM
                    || (stall.Section != MarketSection.FishSection && stall.Section != MarketSection.MeatSection))
                    return Result<ObligationAccountDto>.Failure("A vendor fee applies to an NPM Fish or Meat stall.", ResultStatus.Invalid);
                var today = BusinessToday;
                var linked = stall.OccupancyAnsweringForMonth(today.Year, today.Month, today)?.Contract.Payor;
                if (linked is null || linked.MunicipalityId != actor.TenantId)
                    return Result<ObligationAccountDto>.Failure(
                        "This stall's occupant is not linked to a Business Payor. Link the Payor in NPM before opening a vendor fee.", ResultStatus.Conflict);
                payorId = linked.Id;
                if (await db.ObligationAccounts.AnyAsync(x => x.MunicipalityId == actor.TenantId
                        && x.Kind == ObligationKind.FishMeatVendorFee && x.StallId == sid && x.ActiveTo == null, ct))
                    return Result<ObligationAccountDto>.Failure("This stall already has an open vendor fee account.", ResultStatus.Conflict);
            }
            else if (!await db.Payors.AsNoTracking().AnyAsync(x => x.MunicipalityId == actor.TenantId && x.Id == payorId, ct))
                return Result<ObligationAccountDto>.Failure("Choose an existing Business Payor.", ResultStatus.Invalid);

            var earliest = new DateOnly(2020, 1, 1);
            var start = request.Kind == ObligationKind.FiestaArawLotRental ? request.EventDate ?? default : request.ActiveFrom;
            if (start < earliest || start > BusinessToday.AddDays(366))
                return Result<ObligationAccountDto>.Failure("Choose a realistic start or event date (not more than a year ahead).", ResultStatus.Invalid);

            ObligationAccount account;
            ObligationRate rate;
            try
            {
                account = ObligationAccount.Create(actor.TenantId, request.Kind, payorId, stallId, request.SubjectLabel ?? string.Empty,
                    request.Event, request.EventDate, request.ActiveFrom, actor.Username);
                // The first approved amount is in force from the account's first day, so its first period is billable.
                rate = ObligationRate.Create(actor.TenantId, account.Id, account.ActiveFrom, request.Amount, actor.Username);
            }
            catch (ArgumentException ex)
            {
                return Result<ObligationAccountDto>.Failure(ex.Message, ResultStatus.Invalid);
            }
            db.ObligationAccounts.Add(account);
            db.ObligationRates.Add(rate);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                return Result<ObligationAccountDto>.Failure("The account conflicts with another saved account. Reload and try again.", ResultStatus.Conflict);
            }
            var listed = await GetAccountsAsync(account.Kind, ct);
            return listed.IsSuccess && listed.Value!.FirstOrDefault(x => x.Id == account.Id) is { } dto
                ? Result<ObligationAccountDto>.Success(dto)
                : Result<ObligationAccountDto>.Failure("The account was saved but could not be reloaded.", ResultStatus.Conflict);
        }, ct);

    public async Task<Result<ObligationWorkspaceDto>> GetWorkspaceAsync(ObligationKind kind, CancellationToken ct = default)
    {
        var result = await GetAccountsAsync(kind, ct);
        if (!result.IsSuccess) return Result<ObligationWorkspaceDto>.Failure(result.Error!, result.Status);
        var rows = result.Value!;
        return Result<ObligationWorkspaceDto>.Success(new(rows, rows.Sum(a => a.AssessedToDate),
            rows.Sum(a => a.CollectedToDate), rows.Sum(a => a.OutstandingToDate)));
    }

    public async Task<Result<ObligationWorkspaceDto>> GetStatusReportAsync(ObligationKind kind, int year, CancellationToken ct = default)
    {
        if (year is < 2000 or > 2100) return Result<ObligationWorkspaceDto>.Failure("Choose a valid reporting year.", ResultStatus.Invalid);
        var accounts = await GetAccountsAsync(kind, ct);
        if (!accounts.IsSuccess) return Result<ObligationWorkspaceDto>.Failure(accounts.Error!, accounts.Status);
        var rows = new List<ObligationAccountDto>();
        foreach (var account in accounts.Value!)
        {
            var register = await GetRegisterAsync(account.Id, ct);
            if (!register.IsSuccess) return Result<ObligationWorkspaceDto>.Failure(register.Error!, register.Status);
            var periods = register.Value!.Where(q => q.PeriodStart.Year == year).ToList();
            if (periods.Count == 0) continue;
            rows.Add(account with { AssessedToDate = periods.Sum(q => q.AssessedAmount),
                CollectedToDate = periods.Sum(q => q.SettledAmount), OutstandingToDate = periods.Sum(q => q.OutstandingAmount) });
        }
        return Result<ObligationWorkspaceDto>.Success(new(rows, rows.Sum(a => a.AssessedToDate),
            rows.Sum(a => a.CollectedToDate), rows.Sum(a => a.OutstandingToDate)));
    }

    public Task<Result<ImportSpaceHoldersResult>> ImportSpaceHoldersAsync(ImportSpaceHoldersRequest request, CancellationToken ct = default) =>
        Run<ImportSpaceHoldersResult>(async actor =>
        {
            if (actor.Role != "SuperAdmin") return Result<ImportSpaceHoldersResult>.Forbidden();
            if (request.Rows is null || request.Rows.Count is 0 or > 200)
                return Result<ImportSpaceHoldersResult>.Failure("Import between 1 and 200 reviewed rows.", ResultStatus.Invalid);
            await using var transaction = await db.BeginSerializableTransactionAsync(ct);
            var payors = await db.Payors.AsNoTracking().Where(p => p.MunicipalityId == actor.TenantId)
                .Select(p => p.Id).ToListAsync(ct);
            var existing = await db.ObligationAccounts.AsNoTracking().Where(a => a.MunicipalityId == actor.TenantId)
                .Select(a => new { a.Kind, a.SubjectLabel, a.Event, a.EventDate }).ToListAsync(ct);
            static string Key(ObligationKind kind, string label, LotRentalEvent? ev, DateOnly? date) =>
                $"{kind}|{label.Trim().ToUpperInvariant()}|{ev}|{date:yyyy-MM-dd}";
            var seen = existing.Select(a => Key(a.Kind, a.SubjectLabel, a.Event, a.EventDate)).ToHashSet();
            var additions = new List<(ObligationAccount Account, ObligationRate Rate)>();
            var review = new List<string>();
            var skipped = 0;
            for (var index = 0; index < request.Rows.Count; index++)
            {
                var row = request.Rows[index];
                var input = row.Account;
                try
                {
                    if (input.Kind is not (ObligationKind.KanmanggaySpaceRental or ObligationKind.FiestaArawLotRental))
                        throw new ArgumentException("Only space and event lot accounts can be imported here.");
                    if (!payors.Contains(input.PayorId)) throw new ArgumentException("Choose an existing Business Payor.");
                    var start = input.Kind == ObligationKind.FiestaArawLotRental ? input.EventDate ?? default : input.ActiveFrom;
                    if (start < new DateOnly(2020, 1, 1) || start > BusinessToday.AddDays(366))
                        throw new ArgumentException("Choose a realistic start or event date.");
                    var account = ObligationAccount.Create(actor.TenantId, input.Kind, input.PayorId, input.StallId,
                        input.SubjectLabel, input.Event, input.EventDate, input.ActiveFrom, actor.Username);
                    var rate = ObligationRate.Create(actor.TenantId, account.Id, account.ActiveFrom, input.Amount, actor.Username);
                    if (row.ClosedOn is { } closed) account.Close(closed);
                    if (!seen.Add(Key(account.Kind, account.SubjectLabel, account.Event, account.EventDate))) { skipped++; continue; }
                    additions.Add((account, rate));
                }
                catch (ArgumentException ex) { review.Add($"Row {index + 1}: {ex.Message}"); }
            }
            // Validate the complete list before any account or financial assessment is written.
            if (review.Count > 0) return Result<ImportSpaceHoldersResult>.Success(new(0, skipped, review));
            foreach (var item in additions) { db.ObligationAccounts.Add(item.Account); db.ObligationRates.Add(item.Rate); }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<ImportSpaceHoldersResult>.Success(new(additions.Count, skipped, []));
        }, ct);

    /// <summary>Appends an approved amount. It is never retroactive: it must begin after every period already assessed.</summary>
    public Task<Result<bool>> SetRateAsync(Guid accountId, SetObligationRateRequest request, CancellationToken ct = default) =>
        Run<bool>(async actor =>
        {
            if (actor.Role != "SuperAdmin") return Result<bool>.Forbidden();
            var account = await db.ObligationAccounts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.MunicipalityId == actor.TenantId && x.Id == accountId, ct);
            if (account is null) return Result<bool>.NotFound();
            if (!account.IsMonthly)
                return Result<bool>.Failure("A lot rental has one approved amount. Open a new lot rental instead of changing it.", ResultStatus.Conflict);
            var lastAssessed = await db.ObligationPeriods.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.ObligationAccountId == accountId)
                .Select(x => (DateOnly?)x.PeriodStart).MaxAsync(ct);
            var lastRate = await db.ObligationRates.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.ObligationAccountId == accountId)
                .Select(x => (DateOnly?)x.EffectiveFrom).MaxAsync(ct);
            if ((lastAssessed is { } a && request.EffectiveFrom <= a) || (lastRate is { } r && request.EffectiveFrom <= r))
                return Result<bool>.Failure(
                    "A new amount must take effect after every period already assessed and after the previous amount. History is never re-priced.", ResultStatus.Conflict);
            if (request.EffectiveFrom > BusinessToday.AddDays(366))
                return Result<bool>.Failure("Choose a realistic effective date (not more than a year ahead).", ResultStatus.Invalid);
            try { db.ObligationRates.Add(ObligationRate.Create(actor.TenantId, accountId, request.EffectiveFrom, request.Amount, actor.Username)); }
            catch (ArgumentException ex) { return Result<bool>.Failure(ex.Message, ResultStatus.Invalid); }
            await db.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct);

    public Task<Result<bool>> CloseAccountAsync(Guid accountId, CloseObligationAccountRequest request, CancellationToken ct = default) =>
        Run<bool>(async actor =>
        {
            if (actor.Role != "SuperAdmin") return Result<bool>.Forbidden();
            var account = await db.ObligationAccounts
                .SingleOrDefaultAsync(x => x.MunicipalityId == actor.TenantId && x.Id == accountId, ct);
            if (account is null) return Result<bool>.NotFound();
            var lastAssessed = await db.ObligationPeriods.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.TenantId && x.ObligationAccountId == accountId)
                .Select(x => (DateOnly?)x.PeriodStart).MaxAsync(ct);
            if (lastAssessed is { } a && request.ActiveTo < a)
                return Result<bool>.Failure("An account cannot end before a period that has already been assessed.", ResultStatus.Conflict);
            try { account.Close(request.ActiveTo); }
            catch (ArgumentException ex) { return Result<bool>.Failure(ex.Message, ResultStatus.Invalid); }
            await db.SaveChangesAsync(ct);
            return Result<bool>.Success(true);
        }, ct);

    private async Task<Result<T>> Run<T>(Func<Actor, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Result<T>.Unauthorized();
        if (currentUser.Role is not ("Admin" or "SuperAdmin"))
            return Result<T>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<T>.Forbidden();
        try { return await action(new Actor(userId, tenantId, currentUser.Username ?? "Office User", currentUser.Role!)); }
        catch (DbUpdateException) { return Result<T>.Failure("The operation conflicts with another saved transaction.", ResultStatus.Conflict); }
    }

    private sealed record Actor(Guid UserId, Guid TenantId, string Username, string Role);
}
