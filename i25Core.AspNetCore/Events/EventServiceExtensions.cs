using System.Reflection;
using i25Core.Core.Events;
using i25Core.Infrastructure.Events;

namespace i25Core.AspNetCore.Events
{
    public static class EventServiceExtensions
    {
        /// <summary>
        /// Registers IEventDispatcher and every IEventListener&lt;&gt; found in the given assemblies.
        /// Call it once in Program.cs: <c>services.AddI25Events(config, typeof(Program).Assembly)</c>.
        /// </summary>
        public static IServiceCollection AddI25Events(
            this IServiceCollection services,
            IConfiguration configuration,
            params Assembly[] listenerAssemblies)
        {
            services.Configure<EventOptions>(configuration.GetSection(EventOptions.SectionName));

            services.AddScoped<IEventDispatcher, EventDispatcher>();
            services.AddTransient<QueuedListenerRunner>();

            foreach (var assembly in listenerAssemblies.Distinct())
                AddListenersFrom(services, assembly);

            return services;
        }

        private static void AddListenersFrom(IServiceCollection services, Assembly assembly)
        {
            var listeners = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

            foreach (var listener in listeners)
            {
                var contracts = listener.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventListener<>))
                    .ToList();

                if (contracts.Count == 0)
                    continue;

                // The concrete type is registered too: queued jobs ask for the listener by its class.
                services.AddScoped(listener);

                // A listener of several events is one object, so each contract points at the same registration.
                foreach (var contract in contracts)
                    services.AddScoped(contract, sp => sp.GetRequiredService(listener));
            }
        }
    }
}
