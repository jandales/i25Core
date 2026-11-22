namespace i25Core.Infrastructure.Events
{
    /// <summary>Bound from the "Events" section of appsettings.json.</summary>
    public class EventOptions
    {
        public const string SectionName = "Events";

        /// <summary>
        /// false (default): a failing listener is logged and the remaining listeners still run.
        /// true: after all listeners ran, DispatchAsync throws an AggregateException with the failures.
        /// Automatic model events fire after the save has committed, so they can never undo it either way.
        /// </summary>
        public bool ThrowOnListenerError { get; set; } = false;

        /// <summary>Maximum nesting of events raised from inside listeners. Stops infinite loops.</summary>
        public int MaxDepth { get; set; } = 10;
    }
}
