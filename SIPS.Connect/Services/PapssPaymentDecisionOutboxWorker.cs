using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SIPS.Connect.Config;
using SIPS.PostgreSQL.Interfaces;
using SIPS.XMLDsig.Xades.Options;

namespace SIPS.Connect.Services;

public sealed class PapssPaymentDecisionOutboxWorker(
    IServiceScopeFactory scopes,
    PapssFacingOptions options,
    XadesOptions xades,
    ILogger<PapssPaymentDecisionOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Enabled) await PublishPendingAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(error, "PAPSS payment-decision outbox scan failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task PublishPendingAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IStorageBroker>();
        var publisher = (PapssPaymentDecisionPublisher)scope.ServiceProvider.GetRequiredService<IPapssPaymentDecisionPublisher>();
        var binding = new PapssParticipantBinding(xades.BIC.Trim().ToUpperInvariant(), options.LocalCountry.Trim().ToUpperInvariant(), options.SendingCurrencies, options.CallbackMappingProfile, options.CallbackUrl);
        var lastId = 0;
        var processed = 0;
        while (processed < 25)
        {
            var pending = await storage.ISOMessages.AsNoTracking()
                .Where(x => x.Id > lastId && x.BusinessService == options.SecurityProfile && x.Response != null && x.PapssDecisionPublishedAt == null && x.PapssDecisionFailedAt == null)
                .OrderBy(x => x.Id).Take(100).Select(x => new { x.Id, x.Message, x.PapssDecision }).ToArrayAsync(ct);
            if (pending.Length == 0) break;
            lastId = pending[^1].Id;
            foreach (var item in pending)
            {
                var inbound = Encoding.UTF8.GetString(item.Message);
                if (!IsPapssPayment(inbound)) continue;
                processed++;
                try
                {
                    if (item.PapssDecision is null) await publisher.PersistAndSubmitAsync(binding, inbound, ct);
                    else await publisher.TrySubmitAsync(binding, item.Id, item.PapssDecision, ct);
                }
                catch (Exception error) when (error is InvalidDataException or ParticipantRailException or UnauthorizedAccessException)
                {
                    var code = error is ParticipantRailException rail ? rail.Code : "REJECTED_BEFORE_EXTERNAL_EFFECT";
                    await storage.ISOMessages.Where(x => x.Id == item.Id && x.PapssDecisionPublishedAt == null)
                        .ExecuteUpdateAsync(update => update.SetProperty(x => x.PapssDecisionFailureCode, code).SetProperty(x => x.PapssDecisionFailedAt, DateTimeOffset.UtcNow), ct);
                    logger.LogError(error, "PAPSS decision {MessageId} could not be recovered and was terminalized as {FailureCode}", item.Id, code);
                }
                catch (Exception error) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(error, "PAPSS decision {MessageId} retry failed; continuing with the remaining outbox batch", item.Id);
                }
                if (processed == 25) break;
            }
        }
    }

    private bool IsPapssPayment(string xml)
    {
        try
        {
            using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new() { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
            var header = XDocument.Load(reader, LoadOptions.None).Descendants().Single(x => x.Name.LocalName == "AppHdr");
            string? Value(string name) => header.Elements().SingleOrDefault(x => x.Name.LocalName == name)?.Value;
            return Value("MsgDefIdr") == "pacs.008.001.10" && Value("BizSvc") == options.SecurityProfile;
        }
        catch (Exception error)
        {
            logger.LogWarning(error, "Skipping malformed non-recoverable ISO message while scanning the PAPSS outbox");
            return false;
        }
    }
}
