using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace i25Core.Infrastructure.Extensions
{
    public static class ServiceCollectionExtension
    {
        /// <summary>Registers the i25Core database context (SQL Server).</summary>
        public static IServiceCollection AddI25Infrastructure(this IServiceCollection services, string connectionString)
            => services.AddI25Infrastructure<I25DbContext>(connectionString);

        /// <summary>
        /// Same, for an application's own context that inherits <see cref="I25DbContext"/>. The library's services keep
        /// asking for I25DbContext and receive your context (same instance, same transaction).
        /// </summary>
        public static IServiceCollection AddI25Infrastructure<TContext>(this IServiceCollection services, string connectionString)
            where TContext : I25DbContext
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The connection string \"DefaultConnection\" is missing. Add it under ConnectionStrings in appsettings.json.");

            services.AddDbContext<TContext>(options => options.UseSqlServer(connectionString));

            if (typeof(TContext) != typeof(I25DbContext))
                services.AddScoped<I25DbContext>(sp => sp.GetRequiredService<TContext>());

            return services;
        }
    }
}
