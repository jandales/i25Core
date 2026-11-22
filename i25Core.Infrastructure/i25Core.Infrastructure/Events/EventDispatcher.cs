using System.Reflection;
using System.Text.Json;
using i25Core.Core.BackgroundJobs;
using i25Core.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace i25Core.Infrastructure.Events
{
    /// <summary>
    /// Finds the listeners for an event in the DI container and runs them in order.
    /// Registered as scoped; listeners are resolved lazily so listeners may inject I25DbContext
    /// (the DbContext itself depends on this class, so it must not depend on the DbContext).
    /// </summary>
    public class EventDispatcher : IEventDispatcher
    {
        // Nesting level of the current logical flow (an event raised inside a listener).
        private static readonly AsyncLocal<int> Depth = new();

        private readonly IServiceProvider _services;
        private readonly ILogger<EventDispatcher> _logger;
        private readonly EventOptions _options;

        public EventDispatcher(
            IServiceProvider services,
            ILogger<EventDispatcher> logger,
            IOptions<EventOptions> options)
        {
            _services = services;
            _logger = logger;
            _options = options.Value;
        }

        public async Task DispatchAsync(IEvent @event, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(@event);

            var depth = Depth.Value;

            if (depth >= _options.MaxDepth)
            {
                throw new InvalidOperationException(
                    $"Event nesting is deeper than {_options.MaxDepth} while dispatching {@event.GetType().Name}. " +
                    "A listener probably raises an event that leads back to itself.");
            }

            Depth.Value = depth + 1;

            try
            {
                var errors = new List<Exception>();
                var runtimeType = @event.GetType();

                // Listeners of the exact event type, then of its base types (e.g. EntityEvent).
                for (var type = runtimeType; type != null && typeof(IEvent).IsAssignableFrom(type); type = type.BaseType)
                {
                    var listenerInterface = typeof(IEventListener<>).MakeGenericType(type);

                    foreach (var listener in _services.GetServices(listenerInterface))
                    {
                        try
                        {
                            await RunListenerAsync(listener!, type, runtimeType, @event, cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Listener {Listener} failed for event {Event}",
                                listener!.GetType().Name, runtimeType.Name);
                            errors.Add(ex);
                        }
                    }
                }

                if (errors.Count > 0 && _options.ThrowOnListenerError)
                    throw new AggregateException($"{errors.Count} listener(s) failed for event {runtimeType.Name}.", errors);
            }
            finally
            {
                Depth.Value = depth;
            }
        }

        private async Task RunListenerAsync(
            object listener,
            Type handledType,
            Type runtimeType,
            IEvent @event,
            CancellationToken cancellationToken)
        {
            var listenerType = listener.GetType();
            var queued = listenerType.GetCustomAttribute<QueuedAttribute>();

            if (queued != null)
            {
                var jobs = _services.GetService<IBackgroundJobService>();

                if (jobs != null)
                {
                    var name = $"{listenerType.Name} ← {runtimeType.Name}";
                    var listenerName = EventSupport.TypeName(listenerType);
                    var handledName = EventSupport.TypeName(handledType);
                    var eventName = EventSupport.TypeName(runtimeType);
                    var json = JsonSerializer.Serialize(@event, runtimeType, EventSupport.Json);

                    jobs.Enqueue<QueuedListenerRunner>(
                        runner => runner.RunAsync(name, listenerName, handledName, eventName, json, CancellationToken.None),
                        queued.Queue);

                    return;
                }

                _logger.LogWarning(
                    "{Listener} is [Queued] but background jobs are not registered; running it inline.",
                    listenerType.Name);
            }

            await EventSupport.InvokeAsync(listener, handledType, @event, cancellationToken);
        }
    }
}
