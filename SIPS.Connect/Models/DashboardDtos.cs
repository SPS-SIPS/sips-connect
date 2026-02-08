namespace SIPS.Connect.Models;

public record TransactionTypeSummaryDto
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
}

public class TransactionTypeDistributionDto
{
    public List<TransactionTypeSummaryDto> TransactionTypeSummary { get; set; } = new();
}

public class CashFlowItemDto
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
}

public class CashFlowOverviewDto
{
    public CashFlowItemDto Inbound { get; set; } = new();
    public CashFlowItemDto Outbound { get; set; } = new();
    public decimal NetFlow { get; set; }
}

public class CashFlowOverviewResponseDto
{
    public CashFlowOverviewDto CashFlowOverview { get; set; } = new();
}

public class ReturnMonitoringDto
{
    public int ReadyForReturn { get; set; }
    public int ReturnWithdrawal { get; set; }
    public decimal ReturnRatePercentage { get; set; }
}

public sealed class IssuerActivityResponseDto
{
    public List<IssuerActivityItemDto> DebtorIssuerActivity { get; set; } = [];
    public List<IssuerActivityItemDto> CreditorIssuerActivity { get; set; } = [];
}

public sealed class IssuerActivityItemDto
{
    public string Issuer { get; set; } = string.Empty;
    public int TransactionCount { get; set; }
    public decimal? TotalAmount { get; set; }
}

public enum PeriodType
{
    All = 0,
    Today = 1,
    ThisWeek = 2,
    LastWeek = 3,
    ThisMonth = 4,
    LastMonth = 5,
    Last3Months = 6,
    ThisYear = 7
}

public class DashboardQueryDto
{
    public PeriodType Period { get; set; } = PeriodType.All;
}