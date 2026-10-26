using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Mobile.Collections;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class ArrearsWorkspaceHolderTests
{
    private static ArrearsAccountDto Account(ArrearsAccountKind kind) =>
        new("k", kind, new(kind == ArrearsAccountKind.StallRent ? SourceIdentityKind.Occupancy : SourceIdentityKind.SpaceAccount, Guid.NewGuid()),
            "OP", "Operation", "Payer", "1", null, "Active", "arrears", 1000m, 0m, 1000m, 1, "2026-01", "2026-01", 0m, 1000m, null, true, null, null);

    [Fact]
    public void Holds_an_account_for_the_collector_it_was_read_for_only()
    {
        var now = new DateTime(2026, 10, 26, 1, 0, 0, DateTimeKind.Utc);
        var holder = new ArrearsWorkspaceHolder(() => now);
        var mine = Guid.NewGuid(); var account = Account(ArrearsAccountKind.SpaceRental);
        holder.Set(mine, [account]);

        Assert.Same(account, holder.Find(mine, account.Kind, account.Identity.Id));
        Assert.Null(holder.Find(Guid.NewGuid(), account.Kind, account.Identity.Id));       // another sign-in never sees it
        Assert.Null(holder.Find(Guid.Empty, account.Kind, account.Identity.Id));
        Assert.Null(holder.Find(mine, ArrearsAccountKind.StallRent, account.Identity.Id)); // kind is part of the identity
        Assert.Null(holder.Find(mine, account.Kind, Guid.NewGuid()));
    }

    [Fact]
    public void An_old_list_is_not_handed_back_and_Clear_forgets_it()
    {
        var now = new DateTime(2026, 10, 26, 1, 0, 0, DateTimeKind.Utc);
        var holder = new ArrearsWorkspaceHolder(() => now);
        var mine = Guid.NewGuid(); var account = Account(ArrearsAccountKind.StallRent);
        holder.Set(mine, [account]);

        now += ArrearsWorkspaceHolder.Lifetime;                                              // exactly at the limit is still fresh
        Assert.NotNull(holder.Find(mine, account.Kind, account.Identity.Id));
        now += TimeSpan.FromSeconds(1);
        Assert.Null(holder.Find(mine, account.Kind, account.Identity.Id));

        holder.Set(mine, [account]);
        holder.Clear();
        Assert.Null(holder.Find(mine, account.Kind, account.Identity.Id));
    }
}
