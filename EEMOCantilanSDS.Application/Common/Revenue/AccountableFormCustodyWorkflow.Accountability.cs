using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Accountable-form accountability beyond custody: registering a range from its printed serials, recording loss (whole set or
/// by copy), attaching external follow-up references, and reading the position, the exceptions and the history. Custody,
/// money, remittance and revenue stay separate ledgers (IA-052): nothing here creates a Collection or changes a remittance, and
/// no Treasurer account or approval is involved (IA-058).
/// </summary>
public sealed partial class AccountableFormCustodyWorkflow
{
    private DateOnly TodayLocal => clock?.PhilippineToday ?? PhilippineTime.Today;

    /// <summary>
    /// Registers a received range from the serials exactly as printed. The printed value is kept verbatim on every unit; a
    /// normalized key only detects the same serial written two harmless ways. A range that cannot be counted from its first to
    /// its last serial is refused rather than guessed. The quantity is whatever the range actually holds.
    /// </summary>
    public Task<Result<AccountableFormBookDto>> RegisterAsync(
        RegisterAccountableFormsRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        if (request.InstrumentType is not (RevenueInstrumentType.CashTicket or RevenueInstrumentType.OfficialReceipt))
            return Result<AccountableFormBookDto>.Failure("Only Official Receipt or Cash Ticket stock can be registered.", ResultStatus.Invalid);
        if (!AccountableSerial.TryParseRange(request.FirstSerial, request.LastSerial, out var start, out var lastNumber, out var rangeError))
            return Result<AccountableFormBookDto>.Failure(rangeError, ResultStatus.Invalid);
        var quantity = lastNumber - start.Number + 1;
        if (quantity > MaximumUnitsPerRequest)
            return Result<AccountableFormBookDto>.Failure($"Register at most {MaximumUnitsPerRequest:N0} forms in one range.", ResultStatus.Invalid);
        if (request.Quantity is { } stated && stated != quantity)
            return Result<AccountableFormBookDto>.Failure(
                $"That range holds {quantity:N0} form{(quantity == 1 ? "" : "s")}, not {stated:N0}. Check the first and last serial.", ResultStatus.Invalid);
        if (request.ReceivedOn is { } on && on > TodayLocal.AddDays(1))
            return Result<AccountableFormBookDto>.Failure("The received date cannot be in the future.", ResultStatus.Invalid);

        var variant = (request.FormVariant ?? string.Empty).Trim();
        var series = request.InstrumentType == RevenueInstrumentType.OfficialReceipt
            ? (variant.Length > 0 ? $"AF No. {variant}" : "AF No. 51") : "Cash Ticket";
        AccountableFormBook book;
        try
        {
            book = AccountableFormBook.Receive(actor.TenantId, request.InstrumentType, series, start.Prefix, start.Suffix, variant,
                start.Number, lastNumber, start.Digits, UtcNow, request.ReceivedOn, request.SourceAuthority, request.SourceReference,
                actor.ActorId, actor.Username);
        }
        catch (ArgumentException exception) { return Result<AccountableFormBookDto>.Failure(exception.Message, ResultStatus.Invalid); }

        var documents = new List<AccountableDocument>((int)quantity);
        for (var serial = start.Number; serial <= lastNumber; serial++)
            documents.Add(AccountableDocument.Register(book, serial, actor.Username));

        // The same physical serial may not be registered twice, whatever its spacing or case, whichever book it sits in.
        var keys = documents.Select(x => x.NormalizedNumber).ToArray();
        var clash = await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.TenantId && x.InstrumentType == request.InstrumentType && keys.Contains(x.NormalizedNumber))
            .OrderBy(x => x.SerialNumber).Select(x => x.DocumentNumber).Take(3).ToListAsync(ct);
        if (clash.Count > 0)
            return Result<AccountableFormBookDto>.Failure(
                $"{string.Join(", ", clash)} {(clash.Count == 1 ? "is" : "are")} already registered. Nothing was registered.", ResultStatus.Conflict);

        db.AccountableFormBooks.Add(book);
        db.AccountableDocuments.AddRange(documents);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Result<AccountableFormBookDto>.Failure("Part of this range was registered at the same moment. Nothing was registered; reload and review.", ResultStatus.Conflict);
        }
        return Result<AccountableFormBookDto>.Success(ToDto(book, documents));
    }, ct);

    /// <summary>
    /// Reports a serial or inclusive range lost or missing, whole or by copy. A blank unit is blocked from issuance at once
    /// and for good; the external notice or reference can be added afterwards. An issued unit keeps its financial state and
    /// the report is recorded as an exception on it. This grants no relief from accountability (an external COA process).
    /// </summary>
    public Task<Result<FormLossResultDto>> ReportLossAsync(ReportFormLossRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        if (request.FormBookId == Guid.Empty || request.FirstSerialNumber < 0 || request.LastSerialNumber < request.FirstSerialNumber
            || request.LastSerialNumber - request.FirstSerialNumber >= MaximumUnitsPerRequest)
            return Result<FormLossResultDto>.Failure("A valid accountable-form book and bounded serial range are required.", ResultStatus.Invalid);
        var today = TodayLocal;
        if (request.LostOn > today)
            return Result<FormLossResultDto>.Failure("The loss cannot be dated in the future.", ResultStatus.Invalid);
        var documents = await RangeAsync(actor.TenantId, request.FormBookId, request.FirstSerialNumber, request.LastSerialNumber, ct);
        if (documents is null)
            return Result<FormLossResultDto>.Failure("Every serial in the range must exist in the selected book.", ResultStatus.Conflict);

        var ids = documents.Select(x => x.Id).ToArray();
        var open = await db.AccountableFormAssignments.Where(x =>
            x.MunicipalityId == actor.TenantId && ids.Contains(x.AccountableDocumentId) && x.ReturnedAtUtc == null).ToListAsync(ct);
        var now = UtcNow;
        var blocked = 0;
        try
        {
            foreach (var document in documents)
            {
                var blank = document.State is AccountableDocumentState.InOffice or AccountableDocumentState.Assigned;
                var custodian = document.AssignedUserId;
                db.AccountableFormLossReports.Add(AccountableFormLossReport.Record(document, custodian, request.Copies, request.LostOn,
                    request.Place, request.Narrative, today, blank, actor.ActorId, actor.Username, now));
                if (!blank) continue;
                document.MarkLost(actor.Username);
                foreach (var assignment in open.Where(x => x.AccountableDocumentId == document.Id)) assignment.RecordReturn(actor.ActorId, now);
                blocked++;
            }
        }
        catch (ArgumentException ex) { return Result<FormLossResultDto>.Failure(ex.Message, ResultStatus.Invalid); }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Result<FormLossResultDto>.Failure("A form changed while the loss was being recorded (it may have just been issued). Nothing was recorded; reload and review.", ResultStatus.Conflict);
        }
        return Result<FormLossResultDto>.Success(new(documents.Count, blocked, documents[0].DocumentNumber, documents[^1].DocumentNumber));
    }, ct);

    /// <summary>
    /// Adds the external reference (the RCD that carried a cancelled form, or the notice/report for a loss) to forms already
    /// recorded as cancelled or lost. Append-only; it changes no state and clears the "Needs follow-up" flag.
    /// </summary>
    public Task<Result<FormReferenceResultDto>> AddReferenceAsync(AddFormReferenceRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        if (request.FormBookId == Guid.Empty || request.FirstSerialNumber < 0 || request.LastSerialNumber < request.FirstSerialNumber
            || request.LastSerialNumber - request.FirstSerialNumber >= MaximumUnitsPerRequest)
            return Result<FormReferenceResultDto>.Failure("A valid accountable-form book and bounded serial range are required.", ResultStatus.Invalid);
        var documents = await RangeAsync(actor.TenantId, request.FormBookId, request.FirstSerialNumber, request.LastSerialNumber, ct);
        if (documents is null)
            return Result<FormReferenceResultDto>.Failure("Every serial in the range must exist in the selected book.", ResultStatus.Conflict);
        var ids = documents.Select(x => x.Id).ToArray();
        var recorded = request.Kind == AccountableFormReferenceKind.Cancellation
            ? await db.AccountableFormSpoilages.AsNoTracking().Where(x => x.MunicipalityId == actor.TenantId && ids.Contains(x.AccountableDocumentId))
                .Select(x => x.AccountableDocumentId).ToListAsync(ct)
            : await db.AccountableFormLossReports.AsNoTracking().Where(x => x.MunicipalityId == actor.TenantId && ids.Contains(x.AccountableDocumentId))
                .Select(x => x.AccountableDocumentId).Distinct().ToListAsync(ct);
        if (recorded.Count != ids.Length)
            return Result<FormReferenceResultDto>.Failure(
                request.Kind == AccountableFormReferenceKind.Cancellation
                    ? "A cancellation reference can only be added to forms already recorded as cancelled."
                    : "A loss reference can only be added to forms already reported lost.", ResultStatus.Conflict);
        var now = UtcNow;
        try
        {
            foreach (var document in documents)
                db.AccountableFormReferences.Add(AccountableFormReference.Record(document, request.Kind, request.Reference, request.Note,
                    actor.ActorId, actor.Username, now));
        }
        catch (ArgumentException ex) { return Result<FormReferenceResultDto>.Failure(ex.Message, ResultStatus.Invalid); }
        await db.SaveChangesAsync(ct);
        return Result<FormReferenceResultDto>.Success(new(documents.Count, documents[0].DocumentNumber, documents[^1].DocumentNumber));
    }, ct);

    private async Task<List<AccountableDocument>?> RangeAsync(Guid tenantId, Guid bookId, long first, long last, CancellationToken ct)
    {
        var documents = await db.AccountableDocuments.Where(x =>
            x.MunicipalityId == tenantId && x.FormBookId == bookId && x.SerialNumber >= first && x.SerialNumber <= last)
            .OrderBy(x => x.SerialNumber).ToListAsync(ct);
        return documents.Count == checked((int)(last - first + 1)) ? documents : null;
    }

    /// <summary>Every cancelled and lost form with its reason, actor and whether its external follow-up is still missing.</summary>
    public Task<Result<IReadOnlyList<AccountableFormExceptionDto>>> GetExceptionsAsync(CancellationToken ct = default) => Run(async actor =>
    {
        var tenant = actor.TenantId;
        var spoiled = await (
            from s in db.AccountableFormSpoilages.AsNoTracking()
            join d in db.AccountableDocuments.AsNoTracking() on new { s.MunicipalityId, Id = s.AccountableDocumentId } equals new { d.MunicipalityId, d.Id }
            where s.MunicipalityId == tenant
            select new { d.Id, d.DocumentNumber, d.InstrumentType, s.Reason, s.Note, s.ActorName, s.RecordedAtUtc, s.CustodianUserId }).ToListAsync(ct);
        var lost = await (
            from l in db.AccountableFormLossReports.AsNoTracking()
            join d in db.AccountableDocuments.AsNoTracking() on new { l.MunicipalityId, Id = l.AccountableDocumentId } equals new { d.MunicipalityId, d.Id }
            where l.MunicipalityId == tenant
            select new { d.Id, d.DocumentNumber, d.InstrumentType, l.CopiesLost, l.LostOn, l.Place, l.Narrative, l.ActorName, l.RecordedAtUtc, l.CustodianUserId }).ToListAsync(ct);
        var references = await db.AccountableFormReferences.AsNoTracking().Where(x => x.MunicipalityId == tenant)
            .Select(x => new { x.AccountableDocumentId, x.Kind, x.Reference }).ToListAsync(ct);
        var names = await CollectorNamesAsync(tenant, spoiled.Select(x => x.CustodianUserId).Concat(lost.Select(x => x.CustodianUserId)), ct);

        IReadOnlyList<string> RefsOf(Guid id, AccountableFormReferenceKind kind) =>
            references.Where(r => r.AccountableDocumentId == id && r.Kind == kind).Select(r => r.Reference).ToList();
        string? Name(Guid? id) => id is { } v && names.TryGetValue(v, out var n) ? n : null;

        var rows = new List<AccountableFormExceptionDto>();
        foreach (var s in spoiled)
        {
            var refs = RefsOf(s.Id, AccountableFormReferenceKind.Cancellation);
            rows.Add(new(s.Id, s.DocumentNumber, s.InstrumentType, "Cancelled", string.IsNullOrWhiteSpace(s.Note) ? s.Reason : $"{s.Reason} — {s.Note}",
                null, null, s.ActorName, s.RecordedAtUtc, s.CustodianUserId, Name(s.CustodianUserId), refs.Count == 0, refs));
        }
        foreach (var l in lost)
        {
            var refs = RefsOf(l.Id, AccountableFormReferenceKind.Loss);
            rows.Add(new(l.Id, l.DocumentNumber, l.InstrumentType, CopiesLabel(l.CopiesLost), l.Narrative, l.Place, l.LostOn, l.ActorName,
                l.RecordedAtUtc, l.CustodianUserId, Name(l.CustodianUserId), refs.Count == 0, refs));
        }
        return Result<IReadOnlyList<AccountableFormExceptionDto>>.Success(rows.OrderByDescending(x => x.RecordedAtUtc).ThenBy(x => x.DocumentNumber).ToList());
    }, ct);

    /// <summary>The user-facing name of what is missing, never an internal value.</summary>
    public static string CopiesLabel(AccountableFormCopies copies)
    {
        if (copies == AccountableFormCopies.WholeSet) return "Lost — entire set";
        var parts = new List<string>();
        if (copies.HasFlag(AccountableFormCopies.Original)) parts.Add("Original");
        if (copies.HasFlag(AccountableFormCopies.Duplicate)) parts.Add("Duplicate");
        if (copies.HasFlag(AccountableFormCopies.Triplicate)) parts.Add("Triplicate");
        return $"{string.Join(" and ", parts)} missing";
    }

    private async Task<Dictionary<Guid, string>> CollectorNamesAsync(Guid tenant, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var wanted = ids.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        if (wanted.Length == 0) return [];
        var rows = await db.CollectorUsers.AsNoTracking().Where(x => x.MunicipalityId == tenant && wanted.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName, x.Username }).ToListAsync(ct);
        return rows.ToDictionary(x => x.Id, x => string.IsNullOrWhiteSpace(x.FullName) ? x.Username ?? "Collector" : x.FullName!);
    }

    private sealed record UnitFact(Guid Id, Guid BookId, long Serial, string Number, AccountableDocumentState State, Guid? AssignedUserId, DateTime? ConsumedAtUtc);

    /// <summary>When a form left stock (issued, cancelled or lost), or null while it is still on hand.</summary>
    private static DateTime? GoneAt(UnitFact unit, IReadOnlyDictionary<Guid, DateTime> cancelledAt, IReadOnlyDictionary<Guid, DateTime> lostAt) => unit.State switch
    {
        AccountableDocumentState.Consumed or AccountableDocumentState.ReconciliationRequired => unit.ConsumedAtUtc,
        AccountableDocumentState.Voided => cancelledAt.TryGetValue(unit.Id, out var c) ? c : null,
        AccountableDocumentState.Lost => lostAt.TryGetValue(unit.Id, out var l) ? l : null,
        _ => null
    };

    /// <summary>
    /// The physical position of one instrument, in counts and serials (never pesos): registered, in office, assigned, issued,
    /// cancelled, lost, needing review and skipped (an unused serial lower than one already issued from the same book), with
    /// each collector's holdings. It reads only the form ledger.
    /// </summary>
    public Task<Result<AccountableFormPositionDto>> GetPositionAsync(RevenueInstrumentType instrument, CancellationToken ct = default) => Run(async actor =>
    {
        var tenant = actor.TenantId;
        var units = await db.AccountableDocuments.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.InstrumentType == instrument)
            .Select(x => new UnitFact(x.Id, x.FormBookId, x.SerialNumber, x.DocumentNumber, x.State, x.AssignedUserId, x.ConsumedAtUtc)).ToListAsync(ct);
        var ids = units.Select(x => x.Id).ToHashSet();

        var spoiledCustodians = await (
            from s in db.AccountableFormSpoilages.AsNoTracking()
            join d in db.AccountableDocuments.AsNoTracking() on new { s.MunicipalityId, Id = s.AccountableDocumentId } equals new { d.MunicipalityId, d.Id }
            where s.MunicipalityId == tenant && d.InstrumentType == instrument
            select new { s.AccountableDocumentId, s.CustodianUserId }).ToListAsync(ct);
        var lostReports = await (
            from l in db.AccountableFormLossReports.AsNoTracking()
            join d in db.AccountableDocuments.AsNoTracking() on new { l.MunicipalityId, Id = l.AccountableDocumentId } equals new { d.MunicipalityId, d.Id }
            where l.MunicipalityId == tenant && d.InstrumentType == instrument
            select new { l.AccountableDocumentId, l.CustodianUserId, l.BlockedFromIssue }).ToListAsync(ct);
        var refs = await db.AccountableFormReferences.AsNoTracking().Where(x => x.MunicipalityId == tenant)
            .Select(x => new { x.AccountableDocumentId, x.Kind }).ToListAsync(ct);
        var cancelRef = refs.Where(r => r.Kind == AccountableFormReferenceKind.Cancellation).Select(r => r.AccountableDocumentId).ToHashSet();
        var lossRef = refs.Where(r => r.Kind == AccountableFormReferenceKind.Loss).Select(r => r.AccountableDocumentId).ToHashSet();

        // Who issued a unit: the collector whose custody interval it was issued from (consumption leaves the interval open).
        var consumedIds = units.Where(x => x.State is AccountableDocumentState.Consumed or AccountableDocumentState.ReconciliationRequired).Select(x => x.Id).ToArray();
        var holders = consumedIds.Length == 0 ? [] : (await db.AccountableFormAssignments.AsNoTracking()
            .Where(x => x.MunicipalityId == tenant && consumedIds.Contains(x.AccountableDocumentId))
            .Select(x => new { x.AccountableDocumentId, x.AssignedUserId, x.AssignedAtUtc }).ToListAsync(ct))
            .GroupBy(x => x.AccountableDocumentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.AssignedAtUtc).First().AssignedUserId);

        var maxIssued = units.Where(x => x.State is AccountableDocumentState.Consumed or AccountableDocumentState.ReconciliationRequired)
            .GroupBy(x => x.BookId).ToDictionary(g => g.Key, g => g.Max(x => x.Serial));
        var skipped = units.Count(x => x.State is AccountableDocumentState.InOffice or AccountableDocumentState.Assigned
            && maxIssued.TryGetValue(x.BookId, out var max) && x.Serial < max);

        var cancelled = units.Where(x => x.State == AccountableDocumentState.Voided).ToList();
        var lostUnits = units.Where(x => x.State == AccountableDocumentState.Lost).ToList();
        var followUp = cancelled.Count(x => !cancelRef.Contains(x.Id))
            + lostReports.Select(x => x.AccountableDocumentId).Distinct().Count(id => ids.Contains(id) && !lossRef.Contains(id));
        var totals = new AccountableFormCountsDto(
            units.Count,
            units.Count(x => x.State == AccountableDocumentState.InOffice),
            units.Count(x => x.State == AccountableDocumentState.Assigned),
            units.Count(x => x.State is AccountableDocumentState.Consumed or AccountableDocumentState.ReconciliationRequired),
            cancelled.Count, lostUnits.Count,
            units.Count(x => x.State == AccountableDocumentState.ReconciliationRequired) + skipped + followUp,
            skipped, followUp);

        var collectorIds = units.Where(x => x.AssignedUserId.HasValue).Select(x => x.AssignedUserId)
            .Concat(holders.Values.Select(x => (Guid?)x))
            .Concat(spoiledCustodians.Select(x => x.CustodianUserId)).Concat(lostReports.Select(x => x.CustodianUserId)).ToList();
        var names = await CollectorNamesAsync(tenant, collectorIds, ct);

        var custodians = new List<AccountableFormCustodianPositionDto>();
        foreach (var id in collectorIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct())
        {
            var held = units.Where(x => x.State == AccountableDocumentState.Assigned && x.AssignedUserId == id).OrderBy(x => x.BookId).ThenBy(x => x.Serial).ToList();
            custodians.Add(new(id, names.GetValueOrDefault(id, "Collector"),
                held.Count,
                holders.Count(h => h.Value == id && units.Any(u => u.Id == h.Key && u.State == AccountableDocumentState.Consumed))
                    + holders.Count(h => h.Value == id && units.Any(u => u.Id == h.Key && u.State == AccountableDocumentState.ReconciliationRequired)),
                spoiledCustodians.Count(x => x.CustodianUserId == id),
                lostReports.Where(x => x.CustodianUserId == id && x.BlockedFromIssue).Count(),
                holders.Count(h => h.Value == id && units.Any(u => u.Id == h.Key && u.State == AccountableDocumentState.ReconciliationRequired)),
                RangesOf(held)));
        }
        var office = units.Where(x => x.State == AccountableDocumentState.InOffice).OrderBy(x => x.BookId).ThenBy(x => x.Serial).ToList();
        return Result<AccountableFormPositionDto>.Success(new(instrument, totals,
            custodians.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(), RangesOf(office)));
    }, ct);

    private static IReadOnlyList<SerialRangeDto> RangesOf(IReadOnlyList<UnitFact> ordered)
    {
        var runs = new List<SerialRangeDto>();
        var i = 0;
        while (i < ordered.Count)
        {
            var j = i;
            while (j + 1 < ordered.Count && ordered[j + 1].BookId == ordered[j].BookId && ordered[j + 1].Serial == ordered[j].Serial + 1) j++;
            runs.Add(new SerialRangeDto(ordered[i].Serial, ordered[j].Serial, ordered[i].Number, ordered[j].Number));
            i = j + 1;
        }
        return runs;
    }

    /// <summary>
    /// The operational accountability support view for one month, derived from the form ledger by event time: beginning
    /// balance, receipts, issued, cancelled, lost and ending balance per range, each with its quantity and inclusive serials. It
    /// supports preparing the prescribed RAAF; it is not that report.
    /// </summary>
    public Task<Result<AccountableFormRaafSupportDto>> GetRaafSupportAsync(
        RevenueInstrumentType instrument, int year, int month, CancellationToken ct = default) => Run(async actor =>
    {
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            return Result<AccountableFormRaafSupportDto>.Failure("Choose a valid year and month.", ResultStatus.Invalid);
        var tenant = actor.TenantId;
        // Month boundaries in Philippine time, as the office reads its months.
        var startLocal = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var offset = TimeSpan.FromHours(8);
        var from = DateTime.SpecifyKind(startLocal - offset, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(startLocal.AddMonths(1) - offset, DateTimeKind.Utc);

        var books = await db.AccountableFormBooks.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.InstrumentType == instrument)
            .Select(x => new { x.Id, x.SeriesName, x.ReceivedAtUtc }).ToListAsync(ct);
        var units = await db.AccountableDocuments.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.InstrumentType == instrument)
            .Select(x => new UnitFact(x.Id, x.FormBookId, x.SerialNumber, x.DocumentNumber, x.State, null, x.ConsumedAtUtc)).ToListAsync(ct);
        var cancelledAt = await (
            from s in db.AccountableFormSpoilages.AsNoTracking()
            join d in db.AccountableDocuments.AsNoTracking() on new { s.MunicipalityId, Id = s.AccountableDocumentId } equals new { d.MunicipalityId, d.Id }
            where s.MunicipalityId == tenant && d.InstrumentType == instrument
            select new { s.AccountableDocumentId, s.RecordedAtUtc }).ToDictionaryAsync(x => x.AccountableDocumentId, x => x.RecordedAtUtc, ct);
        var lostAt = (await (
            from l in db.AccountableFormLossReports.AsNoTracking()
            join d in db.AccountableDocuments.AsNoTracking() on new { l.MunicipalityId, Id = l.AccountableDocumentId } equals new { d.MunicipalityId, d.Id }
            where l.MunicipalityId == tenant && d.InstrumentType == instrument && l.BlockedFromIssue
            select new { l.AccountableDocumentId, l.RecordedAtUtc }).ToListAsync(ct))
            .GroupBy(x => x.AccountableDocumentId).ToDictionary(g => g.Key, g => g.Min(x => x.RecordedAtUtc));

        var rows = new List<AccountableFormRaafRowDto>();
        foreach (var book in books.OrderBy(x => x.ReceivedAtUtc))
        {
            var mine = units.Where(x => x.BookId == book.Id).OrderBy(x => x.Serial).ToList();
            // A form has left stock (issued, cancelled or lost) at its event time; before that it was on hand once received.
            var received = book.ReceivedAtUtc;
            var beginning = new List<UnitFact>(); var receipts = new List<UnitFact>(); var issued = new List<UnitFact>();
            var cancelled = new List<UnitFact>(); var lost = new List<UnitFact>(); var ending = new List<UnitFact>();
            foreach (var u in mine)
            {
                var fact = u;
                var gone = GoneAt(u, cancelledAt, lostAt);
                if (received >= to) continue;
                var goneBeforeStart = gone is { } g0 && g0 < from;
                if (received < from && !goneBeforeStart) beginning.Add(fact);
                if (received >= from && received < to) receipts.Add(fact);
                if (gone is { } g && g >= from && g < to)
                {
                    if (u.State is AccountableDocumentState.Consumed or AccountableDocumentState.ReconciliationRequired) issued.Add(fact);
                    else if (u.State == AccountableDocumentState.Voided) cancelled.Add(fact);
                    else if (u.State == AccountableDocumentState.Lost) lost.Add(fact);
                }
                if (!(gone is { } ge && ge < to)) ending.Add(fact);
            }
            if (beginning.Count + receipts.Count + issued.Count + cancelled.Count + lost.Count + ending.Count == 0) continue;
            rows.Add(new(book.Id, book.SeriesName, beginning.Count, receipts.Count, issued.Count, cancelled.Count, lost.Count, ending.Count,
                RangesOf(beginning), RangesOf(receipts), RangesOf(issued), RangesOf(cancelled), RangesOf(lost), RangesOf(ending)));
        }
        return Result<AccountableFormRaafSupportDto>.Success(new(instrument, year, month, rows));
    }, ct);

    /// <summary>
    /// The auditable trail of the form ledger, newest first: registered, assigned, transferred, returned, issued, cancelled,
    /// reported lost and follow-up references, with who, when, from/to custody and the reason or reference. Consecutive serials of
    /// one event are shown as one range.
    /// </summary>
    public Task<Result<IReadOnlyList<AccountableFormHistoryEventDto>>> GetHistoryAsync(
        RevenueInstrumentType instrument, int limit = 200, CancellationToken ct = default) => Run(async actor =>
    {
        var tenant = actor.TenantId;
        var take = Math.Clamp(limit, 1, 1000);
        var units = await db.AccountableDocuments.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.InstrumentType == instrument)
            .Select(x => new { x.Id, x.FormBookId, x.SerialNumber, x.DocumentNumber, x.State, x.ConsumedAtUtc, x.AssignedUserId }).ToListAsync(ct);
        var byId = units.ToDictionary(x => x.Id);
        var books = await db.AccountableFormBooks.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.InstrumentType == instrument).ToListAsync(ct);
        var assignments = await db.AccountableFormAssignments.AsNoTracking().Where(x => x.MunicipalityId == tenant).ToListAsync(ct);
        assignments = assignments.Where(x => byId.ContainsKey(x.AccountableDocumentId)).ToList();
        var spoilages = await db.AccountableFormSpoilages.AsNoTracking().Where(x => x.MunicipalityId == tenant).ToListAsync(ct);
        spoilages = spoilages.Where(x => byId.ContainsKey(x.AccountableDocumentId)).ToList();
        var losses = await db.AccountableFormLossReports.AsNoTracking().Where(x => x.MunicipalityId == tenant).ToListAsync(ct);
        losses = losses.Where(x => byId.ContainsKey(x.AccountableDocumentId)).ToList();
        var references = await db.AccountableFormReferences.AsNoTracking().Where(x => x.MunicipalityId == tenant).ToListAsync(ct);
        references = references.Where(x => byId.ContainsKey(x.AccountableDocumentId)).ToList();

        var staffIds = books.Select(x => x.ReceivedByActorId).Concat(assignments.Select(x => x.AssignedByActorId))
            .Concat(assignments.Select(x => x.ReturnedByActorId).Where(x => x != null).Select(x => x!)).Distinct().ToList();
        var staff = new Dictionary<string, string>();
        foreach (var admin in await db.AdminUsers.AsNoTracking().Select(x => new { x.Id, x.FullName, x.Username }).ToListAsync(ct))
            staff[admin.Id.ToString("N")] = string.IsNullOrWhiteSpace(admin.FullName) ? admin.Username ?? "Office" : admin.FullName!;
        var names = await CollectorNamesAsync(tenant, assignments.Select(x => (Guid?)x.AssignedUserId).Concat(assignments.Select(x => x.TransferredFromUserId)), ct);
        string Staff(string? id) => id is not null && staff.TryGetValue(id, out var n) ? n : "Office";
        string Holder(Guid id) => names.GetValueOrDefault(id, "Collector");

        // One raw fact per unit and event, then consecutive serials of an identical event collapse into one range.
        var facts = new List<(DateTime At, string Event, Guid BookId, long Serial, string Number, string? Actor, string? From, string? To, string? Detail)>();
        foreach (var book in books)
            facts.Add((book.ReceivedAtUtc, "Registered", book.Id, book.FirstSerialNumber, book.FormatNumber(book.FirstSerialNumber),
                Staff(book.ReceivedByActorId), book.SourceAuthority, "Office",
                string.Join(" · ", new[] { book.SourceReference, book.Quantity == 1 ? null : $"Last {book.FormatNumber(book.LastSerialNumber)}" }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        // The registration fact stands for the whole range; per-unit events follow.
        var spoilAt = spoilages.ToDictionary(x => x.AccountableDocumentId, x => x.RecordedAtUtc);
        var lossAt = losses.GroupBy(x => x.AccountableDocumentId).ToDictionary(g => g.Key, g => g.Select(x => x.RecordedAtUtc).ToHashSet());
        var transferIn = assignments.Where(x => x.TransferredFromUserId.HasValue).Select(x => (x.AccountableDocumentId, x.AssignedAtUtc)).ToHashSet();
        foreach (var a in assignments)
        {
            var u = byId[a.AccountableDocumentId];
            if (a.TransferredFromUserId is { } from)
                facts.Add((a.AssignedAtUtc, "Transferred", u.FormBookId, u.SerialNumber, u.DocumentNumber, Staff(a.AssignedByActorId), Holder(from), Holder(a.AssignedUserId), a.TransferReason));
            else
                facts.Add((a.AssignedAtUtc, "Assigned", u.FormBookId, u.SerialNumber, u.DocumentNumber, Staff(a.AssignedByActorId), "Office", Holder(a.AssignedUserId), null));
            if (a.ReturnedAtUtc is { } back)
            {
                var other = transferIn.Contains((u.Id, back)) || (spoilAt.TryGetValue(u.Id, out var sp) && sp == back)
                    || (lossAt.TryGetValue(u.Id, out var set) && set.Contains(back));
                if (!other) facts.Add((back, "Returned", u.FormBookId, u.SerialNumber, u.DocumentNumber, Staff(a.ReturnedByActorId), Holder(a.AssignedUserId), "Office", null));
            }
        }
        foreach (var u in units.Where(x => x.ConsumedAtUtc.HasValue && x.State is AccountableDocumentState.Consumed or AccountableDocumentState.ReconciliationRequired))
            facts.Add((u.ConsumedAtUtc!.Value, u.State == AccountableDocumentState.ReconciliationRequired ? "Issued — needs review" : "Issued",
                u.FormBookId, u.SerialNumber, u.DocumentNumber, null, null, null, null));
        foreach (var s in spoilages)
        {
            var u = byId[s.AccountableDocumentId];
            facts.Add((s.RecordedAtUtc, "Cancelled", u.FormBookId, u.SerialNumber, u.DocumentNumber, s.ActorName, s.CustodianUserId is { } c ? Holder(c) : "Office", null,
                string.IsNullOrWhiteSpace(s.Note) ? s.Reason : $"{s.Reason} — {s.Note}"));
        }
        foreach (var l in losses)
        {
            var u = byId[l.AccountableDocumentId];
            facts.Add((l.RecordedAtUtc, CopiesLabel(l.CopiesLost), u.FormBookId, u.SerialNumber, u.DocumentNumber, l.ActorName,
                l.CustodianUserId is { } c ? Holder(c) : "Office", null, string.Join(" · ", new[] { l.Place, l.Narrative }.Where(x => !string.IsNullOrWhiteSpace(x)))));
        }
        foreach (var r in references)
        {
            var u = byId[r.AccountableDocumentId];
            facts.Add((r.RecordedAtUtc, r.Kind == AccountableFormReferenceKind.Cancellation ? "Cancellation reference added" : "Loss reference added",
                u.FormBookId, u.SerialNumber, u.DocumentNumber, r.ActorName, null, null, r.Reference));
        }

        var events = new List<AccountableFormHistoryEventDto>();
        // The registration rows are already one per range; everything else groups consecutive serials of the same event.
        foreach (var registered in facts.Where(x => x.Event == "Registered"))
        {
            var book = books.First(b => b.Id == registered.BookId);
            events.Add(new(registered.At, "Registered", $"{book.FormatNumber(book.FirstSerialNumber)} – {book.FormatNumber(book.LastSerialNumber)}", book.Quantity,
                registered.Actor, registered.From, "Office", registered.Detail));
        }
        foreach (var group in facts.Where(x => x.Event != "Registered")
                     .GroupBy(x => (x.At, x.Event, x.BookId, x.Actor, x.From, x.To, x.Detail)))
        {
            var ordered = group.OrderBy(x => x.Serial).ToList();
            var i = 0;
            while (i < ordered.Count)
            {
                var j = i;
                while (j + 1 < ordered.Count && ordered[j + 1].Serial == ordered[j].Serial + 1) j++;
                events.Add(new(group.Key.At, group.Key.Event, i == j ? ordered[i].Number : $"{ordered[i].Number} – {ordered[j].Number}",
                    j - i + 1, group.Key.Actor, group.Key.From, group.Key.To, group.Key.Detail));
                i = j + 1;
            }
        }
        return Result<IReadOnlyList<AccountableFormHistoryEventDto>>.Success(
            events.OrderByDescending(x => x.AtUtc).ThenBy(x => x.Serials, StringComparer.Ordinal).Take(take).ToList());
    }, ct);
}
