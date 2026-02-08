using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SIPS.Core.Options;
using SIPS.Core.Services;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;

namespace SIPS.Core.Workers;

public class TimeoutWorker(IScheduleConfig<TimeoutWorker> config, ILogger<TimeoutWorker> logger, IServiceProvider services) : CronJobService(config.CronExpression!, config.TimeZoneInfo!)
{
    private readonly ILogger<TimeoutWorker> _logger = logger;
    private readonly IServiceProvider _services = services;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Timeout Worker Started, next occurrence will be at: {timeStamp} Utc", GetSchedule);
        return base.StartAsync(cancellationToken);
    }

    public override async Task DoWork(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Timeout Worker is working...");

        await using var scope = _services.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageBroker>() ?? throw new ArgumentNullException(nameof(IStorageBroker));
        var options = scope.ServiceProvider.GetRequiredService<CoreOptions>() ?? throw new ArgumentNullException(nameof(CoreOptions));

        var timeoutThreshold = DateTime.UtcNow.AddMinutes(-options.TransactionTimeoutMinutes);

        // Find transactions stuck in Pending state longer than the timeout
        var stuckTransactions = await storage.ISOMessages
            .Where(x => x.Status == TransactionStatus.Pending && x.Date <= timeoutThreshold)
            .ToListAsync(cancellationToken);

        if (stuckTransactions.Count != 0)
        {
            _logger.LogInformation("Timeout Worker found {count} stuck transactions. Moving to CheckStatus.", stuckTransactions.Count);

            foreach (var transaction in stuckTransactions)
            {
                _logger.LogWarning("Timeout Worker: Transaction {txId} timed out in Pending state. Moving to CheckStatus.", transaction.TxId);
                transaction.Status = TransactionStatus.CheckStatus;
                transaction.Round = 0; // Ensure it starts fresh for SAF processing
            }

            await storage.SaveChangesAsync(cancellationToken);
        }
        else
        {
            _logger.LogInformation("Timeout Worker found no stuck transactions.");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Timeout Worker stopped...");
        return base.StopAsync(cancellationToken);
    }
}
