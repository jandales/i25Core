using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using i25Core.Core.Events;

namespace i25Core.Infrastructure.Events
{
    /// <summary>Helpers shared by the dispatcher and the queued-listener runner.</summary>
    internal static class EventSupport
    {
        public static readonly JsonSerializerOptions Json = new()
        {
            // Entities can have navigation properties that point back at each other.
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        private static readonly ConcurrentDictionary<Type, MethodInfo> HandleMethods = new();

        /// <summary>Calls IEventListener&lt;handledType&gt;.HandleAsync(event) on the listener.</summary>
        public static Task InvokeAsync(object listener, Type handledType, IEvent @event, CancellationToken cancellationToken)
        {
            var method = HandleMethods.GetOrAdd(handledType, t =>
                typeof(IEventListener<>).MakeGenericType(t).GetMethod(nameof(IEventListener<IEvent>.HandleAsync))!);

            try
            {
                return (Task)method.Invoke(listener, new object[] { @event, cancellationToken })!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // Reflection wraps anything the listener throws before its first await;
                // rethrow the real exception so logs and errors show the actual cause.
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        /// <summary>
        /// Type name without version/culture/token, so a queued job still resolves after a redeploy:
        /// "Ns.Type, Assembly" or "Ns.Generic`1[[Ns.Arg, Assembly]], Assembly".
        /// </summary>
        public static string TypeName(Type type)
        {
            if (!type.IsGenericType)
                return $"{type.FullName}, {type.Assembly.GetName().Name}";

            var definition = type.GetGenericTypeDefinition();
            var args = string.Join(",", type.GetGenericArguments().Select(a => $"[{TypeName(a)}]"));

            return $"{definition.FullName}[{args}], {definition.Assembly.GetName().Name}";
        }
    }
}
