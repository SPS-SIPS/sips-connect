using Microsoft.Extensions.DependencyInjection;
using SIPS.Core.Models;
using SIPS.Core.Services;

namespace SIPS.Core.Extensions;
public static class ScheduledServiceExtensions
{
    public static IServiceCollection AddCronJob<T>(this IServiceCollection services, JobConfiguration option) where T : CronJobService
    {
        if (option == null)
        {
            throw new ArgumentNullException(nameof(option), @"Please provide Schedule Configurations.");
        }
        var config = new ScheduleConfig<T>();
        if (string.IsNullOrWhiteSpace(option.Expression))
        {
            throw new ArgumentNullException(nameof(ScheduleConfig<T>.CronExpression), @"Empty Cron Expression is not allowed.");
        }

        config.CronExpression = option.Expression;
        config.TimeZoneInfo = option.TimeZoneInfo == "Utc" ? TimeZoneInfo.Utc : TimeZoneInfo.Local;

        services.AddSingleton<IScheduleConfig<T>>(config);
        services.AddHostedService<T>();
        return services;
    }
}
