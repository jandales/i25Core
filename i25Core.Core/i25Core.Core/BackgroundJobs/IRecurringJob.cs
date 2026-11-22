namespace i25Core.Core.BackgroundJobs
{
    /// <summary>
    /// A job that runs on a schedule (Laravel's scheduler / Kernel::schedule).
    /// Implement it, then register it with services.AddRecurringJob&lt;TJob&gt;();
    /// it is scheduled automatically at startup. The class name is the job id.
    /// </summary>
    public interface IRecurringJob
    {
        /// <summary>
        /// Cron expression: 5 fields ("0 2 * * *" = daily at 02:00) or
        /// 6 fields with seconds first ("*/30 * * * * *" = every 30 seconds).
        /// </summary>
        string Cron { get; }

        /// <summary>Time zone for the schedule. Defaults to UTC.</summary>
        TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        /// <summary>Queue to run on. Defaults to "default".</summary>
        string Queue => "default";

        /// <summary>The work. The token is cancelled when the server shuts down.</summary>
        Task ExecuteAsync(CancellationToken cancellationToken);
    }
}
