using System.Linq.Expressions;
using Hangfire;
using Hangfire.States;
using i25Core.Core.BackgroundJobs;

namespace i25Core.Infrastructure.BackgroundJobs
{
    /// <summary>IBackgroundJobService implemented with Hangfire.</summary>
    public class HangfireBackgroundJobService : IBackgroundJobService
    {
        private readonly IBackgroundJobClient _jobs;
        private readonly IRecurringJobManager _recurring;

        public HangfireBackgroundJobService(IBackgroundJobClient jobs, IRecurringJobManager recurring)
        {
            _jobs = jobs;
            _recurring = recurring;
        }

        public string Enqueue<TJob>(Expression<Func<TJob, Task>> job, string? queue = null)
        {
            return _jobs.Create(job, new EnqueuedState(QueueName(queue)));
        }

        public string Schedule<TJob>(Expression<Func<TJob, Task>> job, TimeSpan delay, string? queue = null)
        {
            return queue == null
                ? _jobs.Schedule(job, delay)
                : _jobs.Schedule(QueueName(queue), job, delay);
        }

        public string Schedule<TJob>(Expression<Func<TJob, Task>> job, DateTimeOffset runAt, string? queue = null)
        {
            return queue == null
                ? _jobs.Schedule(job, runAt)
                : _jobs.Schedule(QueueName(queue), job, runAt);
        }

        public string ContinueWith<TJob>(string parentJobId, Expression<Func<TJob, Task>> job, string? queue = null)
        {
            return _jobs.ContinueJobWith(parentJobId, job, new EnqueuedState(QueueName(queue)));
        }

        public void AddOrUpdateRecurring<TJob>(string recurringJobId, Expression<Func<TJob, Task>> job, string cron, TimeZoneInfo? timeZone = null)
        {
            _recurring.AddOrUpdate(recurringJobId, job, cron, new RecurringJobOptions
            {
                TimeZone = timeZone ?? TimeZoneInfo.Utc
            });
        }

        public void RemoveRecurring(string recurringJobId) => _recurring.RemoveIfExists(recurringJobId);

        public void TriggerRecurring(string recurringJobId) => _recurring.Trigger(recurringJobId);

        public bool Delete(string jobId) => _jobs.Delete(jobId);

        // Hangfire queue names must be lowercase letters, digits, '_' or '-'.
        private static string QueueName(string? queue) =>
            string.IsNullOrWhiteSpace(queue) ? EnqueuedState.DefaultQueue : queue.Trim().ToLowerInvariant();
    }
}
