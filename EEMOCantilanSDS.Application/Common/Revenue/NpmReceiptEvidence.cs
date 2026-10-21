using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

internal static class NpmReceiptEvidence
{
    internal static string IntentSuffix(string? number) => string.IsNullOrWhiteSpace(number) ? "" : "|OR:" + AccountableSerial.Normalize(number);

    internal static async Task<Result<AccountableDocument?>> ResolveAsync(IAppDbContext db, ICurrentUserService user,
        Guid tenant, RevenueInstrumentType instrument, string? number, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(number)) return Result<AccountableDocument?>.Success(null);
        if (number.Length > 100) return Result<AccountableDocument?>.Failure("The OR number is too long.", ResultStatus.Invalid);
        if (instrument != RevenueInstrumentType.OfficialReceipt)
            return Result<AccountableDocument?>.Failure("This source uses Cash Ticket policy. An Official Receipt cannot be linked to it.", ResultStatus.Conflict);
        var normalized = AccountableSerial.Normalize(number);
        var document = await db.AccountableDocuments.SingleOrDefaultAsync(x => x.MunicipalityId == tenant
            && x.InstrumentType == RevenueInstrumentType.OfficialReceipt && x.NormalizedNumber == normalized, ct);
        var actor = user.CollectorId ?? user.UserId;
        if (document is null || !(document.State == AccountableDocumentState.Assigned && document.AssignedUserId == actor
            || (user.Role is "Admin" or "SuperAdmin") && document.State == AccountableDocumentState.InOffice))
            return Result<AccountableDocument?>.Failure("The OR must be registered, unused and in your accountable-form custody.", ResultStatus.Conflict);
        return Result<AccountableDocument?>.Success(document);
    }
}
