using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace i25Core.Infrastructure.Extensions
{
    public static class ServiceCollectionExtension
    {
        public static IServiceCollection AddI25Infrastructure(this IServiceCollection services, string connectionString)
        {
            services.AddDbContext<I25DbContext>(options =>
                options.UseSqlServer(connectionString));

            // Add other infrastructure services here

            return services;
        }
    }
}
