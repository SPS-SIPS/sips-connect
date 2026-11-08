using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SIPS.Connect.Config;
using SIPS.ISO20022.Models.DTOs;
using SIPS.ISO20022.Models.DTOs.CB;
using SIPS.XMLDsig.Xades.Options;

namespace SIPS.Connect.Services;

/// <summary>
/// Asks PAPSS (pacs.028) for the status of received payments we accepted but whose final status never arrived. The answer is
/// a pacs.002 that is stored, correlated and applied like any other (accepted or rejected), so nothing is decided here.
/// </summary>
public sealed class PapssInboundReconciliationWorker(
    IServiceScopeFactory scopes,
    PapssFacingOptions options,
    XadesOptions xades,
    TimeProvider clock,
    ILogger<PapssInboundReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Enabled && options.Status.InboundReconciliationEnabled) await EnquireDueAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(error, "PAPSS inbound reconciliation scan failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    private async Task EnquireDueAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<PapssOperationStore>();
        var payments = scope.ServiceProvider.GetRequiredService<IPapssPaymentService>();
        var now = clock.GetUtcNow();
        var binding = new PapssParticipantBinding(xades.BIC.Trim().ToUpperInvariant(), options.LocalCountry.Trim().ToUpperInvariant(), options.SendingCurrencies, options.CallbackMappingProfile, options.CallbackUrl);
        var due = await store.InboundAwaitingCompletionAsync(
            now.AddMinutes(-options.Status.InboundReconciliationAfterMinutes),
            now.AddMinutes(-options.Status.InboundReconciliationRetryMinutes),
            options.Status.InboundReconciliationMaxAttempts, 10, ct);
        foreach (var payment in due)
        {
            try
            {
                var result = await payments.StatusAsync(binding, new StatusRequestDto { TxId = payment.TxId!, EndToEnd = payment.EndToEndId!, ToBIC = payment.CounterpartyBic! }, ct);
                logger.LogInformation("PAPSS pacs.028 for received payment {TxId}: outcome {Outcome}, enquiry {EnquiryId}, refused {Refusal}",
                    payment.TxId, result.Payment?.PapssOutcome, result.EnquiryRequestMessageId, result.EnquiryError);
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(error, "PAPSS pacs.028 for received payment {TxId} could not be sent; it is retried after the retry interval", payment.TxId);
            }
        }
    }
}
