namespace SIPS.Core.Interfaces;

public interface IScheduleConfig<T>
{
    string? CronExpression { get; set; }
    TimeZoneInfo? TimeZoneInfo { get; set; }
}