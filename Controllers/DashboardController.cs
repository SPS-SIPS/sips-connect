using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIPS.Connect.Models;
using SIPS.PostgreSQL.Enums;
using SIPS.PostgreSQL.Interfaces;

namespace SIPS.Connect.Controllers;

[Authorize(Roles = KnownRoles.Dashboard)]
[ApiController]
[Route("api/v1/[controller]")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("UI")]
public sealed class DashboardController(
    IStorageBroker broker,
    IConfiguration configuration) : ControllerBase
{
    private readonly IStorageBroker _broker = broker;
    private readonly IConfiguration _configuration = configuration;

    [HttpGet("TransactionTypeDistribution")]
    public async Task<IActionResult> GetTransactionTypeDistribution([FromQuery] DashboardQueryDto request,
        CancellationToken ct)
    {
        var query = _broker.Transactions
            .AsNoTracking()
            .Include(t => t.ISOMessage) 
            .AsQueryable();

        var (startDate, endDate) = GetDateRange(request.Period);
        query = query.Where(t => t.ISOMessage.Date >= startDate && t.ISOMessage.Date <= endDate);

        var summary = await query
            .GroupBy(t => t.Type)
            .Select(g => new TransactionTypeSummaryDto
            {
                Type = g.Key.ToString(),
                Count = g.Count(),
                TotalAmount = g.Sum(t => t.Amount)
            })
            .ToListAsync(ct);

        return Ok(new TransactionTypeDistributionDto
        {
            TransactionTypeSummary = summary
        });
    }

    [HttpGet("CashFlowOverview")]
    public async Task<IActionResult> GetCashFlowOverview([FromQuery] DashboardQueryDto request, CancellationToken ct)
    {
        var query = _broker.Transactions
            .AsNoTracking()
            .Include(t => t.ISOMessage)
            .AsQueryable();

        var (startDate, endDate) = GetDateRange(request.Period);
        query = query.Where(t => t.ISOMessage.Date >= startDate && t.ISOMessage.Date <= endDate);

        var transactions = await query.ToListAsync(ct);

        var inbound = transactions.Where(t => t.Type == TransactionType.Deposit).ToList();
        var outbound = transactions.Where(t => t.Type == TransactionType.Withdrawal).ToList();

        return Ok(new CashFlowOverviewResponseDto
        {
            CashFlowOverview = new CashFlowOverviewDto
            {
                Inbound = new CashFlowItemDto
                {
                    Type = "Deposit",
                    Count = inbound.Count,
                    TotalAmount = inbound.Sum(t => t.Amount)
                },
                Outbound = new CashFlowItemDto
                {
                    Type = "Withdrawal",
                    Count = outbound.Count,
                    TotalAmount = outbound.Sum(t => t.Amount)
                },
                NetFlow = inbound.Sum(t => t.Amount) - outbound.Sum(t => t.Amount)
            }
        });
    }

    [HttpGet("ReturnExceptionMonitoring")]
    public async Task<IActionResult> GetReturnAndExceptionMonitoring(
        [FromQuery] DashboardQueryDto request,
        CancellationToken ct)
    {
        var query = _broker.Transactions
            .AsNoTracking()
            .Include(t => t.ISOMessage)
            .AsQueryable();

        var (startDate, endDate) = GetDateRange(request.Period);
        query = query.Where(t =>
            t.ISOMessage.Date >= startDate &&
            t.ISOMessage.Date <= endDate);

        var transactions = await query.ToListAsync(ct);

        var readyForReturnCount = transactions.Count(t =>
            t.Type == TransactionType.ReturnDeposit);

        var returnWithdrawalCount = transactions.Count(t =>
            t.Type == TransactionType.ReturnWithdrawal);

        var totalTransactions = transactions.Count;

        var returnRatePercentage = totalTransactions == 0
            ? 0
            : Math.Round(
                (decimal)(readyForReturnCount + returnWithdrawalCount) / totalTransactions * 100,
                2
            );

        return Ok(new ReturnMonitoringDto
        {
            ReadyForReturn = readyForReturnCount,
            ReturnWithdrawal = returnWithdrawalCount,
            ReturnRatePercentage = returnRatePercentage
        });
    }


    [HttpGet("IssuerActivity")]
    public async Task<IActionResult> GetIssuerActivity(
        [FromQuery] DashboardQueryDto request,
        CancellationToken ct)
    {
        var iso20022Section = _configuration.GetSection("ISO20022");
        var ownBankBic = iso20022Section["BIC"];

        var query = _broker.Transactions
            .AsNoTracking()
            .Include(t => t.ISOMessage)
            .AsQueryable();

        var (startDate, endDate) = GetDateRange(request.Period);

        query = query.Where(t =>
            t.ISOMessage.Date >= startDate &&
            t.ISOMessage.Date <= endDate);

        var transactions = await query.ToListAsync(ct);

        // Debtor Issuer Activity → ToBIC (exclude own bank)
        var debtorIssuerActivity = transactions
            .Where(t =>
                t.Type is TransactionType.Withdrawal or TransactionType.ReturnWithdrawal &&
                !string.IsNullOrWhiteSpace(t.ISOMessage?.ToBIC) &&
                !string.Equals(
                    t.ISOMessage.ToBIC,
                    ownBankBic,
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.ISOMessage!.ToBIC)
            .Select(g => new IssuerActivityItemDto
            {
                Issuer = g.Key,
                TransactionCount = g.Count(),
                TotalAmount = g.Sum(t => t.Amount)
            })
            .OrderByDescending(x => x.TransactionCount)
            .ToList();

        // Creditor Issuer Activity → FromBIC (exclude own bank)
        var creditorIssuerActivity = transactions
            .Where(t =>
                t.Type is TransactionType.Deposit or TransactionType.ReturnDeposit &&
                !string.IsNullOrWhiteSpace(t.ISOMessage?.FromBIC) &&
                !string.Equals(
                    t.ISOMessage.FromBIC,
                    ownBankBic,
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.ISOMessage!.FromBIC)
            .Select(g => new IssuerActivityItemDto
            {
                Issuer = g.Key,
                TransactionCount = g.Count(),
                TotalAmount = g.Sum(t => t.Amount)
            })
            .OrderByDescending(x => x.TransactionCount)
            .ToList();

        return Ok(new IssuerActivityResponseDto
        {
            DebtorIssuerActivity = debtorIssuerActivity,
            CreditorIssuerActivity = creditorIssuerActivity
        });
    }

    
    [HttpGet("IsoRequestsSummary")]
    public async Task<IActionResult> GetIsoRequestsSummary([FromQuery] DashboardQueryDto request, CancellationToken ct)
    {
        var query = _broker.ISOMessages.AsNoTracking().AsQueryable();
        
        var (startDate, endDate) = GetDateRange(request.Period);
        query = query.Where(t => t.Date >= startDate && t.Date <= endDate);
        
        var requestTypes = new[]
        {
            ISOMessageType.TransactionRequest,
            ISOMessageType.VerificationRequest,
            ISOMessageType.StatusRequest,
            ISOMessageType.ReturnRequest,
            ISOMessageType.InvalidMessageType
        };

        // Group and count by type
        var groupedCounts = await query
            .Where(t => requestTypes.Contains(t.MessageType))
            .GroupBy(t => t.MessageType)
            .Select(g => new
            {
                Type = g.Key,
                Count = g.Count()
            })
            .ToListAsync(ct);
        
        var summary = requestTypes
            .Select(type => new
            {
                Type = type.ToString(),
                Count = groupedCounts.FirstOrDefault(g => g.Type == type)?.Count ?? 0
            })
            .ToList();

        return Ok(summary);
    }
    
    
    [HttpGet("IsoStatusSummary")]
    public async Task<IActionResult> GetIsoStatusSummary([FromQuery] DashboardQueryDto request, CancellationToken ct)
    {
        var query = _broker.ISOMessages.AsNoTracking().AsQueryable();
        
        var (startDate, endDate) = GetDateRange(request.Period);
        query = query.Where(t => t.Date >= startDate && t.Date <= endDate);
        
        var groupedCounts = await query
            .GroupBy(t => t.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToListAsync(ct);
        
        var allStatuses = new[]
        {
            TransactionStatus.Success,
            TransactionStatus.Failed,
            TransactionStatus.Pending,
            TransactionStatus.ReadyForReturn
        };

        var summary = allStatuses
            .Select(s => new
            {
                status = s.ToString(),
                count = groupedCounts.FirstOrDefault(g => g.Status == s)?.Count ?? 0
            })
            .ToList();

        return Ok(summary);
    }

    [HttpGet("IsoMsgdefSummary")]
    public async Task<IActionResult> GetIsoMsgDefSummary([FromQuery] DashboardQueryDto request, CancellationToken ct)
    {
        var query = _broker.ISOMessages.AsNoTracking().AsQueryable();
        
        var (startDate, endDate) = GetDateRange(request.Period);
        query = query.Where(t => t.Date >= startDate && t.Date <= endDate);
        
        var summary = await query
            .GroupBy(t => t.MsgDefIdr)
            .Select(g => new
            {
                msgDefIdr = g.Key,
                count = g.Count()
            })
            .ToListAsync(ct);

        return Ok(summary);
    }

    private static (DateTime start, DateTime end) GetDateRange(PeriodType period)
    {
        var now = DateTime.UtcNow;
        DateTime start;
        DateTime end;

        switch (period)
        {
            case PeriodType.Today:
                start = now.Date;
                end = start.AddDays(1).AddTicks(-1);
                break;
            case PeriodType.ThisWeek:
                start = now.Date.AddDays(-(int)now.DayOfWeek);
                end = start.AddDays(7).AddTicks(-1);
                break;
            case PeriodType.LastWeek:
                start = now.Date.AddDays(-(int)now.DayOfWeek - 7);
                end = start.AddDays(7).AddTicks(-1);
                break;
            case PeriodType.ThisMonth:
                start = new DateTime(now.Year, now.Month, 1);
                end = start.AddMonths(1).AddTicks(-1);
                break;
            case PeriodType.LastMonth:
                start = new DateTime(now.Year, now.Month, 1).AddMonths(-1);
                end = start.AddMonths(1).AddTicks(-1);
                break;
            case PeriodType.Last3Months:
                start = new DateTime(now.Year, now.Month, 1).AddMonths(-3);
                end = now;
                break;
            case PeriodType.ThisYear:
                start = new DateTime(now.Year, 1, 1);
                end = new DateTime(now.Year, 12, 31, 23, 59, 59);
                break;
            case PeriodType.All:
            default:
                start = DateTime.MinValue;
                end = DateTime.MaxValue;
                break;
        }

        return (start, end);
    }
}