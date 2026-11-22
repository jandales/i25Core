using System.Linq.Expressions;

namespace i25Core.Core.BackgroundJobs
{
    /// <summary>
    /// Runs work outside the HTTP request (Laravel's dispatch / queues).
    /// Jobs are stored in SQL Server, survive restarts and are retried on failure.
    /// The expression is serialized, so pass only simple, serializable arguments (ids, strings),
    /// not entities or DbContexts. The job class is resolved from DI when it runs.
    /// </summary>
    public interface IBackgroundJobService
    {
        /// <summary>Run as soon as a worker is free. Returns the job id. (dispatch(new Job))</summary>
        string Enqueue<TJob>(Expression<Func<TJob, Task>> job, string? queue = null);

        /// <summary>Run once after a delay. Returns the job id. (dispatch(...)->delay(...))</summary>
        string Schedule<TJob>(Expression<Func<TJob, Task>> job, TimeSpan delay, string? queue = null);

        /// <summary>Run once at a specific time. Returns the job id.</summary>
        string Schedule<TJob>(Expression<Func<TJob, Task>> job, DateTimeOffset runAt, string? queue = null);

        /// <summary>Run after another job finishes successfully. Returns the job id.</summary>
        string ContinueWith<TJob>(string parentJobId, Expression<Func<TJob, Task>> job, string? queue = null);

        /// <summary>Create or update a recurring job on a cron schedule (UTC unless a time zone is given).</summary>
        void AddOrUpdateRecurring<TJob>(string recurringJobId, Expression<Func<TJob, Task>> job, string cron, TimeZoneInfo? timeZone = null);

        /// <summary>Remove a recurring job. Does nothing if it doesn't exist.</summary>
        void RemoveRecurring(string recurringJobId);

        /// <summary>Run a recurring job immediately, outside its schedule.</summary>
        void TriggerRecurring(string recurringJobId);

        /// <summary>Cancel a queued or scheduled job. Returns false if it already finished or doesn't exist.</summary>
        bool Delete(string jobId);
    }
}
