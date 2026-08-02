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

        // Process unresolved status checks and only the ReadyForReturn records that
        // represent an actual return awaiting confirmation. ReadyForReturn payments
        // without a ReturnId are terminal for SAF and require operator action.
        var query = storage.ISOMessages
            .Where(x
                => (x.Status == TransactionStatus.CheckStatus && (x.FromBIC == bic || x.ToBIC == bic)) ||
                   (x.Status == TransactionStatus.ReadyForReturn && x.ReturnId != null && x.ReturnId != "")
                )
            .AsQueryable();

        // Snapshot IDs before processing. Paging a query whose rows leave the result
        // set as their status changes would otherwise skip later pages.
        var candidateIds = await query
            .OrderByDescending(x => x.Date)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var count = candidateIds.Count;
        var pages = (int)Math.Ceiling((decimal)count / options.SAFPage);

        _logger.LogInformation("SAF Job found {count} transactions to process in {pages} pages...", count, pages);

        for (var i = 0; i < pages; i++)
        {
            var skip = i * options.SAFPage;
            var pageIds = candidateIds.Skip(skip).Take(options.SAFPage).ToList();

            var transactions = await storage.ISOMessages
                .Where(x => pageIds.Contains(x.Id))
                .Include(x => x.Transactions)
                .Include(x => x.Statuses)
                .OrderByDescending(x => x.Date)
                .ToListAsync(cancellationToken);

            foreach (var transaction in transactions)
            {
                // Double-check status before processing (prevent race conditions)
                // If status changed to terminal state between query and processing, skip it
                if (transaction.Status == TransactionStatus.Success ||
                    transaction.Status == TransactionStatus.Failed)
                {
                    _logger.LogWarning("SAF Job: Transaction {txId} has terminal status {status}. Skipping SAF processing.",
                        transaction.TxId, transaction.Status);
                    continue;
                }

                // Check if this transaction has exceeded max retries
                if (transaction.Round >= options.SAFMaxRetries)
                {
                    if (transaction.Status == TransactionStatus.CheckStatus)
                    {
                        _logger.LogWarning("SAF Job: Transaction {txId} exceeded max retries ({maxRetries}). Finalizing as Failed.",
                            transaction.TxId, options.SAFMaxRetries);
                        await isoService.FinalizeAfterMaxRetriesAsync(
                            transaction,
                            "No response from IPS after max SAF retries",
                            cancellationToken);
                    }
                    else
                    {
                        _logger.LogWarning("SAF Job: Return {returnId} for transaction {txId} exhausted status retries. Leaving ReadyForReturn for manual intervention.",
                            transaction.ReturnId, transaction.TxId);
                    }
                    continue;
                }

                // Exponential backoff check
                // Calculate delay: 2^round minutes, max 60 minutes
                var delayMinutes = Math.Min(60, Math.Pow(2, transaction.Round));
                
                // Get the last attempt time from the latest Status entry, or fallback to the transaction's creation Date
                var lastAttemptTime = transaction.Statuses?.OrderByDescending(s => s.Date).FirstOrDefault()?.Date ?? transaction.Date;

                if (DateTimeOffset.UtcNow < lastAttemptTime.AddMinutes(delayMinutes))
                {
                    _logger.LogInformation("SAF Job: Skipping transaction {txId} (Round {round}) due to exponential backoff. Last attempt: {lastAttemptTime:u}, Next attempt after: {nextAttemptTime:u}.",
                        transaction.TxId, transaction.Round, lastAttemptTime, lastAttemptTime.AddMinutes(delayMinutes));
                    continue;
                }

                var txId = transaction.TxId;
                if (string.IsNullOrWhiteSpace(txId))
                {
                    txId = transaction.Transactions?
                        .Select(x => x.TxId)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                    if (!string.IsNullOrWhiteSpace(txId))
                    {
                        _logger.LogWarning("SAF Job: Transaction {messageId} has null parent TxId. Recovered TxId {txId} from child transaction.",
                            transaction.Id, txId);
                        transaction.TxId = txId;
                    }
                }

                if (string.IsNullOrWhiteSpace(txId))
                {
                    _logger.LogError("SAF Job: Transaction message {messageId} cannot be processed because TxId is missing. Marking as Failed to stop SAF retry churn.",
                        transaction.Id);
                    transaction.Status = TransactionStatus.Failed;
                    transaction.Reason = "Missing TxId";
                    transaction.AdditionalInfo = "SAF cannot query IPS status without a transaction id.";
                    await storage.SaveChangesAsync(cancellationToken);
                    continue;
                }

                _logger.LogInformation("SAF Job: Processing transaction {txId} with status {status}, round {round}...",
                    txId, transaction.Status, transaction.Round);

                // Send pacs.028 status request to IPS
                var response = await outgoing.HandleAsync(new ISO20022.Models.DTOs.CB.StatusRequestDto
                {
                    TxId = txId
                }, cancellationToken);

                _logger.LogInformation("SAF Job: Completed processing transaction {txId} with response status {status}, round {round}...",
                    txId, response.Data?.Status, transaction.Round);
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
