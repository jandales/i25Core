using Hangfire;
using i25Core.Core.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;

namespace i25Core.Infrastructure.BackgroundJobs
{
    /// <summary>
    /// The method Hangfire actually stores for every IRecurringJob. It resolves the job class
    /// from a fresh DI scope at run time, so jobs can use scoped services such as I25DbContext.
    /// </summary>
    public class RecurringJobRunner
    {
        private readonly IServiceProvider _services;

        public RecurringJobRunner(IServiceProvider services)
        {
            _services = services;
        }

        // Shown in the dashboard as e.g. "AuditCleanupJob".
        [JobDisplayName("{0}")]
        public async Task RunAsync(string jobName, string jobTypeName, CancellationToken cancellationToken)
        {
            var jobType = Type.GetType(jobTypeName, throwOnError: true)!;

            using var scope = _services.CreateScope();
            var job = (IRecurringJob)scope.ServiceProvider.GetRequiredService(jobType);

            await job.ExecuteAsync(cancellationToken);
        }
    }
}
