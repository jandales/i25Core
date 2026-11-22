using Hangfire;
using Hangfire.SqlServer;
using i25Core.Core.BackgroundJobs;
using i25Core.Infrastructure.BackgroundJobs;

namespace i25Core.Web.BackgroundJobs
{
    public static class BackgroundJobServiceExtensions
    {
        /// <summary>
        /// Registers Hangfire (SQL Server storage), IBackgroundJobService, the job server
        /// and the recurring-job registrar, using the "BackgroundJobs" settings.
        /// </summary>
        public static IServiceCollection AddI25BackgroundJobs(
            this IServiceCollection services,
            IConfiguration configuration,
            string connectionString)
        {
            var section = configuration.GetSection(BackgroundJobOptions.SectionName);
            services.Configure<BackgroundJobOptions>(section);
            var options = section.Get<BackgroundJobOptions>() ?? new BackgroundJobOptions();

            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                {
                    SchemaName = options.SchemaName,
                    PrepareSchemaIfNecessary = true,          // creates the HangFire tables on first run
                    QueuePollInterval = TimeSpan.Zero,        // pick up new jobs immediately
                    SqlClientFactory = Microsoft.Data.SqlClient.SqlClientFactory.Instance
                })
                .UseFilter(new AutomaticRetryAttribute { Attempts = options.RetryAttempts }));

            if (options.ServerEnabled)
            {
                services.AddHangfireServer(server =>
                {
                    server.Queues = options.Queues.Select(q => q.ToLowerInvariant()).ToArray();
                    server.SchedulePollingInterval = TimeSpan.FromSeconds(options.SchedulePollingIntervalSeconds);

                    if (options.WorkerCount > 0)
                        server.WorkerCount = options.WorkerCount;
                });
            }

            services.AddSingleton<IBackgroundJobService, HangfireBackgroundJobService>();
            services.AddTransient<RecurringJobRunner>();
            services.AddHostedService<RecurringJobRegistrar>();

            return services;
        }

        /// <summary>
        /// Schedules <typeparamref name="TJob"/> at startup using its Cron, TimeZone and Queue.
        /// (Laravel: $schedule->job(new TJob)->cron(...))
        /// </summary>
        public static IServiceCollection AddRecurringJob<TJob>(this IServiceCollection services)
            where TJob : class, IRecurringJob
        {
            services.AddScoped<TJob>();
            services.AddSingleton(new RecurringJobRegistration(typeof(TJob)));
            return services;
        }

        /// <summary>Maps the Hangfire dashboard (local requests only) when enabled.</summary>
        public static WebApplication UseI25BackgroundJobs(this WebApplication app)
        {
            var options = app.Configuration
                .GetSection(BackgroundJobOptions.SectionName)
                .Get<BackgroundJobOptions>() ?? new BackgroundJobOptions();

            if (options.DashboardEnabled)
            {
                // No Authorization option set → Hangfire's default: only requests from the local machine.
                app.UseHangfireDashboard(options.DashboardPath, new DashboardOptions
                {
                    DashboardTitle = "i25Core Jobs"
                });
            }

            return app;
        }
    }

    public sealed record RecurringJobRegistration(Type JobType);
}
