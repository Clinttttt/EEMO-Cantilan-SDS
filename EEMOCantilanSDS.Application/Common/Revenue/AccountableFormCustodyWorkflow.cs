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

/// <summary>Small Head/Admin custody boundary for accountable-form books and assigned CT units.</summary>
public sealed partial class AccountableFormCustodyWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock? clock = null)
{
    private const int MaximumUnitsPerRequest = 5000;

    public Task<Result<AccountableFormBookDto>> ReceiveAsync(
        ReceiveAccountableFormBookRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        if (request.InstrumentType is not (RevenueInstrumentType.CashTicket or RevenueInstrumentType.OfficialReceipt))
            return Result<AccountableFormBookDto>.Failure("Only OR or Cash Ticket books can be received.", ResultStatus.Invalid);
        if (request.FirstSerialNumber < 0 || request.LastSerialNumber < request.FirstSerialNumber
            || request.LastSerialNumber - request.FirstSerialNumber >= MaximumUnitsPerRequest)
            return Result<AccountableFormBookDto>.Failure($"Receive at most {MaximumUnitsPerRequest} units in one request.", ResultStatus.Invalid);
        AccountableFormBook book;
        try
        {
            book = AccountableFormBook.Receive(actor.TenantId, request.InstrumentType,
                request.SeriesName, request.NumberPrefix, request.FirstSerialNumber,
                request.LastSerialNumber, request.SerialWidth, UtcNow,
                actor.ActorId, actor.Username);
        }
        catch (ArgumentException exception)
        {
            return Result<AccountableFormBookDto>.Failure(exception.Message, ResultStatus.Invalid);
        }
        var documents = new List<AccountableDocument>();
        var unitCount = checked((int)(request.LastSerialNumber - request.FirstSerialNumber + 1));
        for (var offset = 0; offset < unitCount; offset++)
        {
            var serial = request.FirstSerialNumber + offset;
            try { documents.Add(AccountableDocument.Register(book, serial, actor.Username)); }
            catch (ArgumentException exception) { return Result<AccountableFormBookDto>.Failure(exception.Message, ResultStatus.Invalid); }
        }
        db.AccountableFormBooks.Add(book);
        db.AccountableDocuments.AddRange(documents);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Result<AccountableFormBookDto>.Failure("The accountable-form range overlaps an existing document identity.", ResultStatus.Conflict);
        }
        return Result<AccountableFormBookDto>.Success(ToDto(book, documents));
    }, ct);

    public Task<Result<IReadOnlyList<AccountableFormBookDto>>> ListAsync(CancellationToken ct = default) => Run(async actor =>
    {
        var books = await db.AccountableFormBooks.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId)
            .OrderByDescending(x => x.ReceivedAtUtc).ToListAsync(ct);
        var ids = books.Select(x => x.Id).ToArray();
        var documents = await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && ids.Contains(x.FormBookId))
            .OrderBy(x => x.SerialNumber).ToListAsync(ct);
        return Result<IReadOnlyList<AccountableFormBookDto>>.Success(books.Select(book =>
            ToDto(book, documents.Where(x => x.FormBookId == book.Id))).ToList());
    }, ct);

    /// <summary>Assigns a range from a received Cash Ticket book (the original route; refuses OR books).</summary>
    public Task<Result<int>> AssignCashTicketRangeAsync(
        AssignAccountableFormRangeRequest request, CancellationToken ct = default) =>
        AssignRangeAsync(request, RevenueInstrumentType.CashTicket, ct);

    /// <summary>
    /// Assigns a range of unused in-office units from ONE received book to an active collector of this tenant. The
    /// instrument comes from the book itself, so an OR range can never be assigned as a Cash Ticket or the reverse;
    /// <paramref name="expectedInstrument"/> lets a route insist on one instrument. Assignment is custody only: it
    /// authorizes no operation, and a unit that is issued, consumed or awaiting reconciliation is never re-assigned.
    /// </summary>
    public Task<Result<int>> AssignRangeAsync(
        AssignAccountableFormRangeRequest request, RevenueInstrumentType? expectedInstrument = null,
        CancellationToken ct = default) => Run(actor => AssignLinesAsync(actor, request.FormBookId, expectedInstrument,
        [new AccountableFormBatchLine(request.AssignedUserId, request.FirstSerialNumber, request.LastSerialNumber)], ct), ct);

    /// <summary>
    /// Assigns several collectors' contiguous ranges from ONE received book in a single save: every range is assigned or
    /// none is. Ranges may not overlap, each collector appears once, and every unit must still be unused in office
    /// custody — the server decides, whatever the screen showed. Custody only: no money, no operation authorized.
    /// </summary>
    public Task<Result<int>> AssignBatchAsync(AssignAccountableFormBatchRequest request, CancellationToken ct = default) =>
        Run(actor => AssignLinesAsync(actor, request.FormBookId, request.InstrumentType, request.Lines ?? [], ct), ct);

    private const int MaximumLinesPerBatch = 50;

    private async Task<Result<int>> AssignLinesAsync(
        Actor actor, Guid formBookId, RevenueInstrumentType? expectedInstrument,
        IReadOnlyList<AccountableFormBatchLine> lines, CancellationToken ct)
    {
        if (formBookId == Guid.Empty || lines.Count == 0 || lines.Count > MaximumLinesPerBatch
            || lines.Any(x => x.AssignedUserId == Guid.Empty || x.FirstSerialNumber < 0
                || x.LastSerialNumber < x.FirstSerialNumber
                || x.LastSerialNumber - x.FirstSerialNumber >= MaximumUnitsPerRequest)
            || lines.Sum(x => x.LastSerialNumber - x.FirstSerialNumber + 1) > MaximumUnitsPerRequest)
            return Result<int>.Failure("A valid accountable-form book, collector, and bounded serial range are required.", ResultStatus.Invalid);
        if (lines.Select(x => x.AssignedUserId).Distinct().Count() != lines.Count)
            return Result<int>.Failure("Each collector can appear once in a batch; combine their ranges into one row.", ResultStatus.Invalid);
        var ordered = lines.OrderBy(x => x.FirstSerialNumber).ToList();
        for (var i = 1; i < ordered.Count; i++)
            if (ordered[i].FirstSerialNumber <= ordered[i - 1].LastSerialNumber)
                return Result<int>.Failure("Two collectors' ranges overlap. Each serial can be assigned to one collector only.", ResultStatus.Invalid);
        var book = await db.AccountableFormBooks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.Id == formBookId, ct);
        if (book is null) return Result<int>.Failure("The accountable-form book was not found in this tenant.", ResultStatus.NotFound);
        if (expectedInstrument is { } required && book.InstrumentType != required)
            return Result<int>.Failure(
                $"This route assigns {InstrumentName(required)} books only; the selected book holds {InstrumentName(book.InstrumentType)}.",
                ResultStatus.Invalid);
        if (book.InstrumentType is not (RevenueInstrumentType.CashTicket or RevenueInstrumentType.OfficialReceipt)
            || ordered[0].FirstSerialNumber < book.FirstSerialNumber
            || ordered[^1].LastSerialNumber > book.LastSerialNumber)
            return Result<int>.Failure("Assignments must stay within one received OR or Cash Ticket book.", ResultStatus.Invalid);
        var collectorIds = lines.Select(x => x.AssignedUserId).ToArray();
        var activeCollectors = await db.CollectorUsers.AsNoTracking().Where(x =>
            x.MunicipalityId == actor.TenantId && collectorIds.Contains(x.Id) && x.IsActive).Select(x => x.Id).ToListAsync(ct);
        if (activeCollectors.Count != collectorIds.Length)
            return Result<int>.Failure("An active collector in this tenant is required.", ResultStatus.Invalid);
        var instrument = book.InstrumentType;
        var low = ordered[0].FirstSerialNumber;
        var high = ordered[^1].LastSerialNumber;
        var candidates = await db.AccountableDocuments.Where(x =>
            x.MunicipalityId == actor.TenantId && x.FormBookId == formBookId
            && x.InstrumentType == instrument
            && x.SerialNumber >= low && x.SerialNumber <= high)
            .OrderBy(x => x.SerialNumber).ToListAsync(ct);
        var now = UtcNow;
        var assigned = 0;
        foreach (var line in ordered)
        {
            var documents = candidates.Where(x => x.SerialNumber >= line.FirstSerialNumber && x.SerialNumber <= line.LastSerialNumber).ToList();
            var expected = checked((int)(line.LastSerialNumber - line.FirstSerialNumber + 1));
            if (documents.Count != expected)
                return Result<int>.Failure($"Every requested {InstrumentName(instrument)} must exist in the received book.", ResultStatus.Conflict);
            var unavailable = documents.Where(x => x.State != AccountableDocumentState.InOffice).ToList();
            if (unavailable.Count > 0)
                return Result<int>.Failure(await UnavailableMessageAsync(actor, instrument, unavailable, ct), ResultStatus.Conflict);
            foreach (var document in documents)
            {
                document.AssignTo(line.AssignedUserId, actor.Username);
                db.AccountableFormAssignments.Add(AccountableFormAssignment.Assign(
                    document, line.AssignedUserId, actor.ActorId, now, actor.Username));
            }
            assigned += documents.Count;
        }
        // One save: the state concurrency token and the one-open-custody index make a concurrent assignment of any unit
        // fail the whole batch rather than split it.
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Result<int>.Failure(
                $"Another assignment changed these {InstrumentName(instrument)}s at the same time. Nothing was assigned; reload and review.",
                ResultStatus.Conflict);
        }
        return Result<int>.Success(assigned);
    }

    /// <summary>
    /// Automatic allocation: the server lays out each collector's units from the book's UNASSIGNED, IN-OFFICE units only, in
    /// serial order, so the office never does range arithmetic. Units already held by a collector, issued, consumed,
    /// spoiled or awaiting review are never touched; a gap among them simply splits a collector's share into several
    /// contiguous runs. Redistribution of units a collector already holds is a Transfer, never an allocation.
    /// Preview changes nothing. Commit recomputes the plan from current custody and assigns only when it equals the
    /// previewed plan, in one save guarded by each unit's concurrency token, so two concurrent commits cannot both
    /// assign a unit: every unit has exactly one custodian.
    /// </summary>
    public Task<Result<AutoAllocatePlanDto>> AutoAllocateAsync(AutoAllocateFormsRequest request, CancellationToken ct = default) =>
        Run(async actor =>
    {
        var shares = request.Shares ?? [];
        if (request.FormBookId == Guid.Empty || shares.Count == 0 || shares.Count > MaximumLinesPerBatch
            || shares.Any(x => x.CollectorId == Guid.Empty || x.Quantity is < 1))
            return Result<AutoAllocatePlanDto>.Failure("Choose at least one collector, with a quantity of at least 1 when one is given.", ResultStatus.Invalid);
        if (shares.Select(x => x.CollectorId).Distinct().Count() != shares.Count)
            return Result<AutoAllocatePlanDto>.Failure("Each collector can appear once in an allocation.", ResultStatus.Invalid);
        if (shares.Any(x => x.Quantity is null) && shares.Any(x => x.Quantity is not null))
            return Result<AutoAllocatePlanDto>.Failure("Use either an even share for everyone or a quantity for everyone.", ResultStatus.Invalid);

        var book = await db.AccountableFormBooks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.Id == request.FormBookId, ct);
        if (book is null) return Result<AutoAllocatePlanDto>.Failure("The accountable-form book was not found in this tenant.", ResultStatus.NotFound);
        if (book.InstrumentType != request.InstrumentType
            || book.InstrumentType is not (RevenueInstrumentType.CashTicket or RevenueInstrumentType.OfficialReceipt))
            return Result<AutoAllocatePlanDto>.Failure(
                $"The selected book holds {InstrumentName(book.InstrumentType)}s, not {InstrumentName(request.InstrumentType)}s.", ResultStatus.Invalid);

        var collectorIds = shares.Select(x => x.CollectorId).ToArray();
        var collectors = await db.CollectorUsers.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && collectorIds.Contains(x.Id) && x.IsActive)
            .Select(x => new { x.Id, x.FullName, x.Username }).ToListAsync(ct);
        if (collectors.Count != collectorIds.Length)
            return Result<AutoAllocatePlanDto>.Failure("Every collector must be an active collector in this tenant.", ResultStatus.Invalid);

        // Tracked: on commit these same rows are assigned, and their concurrency tokens guard the save.
        var inOffice = await db.AccountableDocuments.Where(x =>
                x.MunicipalityId == actor.TenantId && x.FormBookId == book.Id && x.InstrumentType == book.InstrumentType
                && x.State == AccountableDocumentState.InOffice)
            .OrderBy(x => x.SerialNumber).ToListAsync(ct);
        var unit = InstrumentName(book.InstrumentType);
        if (inOffice.Count == 0)
            return Result<AutoAllocatePlanDto>.Failure(
                $"No unassigned {unit}s are in office in this book. To give a collector {unit}s another collector holds, transfer the unused {unit}s.",
                ResultStatus.Conflict);

        int[] quantities;
        if (shares.All(x => x.Quantity is null))
        {
            // Even: every in-office unit, split as evenly as possible; the first collectors take the remainder.
            var each = inOffice.Count / shares.Count;
            var extra = inOffice.Count % shares.Count;
            if (each == 0)
                return Result<AutoAllocatePlanDto>.Failure(
                    $"Only {inOffice.Count:N0} {unit}{(inOffice.Count == 1 ? " is" : "s are")} in office — fewer than one each for {shares.Count} collectors.",
                    ResultStatus.Conflict);
            quantities = shares.Select((_, i) => each + (i < extra ? 1 : 0)).ToArray();
        }
        else
        {
            quantities = shares.Select(x => x.Quantity!.Value).ToArray();
            var requested = quantities.Sum(x => (long)x);
            if (requested > MaximumUnitsPerRequest)
                return Result<AutoAllocatePlanDto>.Failure($"Allocate at most {MaximumUnitsPerRequest:N0} units at a time.", ResultStatus.Invalid);
            if (requested > inOffice.Count)
                return Result<AutoAllocatePlanDto>.Failure(
                    $"{requested:N0} {unit}s were requested but only {inOffice.Count:N0} are unassigned in office. Lower the quantities, or transfer unused {unit}s from a collector.",
                    ResultStatus.Conflict);
        }

        var names = collectors.ToDictionary(x => x.Id, x => string.IsNullOrWhiteSpace(x.FullName) ? x.Username ?? "Collector" : x.FullName!);
        var plan = new List<(AutoAllocateShare Share, List<AccountableDocument> Units)>();
        var cursor = 0;
        for (var i = 0; i < shares.Count; i++)
        {
            plan.Add((shares[i], inOffice.GetRange(cursor, quantities[i])));
            cursor += quantities[i];
        }
        var lines = plan.Select(p => new AutoAllocatePlanLine(p.Share.CollectorId, names[p.Share.CollectorId], p.Units.Count, Runs(p.Units)))
            .ToList();
        var inOfficeAfter = inOffice.Count - cursor;

        if (!request.Commit)
            return Result<AutoAllocatePlanDto>.Success(new AutoAllocatePlanDto(book.Id, book.InstrumentType, inOffice.Count, inOfficeAfter, lines, false));

        if (request.ExpectedPlan is not { } expected || !SamePlan(expected, lines))
            return Result<AutoAllocatePlanDto>.Failure(
                $"{unit} custody changed since the preview. Nothing was assigned; review the new allocation and confirm again.",
                ResultStatus.Conflict);

        var now = UtcNow;
        foreach (var (share, units) in plan)
            foreach (var document in units)
            {
                document.AssignTo(share.CollectorId, actor.Username);
                db.AccountableFormAssignments.Add(AccountableFormAssignment.Assign(document, share.CollectorId, actor.ActorId, now, actor.Username));
            }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Result<AutoAllocatePlanDto>.Failure(
                $"Another change assigned some of these {unit}s at the same time. Nothing was assigned; reload and review.",
                ResultStatus.Conflict);
        }
        return Result<AutoAllocatePlanDto>.Success(new AutoAllocatePlanDto(book.Id, book.InstrumentType, inOffice.Count, inOfficeAfter, lines, true));
    }, ct);

    /// <summary>Consecutive serials grouped into contiguous runs.</summary>
    internal static IReadOnlyList<SerialRangeDto> Runs(IReadOnlyList<AccountableDocument> units)
    {
        var runs = new List<SerialRangeDto>();
        var i = 0;
        while (i < units.Count)
        {
            var j = i;
            while (j + 1 < units.Count && units[j + 1].SerialNumber == units[j].SerialNumber + 1) j++;
            runs.Add(new SerialRangeDto(units[i].SerialNumber, units[j].SerialNumber, units[i].DocumentNumber, units[j].DocumentNumber));
            i = j + 1;
        }
        return runs;
    }

    private static bool SamePlan(IReadOnlyList<AutoAllocatePlanLine> expected, IReadOnlyList<AutoAllocatePlanLine> actual) =>
        expected.Count == actual.Count
        && expected.Zip(actual).All(pair => pair.First.CollectorId == pair.Second.CollectorId
            && pair.First.Quantity == pair.Second.Quantity
            && pair.First.Ranges.Count == pair.Second.Ranges.Count
            && pair.First.Ranges.Zip(pair.Second.Ranges).All(r =>
                r.First.FirstSerialNumber == r.Second.FirstSerialNumber && r.First.LastSerialNumber == r.Second.LastSerialNumber));

    /// <summary>
    /// Moves assigned, unused units of ONE book from the collector who holds them to another active collector, with a
    /// reason. It closes the current custody interval and opens the new one in one save, so history reads from, to, range,
    /// by, at and why. Every unit in the range must be held by the same collector; an issued, consumed, spoiled or
    /// awaiting-review unit is refused and its history is never altered. Custody only: no money moves.
    /// </summary>
    public Task<Result<int>> TransferAsync(TransferAccountableFormsRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        if (request.FormBookId == Guid.Empty || request.ToUserId == Guid.Empty || request.FirstSerialNumber < 0
            || request.LastSerialNumber < request.FirstSerialNumber
            || request.LastSerialNumber - request.FirstSerialNumber >= MaximumUnitsPerRequest)
            return Result<int>.Failure("A valid accountable-form book, receiving collector, and bounded serial range are required.", ResultStatus.Invalid);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > AccountableFormAssignment.MaxReasonLength)
            return Result<int>.Failure($"State why the forms are moving (at most {AccountableFormAssignment.MaxReasonLength} characters).", ResultStatus.Invalid);
        var receiver = await db.CollectorUsers.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.Id == request.ToUserId && x.IsActive, ct);
        if (receiver is null) return Result<int>.Failure("An active collector in this tenant is required.", ResultStatus.Invalid);
        var documents = await db.AccountableDocuments.Where(x =>
            x.MunicipalityId == actor.TenantId && x.FormBookId == request.FormBookId
            && x.SerialNumber >= request.FirstSerialNumber && x.SerialNumber <= request.LastSerialNumber)
            .OrderBy(x => x.SerialNumber).ToListAsync(ct);
        var expected = checked((int)(request.LastSerialNumber - request.FirstSerialNumber + 1));
        if (documents.Count != expected)
            return Result<int>.Failure("Every unit in the range must exist in the selected book.", ResultStatus.Conflict);
        var instrument = documents[0].InstrumentType;
        var unavailable = documents.Where(x => x.State != AccountableDocumentState.Assigned).ToList();
        if (unavailable.Count > 0)
            return Result<int>.Failure(
                $"Only assigned, unused forms can be transferred. {await UnavailableMessageAsync(actor, instrument, unavailable, ct)}",
                ResultStatus.Conflict);
        var holders = documents.Select(x => x.AssignedUserId).Distinct().ToList();
        if (holders.Count != 1 || holders[0] is not { } fromUserId)
            return Result<int>.Failure("The range is held by more than one collector. Transfer one collector's range at a time.", ResultStatus.Conflict);
        if (fromUserId == request.ToUserId)
            return Result<int>.Failure("These forms are already with that collector.", ResultStatus.Invalid);
        var ids = documents.Select(x => x.Id).ToArray();
        var open = await db.AccountableFormAssignments.Where(x =>
            x.MunicipalityId == actor.TenantId && ids.Contains(x.AccountableDocumentId) && x.ReturnedAtUtc == null).ToListAsync(ct);
        var now = UtcNow;
        foreach (var document in documents)
        {
            foreach (var assignment in open.Where(x => x.AccountableDocumentId == document.Id))
                assignment.RecordReturn(actor.ActorId, now);
            document.TransferTo(request.ToUserId, actor.Username);
            db.AccountableFormAssignments.Add(AccountableFormAssignment.Transfer(
                document, fromUserId, request.ToUserId, request.Reason, actor.ActorId, now, actor.Username));
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Result<int>.Failure("A unit changed while it was being transferred (it may have just been issued). Nothing moved; reload and review.", ResultStatus.Conflict);
        }
        return Result<int>.Success(documents.Count);
    }, ct);

    /// <summary>Names the units that cannot be assigned, grouped by why, e.g. "CT000004 – CT000006 are assigned to Ana Reyes."</summary>
    private async Task<string> UnavailableMessageAsync(
        Actor actor, RevenueInstrumentType instrument, IReadOnlyList<AccountableDocument> unavailable, CancellationToken ct)
    {
        var holderIds = unavailable.Where(x => x.AssignedUserId is not null).Select(x => x.AssignedUserId!.Value).Distinct().ToArray();
        var names = await db.CollectorUsers.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && holderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        var parts = unavailable
            .GroupBy(x => (x.State, x.State == AccountableDocumentState.Assigned ? x.AssignedUserId : null))
            .Select(group =>
            {
                var runs = string.Join(", ", Runs(group));
                var plural = group.Count() != 1;
                var reason = group.Key.State switch
                {
                    AccountableDocumentState.Assigned => $"assigned to {(group.Key.Item2 is { } id && names.TryGetValue(id, out var name) ? name : "another collector")}",
                    AccountableDocumentState.Consumed => "already issued",
                    AccountableDocumentState.Voided => "spoiled",
                    AccountableDocumentState.ReconciliationRequired => "awaiting review",
                    AccountableDocumentState.InOffice => "in office",
                    _ => "unavailable"
                };
                return $"{runs} {(plural ? "are" : "is")} {reason}";
            });
        return $"{string.Join("; ", parts)}. No {InstrumentName(instrument)}s were assigned.";
    }

    internal static IEnumerable<string> Runs(IEnumerable<AccountableDocument> documents)
    {
        AccountableDocument? start = null, previous = null;
        foreach (var document in documents.OrderBy(x => x.SerialNumber))
        {
            if (start is null) { start = previous = document; continue; }
            if (document.SerialNumber == previous!.SerialNumber + 1) { previous = document; continue; }
            yield return start == previous ? start.DocumentNumber : $"{start.DocumentNumber} – {previous.DocumentNumber}";
            start = previous = document;
        }
        if (start is not null)
            yield return start == previous ? start.DocumentNumber : $"{start.DocumentNumber} – {previous!.DocumentNumber}";
    }

    /// <summary>
    /// Returns unused assigned units of ONE book to office custody (IA-052). Only units still Assigned and unused can return:
    /// an issued, consumed, spoiled or awaiting-reconciliation unit never becomes available again. A return is an auditable
    /// custody event (the assignment records who returned it and when); it is not consumption, not revenue and not remitted.
    /// </summary>
    public Task<Result<int>> ReturnUnusedAsync(ReturnUnusedFormsRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        if (request.FormBookId == Guid.Empty || request.FirstSerialNumber < 0 || request.LastSerialNumber < request.FirstSerialNumber
            || request.LastSerialNumber - request.FirstSerialNumber >= MaximumUnitsPerRequest)
            return Result<int>.Failure("A valid accountable-form book and bounded serial range are required.", ResultStatus.Invalid);
        var documents = await db.AccountableDocuments.Where(x =>
            x.MunicipalityId == actor.TenantId && x.FormBookId == request.FormBookId
            && x.SerialNumber >= request.FirstSerialNumber && x.SerialNumber <= request.LastSerialNumber)
            .OrderBy(x => x.SerialNumber).ToListAsync(ct);
        var expected = checked((int)(request.LastSerialNumber - request.FirstSerialNumber + 1));
        if (documents.Count != expected || documents.Any(x => x.State != AccountableDocumentState.Assigned))
            return Result<int>.Failure("Every unit in the range must exist and still be assigned and unused. An issued, consumed or spoiled form cannot return to stock.", ResultStatus.Conflict);
        var ids = documents.Select(x => x.Id).ToArray();
        var open = await db.AccountableFormAssignments.Where(x =>
            x.MunicipalityId == actor.TenantId && ids.Contains(x.AccountableDocumentId) && x.ReturnedAtUtc == null).ToListAsync(ct);
        var now = UtcNow;
        foreach (var document in documents)
        {
            document.ReturnToOffice(actor.Username);
            foreach (var assignment in open.Where(x => x.AccountableDocumentId == document.Id))
                assignment.RecordReturn(actor.ActorId, now);
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Result<int>.Failure("A unit changed while it was being returned (it may have just been issued). Reload and review.", ResultStatus.Conflict);
        }
        return Result<int>.Success(documents.Count);
    }, ct);

    /// <summary>
    /// Records that a BLANK form was spoiled or cancelled before any financial use, with its reason (IA-052). It consumes
    /// inventory and is never revenue, never a Collection and never returned to stock. An issued or consumed document is
    /// refused here: it keeps its linked correction history.
    /// </summary>
    public Task<Result<SpoiledFormDto>> SpoilAsync(SpoilFormRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var document = await db.AccountableDocuments.SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.Id == request.AccountableDocumentId, ct);
        if (document is null) return Result<SpoiledFormDto>.NotFound();
        if (document.State is not (AccountableDocumentState.InOffice or AccountableDocumentState.Assigned))
            return Result<SpoiledFormDto>.Failure(
                "Only a blank, unused form can be recorded as spoiled. An issued or consumed document keeps its correction history.", ResultStatus.Conflict);
        var custodian = document.AssignedUserId;
        AccountableFormSpoilage record;
        try
        {
            record = AccountableFormSpoilage.Record(document, custodian, request.Reason, request.Note, actor.ActorId, actor.Username, UtcNow);
            document.Void(actor.Username);
        }
        catch (ArgumentException ex) { return Result<SpoiledFormDto>.Failure(ex.Message, ResultStatus.Invalid); }
        db.AccountableFormSpoilages.Add(record);
        var open = await db.AccountableFormAssignments.Where(x =>
            x.MunicipalityId == actor.TenantId && x.AccountableDocumentId == document.Id && x.ReturnedAtUtc == null).ToListAsync(ct);
        foreach (var assignment in open) assignment.RecordReturn(actor.ActorId, record.RecordedAtUtc);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Result<SpoiledFormDto>.Failure("The form changed while it was being recorded as spoiled. Reload and review.", ResultStatus.Conflict);
        }
        return Result<SpoiledFormDto>.Success(new(document.Id, document.DocumentNumber, document.InstrumentType,
            record.Reason, record.Note, record.ActorName, record.RecordedAtUtc, record.CustodianUserId));
    }, ct);

    public Task<Result<IReadOnlyList<SpoiledFormDto>>> GetSpoiledAsync(CancellationToken ct = default) => Run(async actor =>
    {
        var rows = await (
            from spoilage in db.AccountableFormSpoilages.AsNoTracking()
            join document in db.AccountableDocuments.AsNoTracking()
                on new { spoilage.MunicipalityId, Id = spoilage.AccountableDocumentId } equals new { document.MunicipalityId, document.Id }
            where spoilage.MunicipalityId == actor.TenantId
            orderby spoilage.RecordedAtUtc descending
            select new SpoiledFormDto(document.Id, document.DocumentNumber, document.InstrumentType, spoilage.Reason,
                spoilage.Note, spoilage.ActorName, spoilage.RecordedAtUtc, spoilage.CustodianUserId)).ToListAsync(ct);
        return Result<IReadOnlyList<SpoiledFormDto>>.Success(rows);
    }, ct);

    private static string InstrumentName(RevenueInstrumentType instrument) => instrument switch
    {
        RevenueInstrumentType.OfficialReceipt => "Official Receipt",
        RevenueInstrumentType.CashTicket => "Cash Ticket",
        _ => "accountable document"
    };

    internal DateTime UtcNow => clock?.UtcNow ?? DateTime.UtcNow;

    internal Task<Result<T>> Run<T>(Func<Actor, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Task.FromResult(Result<T>.Unauthorized());
        if (currentUser.Role is not ("Admin" or "SuperAdmin"))
            return Task.FromResult(Result<T>.Forbidden());
        var tenant = municipality.MunicipalityId;
        if (tenant == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenant)
            return Task.FromResult(Result<T>.Forbidden());
        return action(new Actor(tenant, userId, currentUser.Username ?? "Office User", userId.ToString("N")));
    }

    internal static AccountableFormBookDto ToDto(AccountableFormBook book, IEnumerable<AccountableDocument> documents) =>
        new(book.Id, book.InstrumentType, book.SeriesName, book.NumberPrefix,
            book.FirstSerialNumber, book.LastSerialNumber, documents.Select(x => new CashTicketDocumentDto(
                x.Id, x.DocumentNumber, x.State, x.AssignedUserId, x.SerialNumber)).ToList(),
            book.NumberSuffix, book.FormVariant, book.Quantity, book.ReceivedOn, book.SourceAuthority, book.SourceReference, book.ReceivedAtUtc);

    internal sealed record Actor(Guid TenantId, Guid UserId, string Username, string ActorId);
}
