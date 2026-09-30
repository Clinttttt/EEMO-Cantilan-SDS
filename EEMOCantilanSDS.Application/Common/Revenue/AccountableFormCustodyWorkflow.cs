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
public sealed class AccountableFormCustodyWorkflow(
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

    public Task<Result<int>> AssignCashTicketRangeAsync(
        AssignAccountableFormRangeRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        if (request.FormBookId == Guid.Empty || request.AssignedUserId == Guid.Empty
            || request.FirstSerialNumber < 0 || request.LastSerialNumber < request.FirstSerialNumber
            || request.LastSerialNumber - request.FirstSerialNumber >= MaximumUnitsPerRequest)
            return Result<int>.Failure("A valid Cash Ticket book, collector, and bounded serial range are required.", ResultStatus.Invalid);
        var book = await db.AccountableFormBooks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.Id == request.FormBookId, ct);
        if (book is null) return Result<int>.Failure("The accountable-form book was not found in this tenant.", ResultStatus.NotFound);
        if (book.InstrumentType != RevenueInstrumentType.CashTicket
            || request.FirstSerialNumber < book.FirstSerialNumber
            || request.LastSerialNumber > book.LastSerialNumber)
            return Result<int>.Failure("Assignments must stay within one received Cash Ticket book.", ResultStatus.Invalid);
        var collector = await db.CollectorUsers.SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.Id == request.AssignedUserId && x.IsActive, ct);
        if (collector is null) return Result<int>.Failure("An active collector in this tenant is required.", ResultStatus.Invalid);
        var documents = await db.AccountableDocuments.Where(x =>
            x.MunicipalityId == actor.TenantId && x.FormBookId == request.FormBookId
            && x.InstrumentType == RevenueInstrumentType.CashTicket
            && x.SerialNumber >= request.FirstSerialNumber && x.SerialNumber <= request.LastSerialNumber)
            .OrderBy(x => x.SerialNumber).ToListAsync(ct);
        var expected = checked((int)(request.LastSerialNumber - request.FirstSerialNumber + 1));
        if (documents.Count != expected || documents.Any(x => x.State != AccountableDocumentState.InOffice))
            return Result<int>.Failure("Every requested Cash Ticket must exist and remain unused in office custody.", ResultStatus.Conflict);
        var now = UtcNow;
        foreach (var document in documents)
        {
            document.AssignTo(collector.Id, actor.Username);
            db.AccountableFormAssignments.Add(AccountableFormAssignment.Assign(
                document, collector.Id, actor.ActorId, now, actor.Username));
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Result<int>.Failure("The Cash Ticket custody assignment changed concurrently.", ResultStatus.Conflict);
        }
        return Result<int>.Success(documents.Count);
    }, ct);

    private DateTime UtcNow => clock?.UtcNow ?? DateTime.UtcNow;

    private Task<Result<T>> Run<T>(Func<Actor, Task<Result<T>>> action, CancellationToken ct)
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

    private static AccountableFormBookDto ToDto(AccountableFormBook book, IEnumerable<AccountableDocument> documents) =>
        new(book.Id, book.InstrumentType, book.SeriesName, book.NumberPrefix,
            book.FirstSerialNumber, book.LastSerialNumber, documents.Select(x => new CashTicketDocumentDto(
                x.Id, x.DocumentNumber, x.State, x.AssignedUserId, x.SerialNumber)).ToList());

    private sealed record Actor(Guid TenantId, Guid UserId, string Username, string ActorId);
}
