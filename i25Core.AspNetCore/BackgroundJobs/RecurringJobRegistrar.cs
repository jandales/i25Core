using Hangfire;
using i25Core.Core.BackgroundJobs;
using i25Core.Infrastructure.BackgroundJobs;

namespace i25Core.AspNetCore.BackgroundJobs
{
    /// <summary>
    /// At startup, creates or updates a Hangfire recurring job for every class registered with
    /// AddRecurringJob&lt;T&gt;(). Changing a job's Cron in code takes effect on the next start.
    /// </summary>
    public sealed class RecurringJobRegistrar : IHostedService
    {
        private readonly IEnumerable<RecurringJobRegistration> _registrations;
        private readonly IServiceProvider _services;
        private readonly IRecurringJobManager _manager;
        private readonly ILogger<RecurringJobRegistrar> _logger;

        public RecurringJobRegistrar(
            IEnumerable<RecurringJobRegistration> registrations,
            IServiceProvider services,
            IRecurringJobManager manager,
            ILogger<RecurringJobRegistrar> logger)
        {
            _registrations = registrations;
            _services = services;
            _manager = manager;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var registration in _registrations)
            {
                var jobType = registration.JobType;
                var jobId = jobType.Name;

                // Stable type name (no version) so redeploys keep resolving the same class.
                var typeName = $"{jobType.FullName}, {jobType.Assembly.GetName().Name}";

                // Read the schedule from an instance of the job.
                using var scope = _services.CreateScope();
                var job = (IRecurringJob)scope.ServiceProvider.GetRequiredService(jobType);

                _manager.AddOrUpdate<RecurringJobRunner>(
                    jobId,
                    job.Queue.ToLowerInvariant(),
                    runner => runner.RunAsync(jobId, typeName, CancellationToken.None),
                    job.Cron,
                    new RecurringJobOptions { TimeZone = job.TimeZone });

                _logger.LogInformation(
                    "Recurring job {JobId} scheduled with cron '{Cron}' ({TimeZone}) on queue {Queue}",
                    jobId, job.Cron, job.TimeZone.Id, job.Queue);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
