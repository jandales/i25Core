using System.Text.Json;
using Hangfire;
using i25Core.Core.Events;
using Microsoft.Extensions.DependencyInjection;

namespace i25Core.Infrastructure.Events
{
    /// <summary>
    /// What the job queue runs for a [Queued] listener: rebuilds the event from JSON,
    /// creates the listener in a fresh DI scope, and calls it.
    /// </summary>
    public class QueuedListenerRunner
    {
        private readonly IServiceProvider _services;

        public QueuedListenerRunner(IServiceProvider services)
        {
            _services = services;
        }

        // The first argument is only the label shown in the job dashboard.
        [JobDisplayName("{0}")]
        public async Task RunAsync(
            string displayName,
            string listenerTypeName,
            string handledTypeName,
            string eventTypeName,
            string eventJson,
            CancellationToken cancellationToken)
        {
            var listenerType = Type.GetType(listenerTypeName, throwOnError: true)!;
            var handledType = Type.GetType(handledTypeName, throwOnError: true)!;
            var eventType = Type.GetType(eventTypeName, throwOnError: true)!;

            var @event = (IEvent)JsonSerializer.Deserialize(eventJson, eventType, EventSupport.Json)!;

            using var scope = _services.CreateScope();
            var listener = scope.ServiceProvider.GetRequiredService(listenerType);

            await EventSupport.InvokeAsync(listener, handledType, @event, cancellationToken);
        }
    }
}
