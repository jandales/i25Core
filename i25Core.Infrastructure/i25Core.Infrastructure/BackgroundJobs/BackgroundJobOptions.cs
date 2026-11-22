namespace i25Core.Infrastructure.BackgroundJobs
{
    /// <summary>Bound from the "BackgroundJobs" section of appsettings.json.</summary>
    public class BackgroundJobOptions
    {
        public const string SectionName = "BackgroundJobs";

        /// <summary>
        /// Run a job-processing server inside this app. Set false on instances that should
        /// only enqueue jobs (jobs are still stored and picked up by another instance).
        /// </summary>
        public bool ServerEnabled { get; set; } = true;

        /// <summary>Show the dashboard. It is only reachable from the local machine.</summary>
        public bool DashboardEnabled { get; set; } = true;

        /// <summary>URL of the dashboard.</summary>
        public string DashboardPath { get; set; } = "/jobs";

        /// <summary>Parallel workers. 0 = Hangfire default (processor count × 5, max 20).</summary>
        public int WorkerCount { get; set; } = 0;

        /// <summary>Queues this server processes, highest priority first.</summary>
        public string[] Queues { get; set; } = { "default" };

        /// <summary>Automatic retries for a failed job before it is marked Failed.</summary>
        public int RetryAttempts { get; set; } = 3;

        /// <summary>How often delayed and recurring jobs are checked, in seconds.</summary>
        public int SchedulePollingIntervalSeconds { get; set; } = 15;

        /// <summary>SQL Server schema for the Hangfire tables.</summary>
        public string SchemaName { get; set; } = "HangFire";
    }
}
