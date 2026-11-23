using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using i25Core.Core.Storage;
using i25Core.Infrastructure;

namespace i25Core.Web.Health
{
    /// <summary>Can we reach the database, and is the schema up to date?</summary>
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly I25DbContext _db;
        public DatabaseHealthCheck(I25DbContext db) => _db = db;

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await _db.Database.SqlQueryRaw<int>("SELECT 1 AS Value").ToListAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return HealthCheckResult.Unhealthy("The database does not answer.", ex);
            }

            // Reachable but behind: the app may fail on the first query that needs a new column.
            var pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();

            return pending == 0
                ? HealthCheckResult.Healthy("The database answers and the schema is up to date.")
                : HealthCheckResult.Degraded($"The database answers, but {pending} migration(s) are not applied.");
        }
    }

    /// <summary>Can every configured disk be read, and can the default disk be written?</summary>
    public sealed class StorageHealthCheck : IHealthCheck
    {
        private readonly IFileStorageManager _storage;
        public StorageHealthCheck(IFileStorageManager storage) => _storage = storage;

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var failed = new List<string>();

            foreach (var name in _storage.DiskNames)
            {
                try
                {
                    // Reading only: a probe file written to a public disk would be downloadable for a moment.
                    await _storage.Disk(name).ListAsync("", recursive: false, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed.Add($"{name} (read: {ex.GetType().Name})");
                }
            }

            // The default disk must also accept writes. The probe is removed right away.
            var probe = $".health/{Guid.NewGuid():N}.tmp";
            try
            {
                var disk = _storage.Disk();
                await disk.PutTextAsync(probe, "ok", overwrite: true, cancellationToken);
                var text = await disk.GetTextAsync(probe, cancellationToken);
                if (text != "ok")
                    failed.Add("default (the probe file came back different)");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed.Add($"default (write: {ex.GetType().Name})");
            }
            finally
            {
                try { await _storage.Disk().DeleteAsync(probe, CancellationToken.None); } catch { /* best effort */ }
            }

            return failed.Count == 0
                ? HealthCheckResult.Healthy("All disks are readable and the default disk is writable.")
                : HealthCheckResult.Unhealthy("Storage problem: " + string.Join("; ", failed));
        }
    }

    /// <summary>Is the job store (Hangfire) reachable, and is a job server running?</summary>
    public sealed class BackgroundJobsHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var storage = JobStorage.Current;
                using (storage.GetConnection()) { }

                // A server that was killed stays in the table for minutes; only recent heartbeats count as running.
                var cutoff = DateTime.UtcNow.AddMinutes(-2);
                var servers = storage.GetMonitoringApi().Servers().Count(s => s.Heartbeat.HasValue && s.Heartbeat.Value > cutoff);

                // The API still works without a worker, so this does not take the instance out of rotation.
                return Task.FromResult(servers > 0
                    ? HealthCheckResult.Healthy($"The job store answers; {servers} job server(s) running.")
                    : HealthCheckResult.Degraded("The job store answers, but no job server is running: queued jobs will wait."));
            }
            catch (Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("The job store does not answer.", ex));
            }
        }
    }
}
