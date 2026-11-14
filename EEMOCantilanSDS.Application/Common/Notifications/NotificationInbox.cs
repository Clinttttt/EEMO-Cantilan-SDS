using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Notifications;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Notifications;

/// <summary>A person's own messages. The recipient always comes from the signed-in caller, so nobody can read or clear another person's.</summary>
public sealed class NotificationInbox(IAppDbContext db, ICurrentUserService user, ICurrentMunicipalityAccessor municipality, IClock clock)
{
    private const int PageSize = 50;

    private Guid Tenant => municipality.MunicipalityId;

    private Result<InboxPageDto>? Guard(out InboxRecipientKind kind, out Guid recipient)
    {
        kind = default;
        recipient = user.UserId ?? Guid.Empty;
        if (!user.IsAuthenticated || recipient == Guid.Empty) return Result<InboxPageDto>.Unauthorized();
        if (Tenant == Guid.Empty || user.MunicipalityId != Tenant) return Result<InboxPageDto>.Forbidden();
        switch (user.Role)
        {
            case "Admin" or "SuperAdmin": kind = InboxRecipientKind.Staff; return null;
            case "Payor": kind = InboxRecipientKind.Payor; return null;
            default: return Result<InboxPageDto>.Forbidden();
        }
    }

    private IQueryable<InboxNotification> Mine(InboxRecipientKind kind, Guid recipient) =>
        db.InboxNotifications.Where(n => n.MunicipalityId == Tenant && n.RecipientKind == kind && n.RecipientId == recipient);

    public async Task<Result<InboxPageDto>> ReadMineAsync(CancellationToken ct = default)
    {
        if (Guard(out var kind, out var recipient) is { } denied) return denied;
        return Result<InboxPageDto>.Success(await PageAsync(kind, recipient, ct));
    }

    /// <summary>Marks one message read, or all of them when no id is given. An id that is not the caller's own is answered NotFound.</summary>
    public async Task<Result<InboxPageDto>> MarkReadAsync(Guid? id, CancellationToken ct = default)
    {
        if (Guard(out var kind, out var recipient) is { } denied) return denied;
        var unread = Mine(kind, recipient).Where(n => n.ReadAt == null);
        if (id is { } one)
        {
            if (!await Mine(kind, recipient).AnyAsync(n => n.Id == one, ct))
                return Result<InboxPageDto>.Failure("That message was not found.", ResultStatus.NotFound);
            unread = unread.Where(n => n.Id == one);
        }

        foreach (var message in await unread.ToListAsync(ct)) message.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result<InboxPageDto>.Success(await PageAsync(kind, recipient, ct));
    }

    private async Task<InboxPageDto> PageAsync(InboxRecipientKind kind, Guid recipient, CancellationToken ct)
    {
        var items = await Mine(kind, recipient).AsNoTracking()
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id).Take(PageSize)
            .Select(n => new InboxItemDto(n.Id, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(ct);
        return new InboxPageDto(items, await Mine(kind, recipient).CountAsync(n => n.ReadAt == null, ct));
    }
}
