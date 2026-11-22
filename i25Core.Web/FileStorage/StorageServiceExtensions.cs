using i25Core.Core.Storage;
using i25Core.Infrastructure.Storage;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace i25Core.Web.FileStorage
{
    public static class StorageServiceExtensions
    {
        /// <summary>
        /// Registers the disk manager, the default disk (inject <see cref="IFileStorage"/>), the Local driver
        /// and the upload helper, using the "Storage" section of appsettings.json.
        /// To add a driver (S3, Azure...): <c>services.AddSingleton&lt;IStorageDriverFactory, S3DriverFactory&gt;()</c>.
        /// </summary>
        public static IServiceCollection AddI25Storage(
            this IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
            services.PostConfigure<StorageOptions>(o => o.RootPath = environment.ContentRootPath);

            services.AddSingleton<IStorageDriverFactory, LocalStorageDriverFactory>();
            services.AddSingleton<IFileStorageManager, FileStorageManager>();
            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<IFileStorageManager>().Disk());
            services.AddSingleton<IFileUploader, FileUploader>();

            return services;
        }

        /// <summary>
        /// Serves every Local disk that has a path-style Url (for example "/storage") as static files,
        /// so a "public" disk's files are reachable by browsers. Private disks are never served.
        /// </summary>
        public static WebApplication UseI25Storage(this WebApplication app)
        {
            var options = app.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
            var manager = app.Services.GetRequiredService<IFileStorageManager>();
            var served = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Create every configured disk now (this also creates the folders), so a missing Root,
            // an unregistered driver or an unwritable folder stops the app at startup instead of at the first upload.
            foreach (var diskName in manager.DiskNames)
                manager.Disk(diskName);

            foreach (var (name, disk) in options.EffectiveDisks)
            {
                var url = disk.Url?.Trim();

                // No Url = private disk. A full address (https://cdn...) is served by someone else.
                if (string.IsNullOrEmpty(url) || !url.StartsWith('/'))
                    continue;

                if (manager.Disk(name) is not LocalFileStorage local)
                    continue;

                var requestPath = "/" + url.Trim('/');

                if (requestPath == "/" || requestPath.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Storage disk '{name}' has Url '{disk.Url}'. Use a dedicated path such as '/storage'; the root and '/api' are not allowed.");
                }

                if (served.TryGetValue(requestPath, out var other))
                {
                    throw new InvalidOperationException(
                        $"Storage disks '{other}' and '{name}' both use the Url '{requestPath}'. Give each public disk its own path.");
                }

                served[requestPath] = name;

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(local.RootFolder),   // skips dot-files, hidden and system files
                    RequestPath = requestPath,
                    ServeUnknownFileTypes = false,
                    OnPrepareResponse = context =>
                    {
                        // Uploaded files are untrusted: stop browsers from guessing a type or running scripts in them.
                        context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                        context.Context.Response.Headers["Content-Security-Policy"] = "sandbox";
                    }
                });
            }

            return app;
        }
    }
}
