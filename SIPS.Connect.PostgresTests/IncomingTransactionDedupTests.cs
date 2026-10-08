using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SIPS.Core.Services.Implementations;
using SIPS.Core.Services.Persistence;
using SIPS.ISO20022.Helpers;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Gateway;
using SIPS.PostgreSQL.Interfaces;
using SIPS.PostgreSQL.Models;
using Xunit;

namespace SIPS.Connect.PostgresTests;

public sealed class IncomingTransactionDedupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unresolved_payment_redelivery_loads_existing_record_and_does_not_poison_later_saves(bool sameContext)
    {
        await using var harness = await PostgresHarness.CreateAsync();
        await using var provider = harness.BuildProvider();
        using var firstScope = provider.CreateScope();
        var firstStorage = firstScope.ServiceProvider.GetRequiredService<IStorageBroker>();
        var xml = PostgresHarness.InboundPayment("CT0202610080001244401", "20261008NG1017152624", "E2E-RETRY");
        var request = PaymentRequestBuilder.Parse(xml);
        var original = await Service(firstStorage).TryRecordIncomingTransactionAsync(request, xml, CancellationToken.None);
        Assert.True(original.IsNew);
        var response = PaymentRequestResponseBuilder.Build(new()
        {
            From = request.To, To = request.From, Original = request, Status = "RJCT", Reason = "MS03"
        });
        original.Message!.Status = TransactionStatus.CheckStatus;
        original.Message.Response = Encoding.UTF8.GetBytes(response);
        await firstStorage.SaveChangesAsync(CancellationToken.None);

        using var retryScope = provider.CreateScope();
        var storage = sameContext ? firstStorage : retryScope.ServiceProvider.GetRequiredService<IStorageBroker>();
        for (var retry = 0; retry < 2; retry++)
        {
            var follower = await Service(storage).TryRecordIncomingTransactionAsync(request, xml, CancellationToken.None);
            Assert.False(follower.IsNew);
            Assert.Equal(original.Message.Id, follower.Message!.Id);
            Assert.Equal(TransactionStatus.CheckStatus, follower.Message.Status);
            Assert.Equal(response, Encoding.UTF8.GetString(follower.Message.Response!));
            Assert.DoesNotContain(storage.ChangeTracker.Entries<ISOMessage>(), e => e.State == EntityState.Added);
            Assert.DoesNotContain(storage.ChangeTracker.Entries<Transaction>(), e => e.State == EntityState.Added);

            // The controller continues with the PAPSS operation store on the same context. A later
            // save must not attempt to insert the failed duplicate graph again.
            follower.Message.AdditionalInfo = $"Observed retry {retry}";
            await storage.SaveChangesAsync(CancellationToken.None);
        }
        Assert.Equal(1, await storage.ISOMessages.CountAsync());
        Assert.Equal(1, await storage.Transactions.CountAsync());
    }

    private static ISOMessageService Service(IStorageBroker storage) => new(
        new PersistenceGateway(new IncomingRecorder(NullLogger<IncomingRecorder>.Instance, storage)),
        NullLogger<ISOMessageService>.Instance);
}
