using System;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories
{
    /// <summary>
    /// Regression guard for the anonymous PayMongo webhook (multi-LGU): the webhook resolves to the DEFAULT
    /// tenant, so the gateway-reference lookup MUST bypass the tenant filter — otherwise a non-default LGU's
    /// online payment can never be found and is silently never settled.
    /// </summary>
    public class OnlinePaymentRepositoryTenantTests
    {
        private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
        {
            public Guid MunicipalityId => id;
            public void Set(Guid municipalityId) { }
        }

        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        [Fact]
        public async Task GetByGatewayReference_FindsTransactionScopedToAnotherTenant()
        {
            var options = Options();
            var otherLgu = Guid.NewGuid();
            var defaultLgu = Guid.NewGuid();
            const string gatewayRef = "cs_test_multitenant";

            // Seed a transaction owned by a NON-default LGU.
            using (var seed = new AppDbContext(options, new FixedMunicipality(otherLgu)))
            {
                var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, 2400m);
                seed.PaymentRecords.Add(record);
                var txn = OnlinePaymentTransaction.Create("EEMO-OP-20260613-ABCD1234", Guid.NewGuid(), record.Id, 2400m, "PayMongo");
                txn.SetPending(gatewayRef, "https://checkout.test/cs");
                seed.OnlinePaymentTransactions.Add(txn);
                await seed.SaveChangesAsync();
            }

            // Under the DEFAULT tenant context (what the anonymous webhook resolves to):
            using var ctx = new AppDbContext(options, new FixedMunicipality(defaultLgu));

            // Control: the tenant filter WOULD hide it (a plain, filtered query returns nothing).
            var filtered = await ctx.OnlinePaymentTransactions
                .FirstOrDefaultAsync(t => t.GatewayReference == gatewayRef);
            Assert.Null(filtered);

            // The repository lookup bypasses the filter, so the webhook can still find + settle it.
            var found = await new OnlinePaymentRepository(ctx).GetByGatewayReferenceAsync(gatewayRef, CancellationToken.None);
            Assert.NotNull(found);
            Assert.Equal(gatewayRef, found!.GatewayReference);
        }
    }
}
