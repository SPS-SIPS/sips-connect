using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.Core.Services.Abstractions;
using SIPS.ISO20022.Interfaces;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;
namespace SIPS.Core.Workers;

public class SAFWorker(IScheduleConfig<SAFWorker> config, ILogger<SAFWorker> logger, IServiceProvider services) : CronJobService(config.CronExpression!, config.TimeZoneInfo!)
{
    private readonly ILogger<SAFWorker> _logger = logger;
    private readonly IServiceProvider _services = services;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SAF Job Started, next occurrence will be at: {timeStamp} Utc", GetSchedule);
        return base.StartAsync(cancellationToken);
    }

    public override async Task DoWork(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SAF Job is working...");

        await using var scope = _services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageBroker>() ?? throw new ArgumentNullException(nameof(IStorageBroker));
        var options = scope.ServiceProvider.GetRequiredService<CoreOptions>() ?? throw new ArgumentNullException(nameof(CoreOptions));
        var outgoing = scope.ServiceProvider.GetRequiredService<IOutgoingTransactionStatusHandler>() ?? throw new ArgumentNullException(nameof(IOutgoingTransactionStatusHandler));
        var isoService = scope.ServiceProvider.GetRequiredService<IISOMessageService>() ?? throw new ArgumentNullException(nameof(IISOMessageService));

        var bic = options.BIC;

        // SAF only processes messages with CheckStatus that haven't exceeded max retries
        // Exclude terminal statuses (Success, Failed, ReadyForReturn) to prevent reprocessing completed transactions
        var query = storage.ISOMessages
            .Where(x
                => x.Status == TransactionStatus.CheckStatus &&
                    x.Round < options.SAFMaxRetries &&
                    x.FromBIC == bic
                )
            .AsQueryable();

        var count = await query.CountAsync(cancellationToken);
        var pages = (int)Math.Ceiling((decimal)count / options.SAFPage);

        _logger.LogInformation("SAF Job found {count} transactions to process in {pages} pages...", count, pages);

        for (var i = 0; i < pages; i++)
        {
            var skip = i * options.SAFPage;

            var transactions = await query.Include(x => x.Transactions).OrderByDescending(x => x.Date)
                .Skip(skip)
                .Take(options.SAFPage)
                .ToListAsync(cancellationToken);

            foreach (var transaction in transactions)
            {
                // Double-check status before processing (prevent race conditions)
                // If status changed to terminal state between query and processing, skip it
                if (transaction.Status == TransactionStatus.Success ||
                    transaction.Status == TransactionStatus.Failed ||
                    transaction.Status == TransactionStatus.ReadyForReturn)
                {
                    _logger.LogWarning("SAF Job: Transaction {txId} has terminal status {status}. Skipping SAF processing.",
                        transaction.TxId, transaction.Status);
                    continue;
                }

                // Check if this transaction has exceeded max retries
                if (transaction.Round >= options.SAFMaxRetries)
                {
                    _logger.LogWarning("SAF Job: Transaction {txId} exceeded max retries ({maxRetries}). Finalizing as Failed.",
                        transaction.TxId, options.SAFMaxRetries);
                    await isoService.FinalizeAfterMaxRetriesAsync(
                        transaction,
                        "No response from IPS after max SAF retries",
                        cancellationToken);
                    continue;
                }

                _logger.LogInformation("SAF Job: Processing transaction {txId} with status {status}, round {round}...",
                    transaction.TxId, transaction.Status, transaction.Round);

                // Send pacs.028 status request to IPS
                var response = await outgoing.HandleAsync(new ISO20022.Models.DTOs.CB.StatusRequestDto
                {
                    TxId = transaction.TxId!
                }, cancellationToken);

                _logger.LogInformation("SAF Job: Completed processing transaction {txId} with response status {status}, round {round}...",
                    transaction.TxId, response.Data?.Status, transaction.Round);
            }
            await storage.SaveChangesAsync(cancellationToken);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"SAF Job stopped...");
        return base.StopAsync(cancellationToken);
    }
}