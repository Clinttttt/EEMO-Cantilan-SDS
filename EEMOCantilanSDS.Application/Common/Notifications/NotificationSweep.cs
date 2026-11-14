using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Entities.Notifications;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Notifications;

/// <summary>
/// Turns what the owning records already say into inbox messages (IA-074). It reads payments, charges and balances and writes only
/// messages, so no payment or penalty path depends on it and a failed pass costs nothing but a later message. Each fact has one key per
/// person, so running often is harmless and an app that slept catches up on its next pass.
/// </summary>
public sealed class NotificationSweep(IAppDbContext db, IPayorRepository payors, BillingPolicyWorkflow policy, ICurrentMunicipalityAccessor municipality, IClock clock)
{
    // shortcut: a fixed week, so a first run does not announce years of old payments and a pause longer than this loses those messages. Keep a per-office high-water mark if that ever matters.
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(7);
    private const int DueSoonDays = 3;

    private readonly record struct Message(InboxRecipientKind Kind, Guid Recipient, string Key, string Title, string Body, string? Link);

    private Guid Tenant => municipality.MunicipalityId;

    /// <summary>Exceptions and penalties for the office, status changes for payors. Light enough to run every few minutes.</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var messages = new List<Message>();
        await AddStaffMessagesAsync(messages, ct);
        await AddPaymentMessagesAsync(messages, ct);
        await AddPenaltyMessagesAsync(messages, ct);
        return await SaveNewAsync(messages, ct);
    }

    /// <summary>Rent coming due and rent past due, from the same figures the payor's own Balances screen shows.</summary>
    // shortcut: one balance read per payor, which is why the service asks for this once a day. Read balances in bulk if an office ever has thousands of linked payors.
    public async Task<int> RemindAsync(CancellationToken ct = default)
    {
        var rules = await policy.CurrentAsync(ct);
        var today = clock.PhilippineToday;
        var linked = db.PayorStallLinks.AsNoTracking().Where(l => l.MunicipalityId == Tenant).Select(l => l.PayorUserId);
        var recipients = await db.PayorUsers.AsNoTracking().Where(p => p.IsActive && linked.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);

        var messages = new List<Message>();
        foreach (var payor in recipients)
        {
            var rent = (await payors.GetPayableItemsAsync(payor, ct))
                .Where(i => i.Kind == PayorPayableKind.Monthly && i.DueDate is not null && !i.Advance && !i.PaymentUnderReview && i.BalanceDue > 0m)
                .ToList();

            foreach (var item in rent.Where(i => today >= i.DueDate!.Value.AddDays(-DueSoonDays) && today <= i.DueDate!.Value))
                messages.Add(new(InboxRecipientKind.Payor, payor, $"rent-due:{item.StallId:N}:{item.Year:0000}-{item.Month:00}", "Rent coming due",
                    $"Rent for {item.StallNo}, {MonthName(item.Year, item.Month)}, is due on {Day(item.DueDate!.Value)}: {Peso(item.BalanceDue)}.", "/balances"));

            foreach (var stall in rent.Where(i => today >= rules.FirstLateDay(i.Year, i.Month)).GroupBy(i => i.StallId))
            {
                var oldest = stall.OrderBy(i => i.Year).ThenBy(i => i.Month).First();
                var months = stall.Count();
                var body = months == 1
                    ? $"Rent for {oldest.StallNo}, {MonthName(oldest.Year, oldest.Month)}, is past due: {Peso(oldest.BalanceDue)}."
                    : $"{oldest.StallNo} has {months} months of rent past due, {Peso(stall.Sum(i => i.BalanceDue))} in total. The oldest is {MonthName(oldest.Year, oldest.Month)}.";
                messages.Add(new(InboxRecipientKind.Payor, payor, $"rent-overdue:{stall.Key:N}:{today.Year:0000}-{today.Month:00}", "Rent past due", body, "/balances"));
            }
        }
        return await SaveNewAsync(messages, ct);
    }

    private async Task AddStaffMessagesAsync(List<Message> messages, CancellationToken ct)
    {
        var staff = await db.AdminUsers.AsNoTracking()
            .Where(a => a.MunicipalityId == Tenant && a.IsActive && !a.IsPlatformOperator && (a.Role == AdminRole.SuperAdmin || a.Role == AdminRole.Admin))
            .Select(a => a.Id).ToListAsync(ct);
        if (staff.Count == 0) return;

        var exceptions = await db.OnlinePaymentTransactions.AsNoTracking()
            .Where(t => t.MunicipalityId == Tenant && t.Status == OnlinePaymentStatus.ReconciliationRequired && t.Disposition == null)
            .Select(t => new { t.Id, t.Reference, Amount = t.CapturedAmount ?? t.Amount })
            .ToListAsync(ct);
        foreach (var person in staff)
        foreach (var payment in exceptions)
            messages.Add(new(InboxRecipientKind.Staff, person, $"online-capture:{payment.Id:N}", "Online payment needs review",
                $"The gateway reported {Peso(payment.Amount)} on {payment.Reference} and it could not be posted on its own.", "/online-payments"));

        var today = clock.PhilippineToday;
        var monthStartUtc = clock.UtcNow - (clock.PhilippineNow - new DateTime(today.Year, today.Month, 1));
        var charges = await db.FineAssessments.AsNoTracking()
            .CountAsync(a => a.MunicipalityId == Tenant && a.PenaltyCode == FineAssessment.LatePaymentCode && a.RecordedAtUtc >= monthStartUtc, ct);
        if (charges == 0) return;
        foreach (var person in staff)
            messages.Add(new(InboxRecipientKind.Staff, person, $"penalties:{today.Year:0000}-{today.Month:00}", "Late penalties added",
                $"{charges} automatic late {(charges == 1 ? "penalty has" : "penalties have")} been added so far this month. You can review them in Fines.", "/operations/fines"));
    }

    private async Task AddPaymentMessagesAsync(List<Message> messages, CancellationToken ct)
    {
        var since = clock.UtcNow - Lookback;
        var payments = await db.OnlinePaymentTransactions.AsNoTracking()
            .Where(t => t.MunicipalityId == Tenant && (t.UpdatedAt ?? t.CreatedAt) >= since)
            .Select(t => new
            {
                t.Id, t.PayorUserId, t.Reference, t.Status, t.Disposition, t.DispositionReference, t.ORNumber, t.GatewayReference,
                Amount = t.CapturedAmount ?? t.Amount,
                Stall = t.TargetStallId ?? (Guid?)t.PaymentRecord!.StallId,
                Year = t.TargetYear ?? (int?)t.PaymentRecord!.BillingYear,
                Month = t.TargetMonth ?? (int?)t.PaymentRecord!.BillingMonth,
            })
            .ToListAsync(ct);
        if (payments.Count == 0) return;

        var stallIds = payments.Where(p => p.Stall is not null).Select(p => p.Stall!.Value).Distinct().ToList();
        var stalls = await db.Stalls.AsNoTracking().Where(s => stallIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.StallNo, ct);

        foreach (var p in payments)
        {
            var space = p.Stall is { } id && stalls.TryGetValue(id, out var no) ? no : null;
            var what = space is not null && p.Year is { } y && p.Month is { } m ? $"{space}, {MonthName(y, m)}" : "your account";
            void Say(string kind, string title, string body, string link) =>
                messages.Add(new(InboxRecipientKind.Payor, p.PayorUserId, $"{kind}:{p.Id:N}", title, body, link));

            if (p.Status is OnlinePaymentStatus.Paid or OnlinePaymentStatus.Completed)
                Say("payment-received", "Payment received", $"We received {Peso(p.Amount)} for {what}. Reference {p.Reference}.", "/history");
            if (p.Status == OnlinePaymentStatus.Completed && !string.IsNullOrWhiteSpace(p.ORNumber))
                Say("or-issued", "Official receipt issued", $"OR {p.ORNumber} was issued for your {Peso(p.Amount)} payment for {what}.", "/history");

            if (p.Status == OnlinePaymentStatus.ReconciliationRequired)
            {
                if (p.Disposition is null)
                    Say("payment-review", "Payment under review", $"The gateway reported {Peso(p.Amount)} for {what} and the office is reviewing it. Please do not pay this month again. Reference {p.Reference}.", "/balances");
                else if (p.Disposition == OnlinePaymentDisposition.Applied)
                    // Not the checkout's month: held money may have paid older months first (IA-082), leaving that month unpaid.
                    Say("payment-decided", "Payment review finished", $"The office applied your {Peso(p.Amount)} payment{(space is null ? "" : $" for {space}")} under receipt {p.DispositionReference}. Your balances show what is still owed.", "/balances");
                else if (p.Disposition == OnlinePaymentDisposition.Refunded)
                    Say("payment-decided", "Payment review finished", $"The office recorded a refund of your {Peso(p.Amount)} payment for {what}. Refund reference {p.DispositionReference}.", "/history");
            }

            // Only a checkout the gateway actually opened. Neither message may say nothing was charged: a late capture can still arrive, and the office reviews it if so.
            if (p.GatewayReference is null) continue;
            if (p.Status == OnlinePaymentStatus.Failed)
                Say("payment-failed", "Payment failed", $"The gateway reported that your payment for {what} failed. If money left your account, contact the office with reference {p.Reference}.", "/balances");
            if (p.Status == OnlinePaymentStatus.Expired)
                Say("payment-expired", "Checkout closed", $"Your checkout for {what} closed without a confirmed payment. If money left your account, contact the office with reference {p.Reference}.", "/balances");
        }
    }

    private async Task AddPenaltyMessagesAsync(List<Message> messages, CancellationToken ct)
    {
        var since = clock.UtcNow - Lookback;
        // The current occupant only: a link to a space must not make someone answer for the earlier occupant's charge.
        var charges = await (
            from a in db.FineAssessments.AsNoTracking()
            join c in db.Contracts.AsNoTracking() on a.ContractId equals (Guid?)c.Id
            where a.MunicipalityId == Tenant && a.PenaltyCode == FineAssessment.LatePaymentCode && a.RecordedAtUtc >= since && c.IsActive
            join l in db.PayorStallLinks.AsNoTracking() on c.StallId equals l.StallId
            select new { a.Id, l.PayorUserId, a.UnitReference, a.RelatedPeriodLabel, Amount = a.AdjustedAmount ?? a.AssessedAmount })
            .ToListAsync(ct);
        foreach (var charge in charges.Where(c => c.Amount > 0m))
            messages.Add(new(InboxRecipientKind.Payor, charge.PayorUserId, $"penalty:{charge.Id:N}", "Late penalty added",
                $"A late penalty of {Peso(charge.Amount)} was added to {charge.UnitReference} for {charge.RelatedPeriodLabel} rent. Penalties are paid at the office.", "/balances"));
    }

    private async Task<int> SaveNewAsync(List<Message> messages, CancellationToken ct)
    {
        if (messages.Count == 0) return 0;
        var keys = messages.Select(m => m.Key).Distinct().ToList();
        var sent = (await db.InboxNotifications.AsNoTracking()
            .Where(n => n.MunicipalityId == Tenant && keys.Contains(n.DedupeKey))
            .Select(n => new { n.RecipientKind, n.RecipientId, n.DedupeKey })
            .ToListAsync(ct))
            .Select(n => (n.RecipientKind, n.RecipientId, n.DedupeKey))
            .ToHashSet();

        var fresh = messages.Where(m => sent.Add((m.Kind, m.Recipient, m.Key))).ToList();
        if (fresh.Count == 0) return 0;
        db.InboxNotifications.AddRange(fresh.Select(m => InboxNotification.Create(Tenant, m.Kind, m.Recipient, m.Key, m.Title, m.Body, m.Link, clock.UtcNow)));
        await db.SaveChangesAsync(ct);
        return fresh.Count;
    }

    private static string Peso(decimal amount) => string.Create(CultureInfo.InvariantCulture, $"₱{amount:N2}");

    private static string MonthName(int year, int month) => new DateOnly(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    private static string Day(DateOnly date) => date.ToString("MMMM d", CultureInfo.InvariantCulture);
}
